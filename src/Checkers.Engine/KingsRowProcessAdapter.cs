using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Checkers.Application;
using Checkers.Engine.Protocol;
using Microsoft.Extensions.Logging;

namespace Checkers.Engine;

/// <summary>
/// Talks to one KingsRow host process (Checkers.Engine.KingsRowHost.exe) over its standard input and output.
/// The process is long-lived: it is started once, warmed up, and reused for every request. If it dies it is
/// started again by the next request. One request is in flight at a time (the pool guarantees that).
/// </summary>
public sealed class KingsRowProcessAdapter : IEngineAdapter
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly EngineOptions _options;
    private readonly int _workerId;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();

    private Process? _process;
    private StreamWriter? _input;
    private TaskCompletionSource<HostResponse>? _pending;
    private long _pendingId = -1;
    private long _nextId;
    private bool _faulted = true;

    public KingsRowProcessAdapter(EngineOptions options, int workerId, ILogger logger)
    {
        _options = options;
        _workerId = workerId;
        _logger = logger;
    }

    public HostInfo? Info { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StopProcessAsync().ConfigureAwait(false);

        string hostPath = ResolveHostPath();
        if (!File.Exists(hostPath))
        {
            throw new EngineUnavailableException(
                $"The engine host was not found at '{hostPath}'. Publish Checkers.Engine.KingsRowHost and set Engine:HostPath.");
        }

        if (string.IsNullOrWhiteSpace(_options.Home))
        {
            throw new EngineUnavailableException("Engine:Home is not set. It must point at the folder with the KingsRow files.");
        }

        var startInfo = new ProcessStartInfo(hostPath)
        {
            WorkingDirectory = Path.GetDirectoryName(hostPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };
        AddArgument(startInfo, "--home", _options.Home);
        AddArgument(startInfo, "--dll", _options.KingsRowDll);
        AddArgument(startInfo, "--egdb", _options.EgdbDll);
        AddArgument(startInfo, "--db", _options.Databases);
        AddArgument(startInfo, "--hash-mb", _options.HashMb.ToString());
        AddArgument(startInfo, "--db-cache-mb", _options.DatabaseCacheMb.ToString());
        AddArgument(startInfo, "--depth-poll-ms", _options.DepthPollMs.ToString());

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw new EngineUnavailableException($"Could not start '{hostPath}'.");
        }
        catch (Win32Exception ex)
        {
            throw new EngineUnavailableException($"Could not start '{hostPath}': {ex.Message}", ex);
        }

        var ready = new TaskCompletionSource<HostResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _process = process;
            _pending = ready;
            _pendingId = 0;
            _faulted = false;
        }

        _input = process.StandardInput;
        _input.AutoFlush = true;
        _input.NewLine = "\n";

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) _logger.LogInformation("kingsrow[{Worker}] {Line}", _workerId, e.Data);
        };
        process.BeginErrorReadLine();
        _ = Task.Run(() => ReadLoopAsync(process));

        try
        {
            var hello = await ready.Task
                .WaitAsync(TimeSpan.FromSeconds(_options.StartupTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);

            if (!hello.Ok || hello.Info is null)
            {
                Kill();
                throw new EngineUnavailableException($"The KingsRow host failed to start: {hello.Error ?? "no details"}");
            }

            Info = hello.Info;
            _logger.LogInformation(
                "Engine worker {Worker} ready: {Engine}, database up to {Pieces} pieces, board layout \"{Layout}\"",
                _workerId, hello.Info.Engine, hello.Info.DatabasePieces, hello.Info.Layout);
        }
        catch (TimeoutException)
        {
            Kill();
            throw new EngineUnavailableException(
                $"The KingsRow host did not become ready within {_options.StartupTimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw;
        }
    }

    public Task SetPositionAsync(string positionPdn, CancellationToken cancellationToken) =>
        RequestAsync(HostMessageTypes.SetPosition, positionPdn, null, cancellationToken);

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var response = await RequestAsync(HostMessageTypes.Search, null, request, cancellationToken).ConfigureAwait(false);
        return response.Search ?? throw new EngineFailureException("The engine answered without a result.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task<HostResponse> RequestAsync(
        string type, string? position, SearchRequest? search, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        long id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<HostResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending = pending;
            _pendingId = id;
        }

        try
        {
            await WriteLineAsync(ProtocolJson.Serialize(new HostRequest(id, type, position, search))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Kill();
            throw new EngineUnavailableException("Could not send the request to the engine process.", ex);
        }

        HostResponse response;
        try
        {
            response = await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbortAsync(pending.Task).ConfigureAwait(false);
            throw;
        }

        if (!response.Ok)
        {
            throw new EngineFailureException(response.Error ?? "The engine reported an error.");
        }

        return response;
    }

    /// <summary>
    /// A request was cancelled, usually by the hard time limit. Ask the host to stop searching so the worker stays usable;
    /// if it does not answer in time, kill it. The next request restarts it.
    /// </summary>
    private async Task AbortAsync(Task<HostResponse> pending)
    {
        try
        {
            await WriteLineAsync(ProtocolJson.Serialize(new HostRequest(0, HostMessageTypes.Stop))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Kill();
            return;
        }

        var finished = await Task.WhenAny(pending, Task.Delay(_options.StopGraceMs)).ConfigureAwait(false);
        if (finished != pending)
        {
            _logger.LogWarning(
                "Engine worker {Worker} did not stop within {Ms} ms; restarting it", _workerId, _options.StopGraceMs);
            Kill();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        bool running;
        lock (_gate)
        {
            running = !_faulted && _process is { HasExited: false };
        }

        if (running) return;

        _logger.LogWarning("Starting engine worker {Worker}", _workerId);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                HostResponse? response;
                try
                {
                    response = ProtocolJson.Deserialize<HostResponse>(line);
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Engine worker {Worker} sent something that is not a message: {Line}", _workerId, line);
                    continue;
                }

                if (response is null) continue;

                TaskCompletionSource<HostResponse>? target = null;
                lock (_gate)
                {
                    if (ReferenceEquals(_process, process) && _pending is not null && response.Id == _pendingId)
                    {
                        target = _pending;
                        _pending = null;
                    }
                }

                target?.TrySetResult(response);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Reading from engine worker {Worker} ended", _workerId);
        }
        finally
        {
            TaskCompletionSource<HostResponse>? orphan = null;
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _faulted = true;
                    orphan = _pending;
                    _pending = null;
                }
            }

            orphan?.TrySetException(new EngineUnavailableException("The engine process exited."));
        }
    }

    private async Task WriteLineAsync(string line)
    {
        var input = _input ?? throw new EngineUnavailableException("The engine process is not running.");

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await input.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void Kill()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _faulted = true;
        }

        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    private async Task StopProcessAsync()
    {
        Process? process;
        StreamWriter? input;
        lock (_gate)
        {
            process = _process;
            input = _input;
            _process = null;
            _input = null;
            _faulted = true;
            _pending?.TrySetException(new EngineUnavailableException("The engine process was stopped."));
            _pending = null;
        }

        if (process is null) return;

        try
        {
            // Closing the host's input is its signal to exit.
            input?.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception inner) when (inner is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private string ResolveHostPath()
    {
        string path = _options.HostPath;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
