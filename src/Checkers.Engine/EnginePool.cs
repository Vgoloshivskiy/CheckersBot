using Checkers.Application;
using Checkers.Engine.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkers.Engine;

public sealed class KingsRowAdapterFactory : IEngineAdapterFactory
{
    private readonly EngineOptions _options;
    private readonly ILoggerFactory _loggers;

    public KingsRowAdapterFactory(IOptions<EngineOptions> options, ILoggerFactory loggers)
    {
        _options = options.Value;
        _loggers = loggers;
    }

    public IEngineAdapter Create(int workerId) =>
        new KingsRowProcessAdapter(_options, workerId, _loggers.CreateLogger<KingsRowProcessAdapter>());
}

/// <summary>
/// A small fixed pool of long-lived engine workers. Requests are spread round-robin and each worker handles one
/// request at a time (async lock), so engines are never started per request.
/// </summary>
public sealed class EnginePool : IEngineGateway, IAsyncDisposable
{
    private sealed class Worker
    {
        public Worker(IEngineAdapter adapter)
        {
            Adapter = adapter;
        }

        public IEngineAdapter Adapter { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private readonly Worker[] _workers;
    private readonly ILogger<EnginePool> _logger;
    private int _next = -1;
    private volatile bool _ready;
    private volatile EngineInfo _info = EngineInfo.Unknown;

    public EnginePool(IEngineAdapterFactory factory, IOptions<EngineOptions> options, ILogger<EnginePool> logger)
    {
        int count = options.Value.Workers;
        if (count < 1 || count > 16)
        {
            throw new InvalidOperationException("Engine:Workers must be between 1 and 16.");
        }

        _logger = logger;
        _workers = new Worker[count];
        for (int i = 0; i < count; i++)
        {
            _workers[i] = new Worker(factory.Create(i + 1));
        }
    }

    public bool IsReady => _ready;

    public int WorkerCount => _workers.Length;

    public EngineInfo Info => _info;

    /// <summary>Starts every worker and waits until all of them have warmed up.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_workers.Select(w => w.Adapter.StartAsync(cancellationToken))).ConfigureAwait(false);

        var infos = _workers.Select(w => w.Adapter.Info).OfType<HostInfo>().ToList();
        var first = infos[0];

        // Use the smallest database any worker reports, so a probe is only attempted where every worker can answer it.
        _info = new EngineInfo(first.Engine, infos.Min(i => i.DatabasePieces), first.About);
        _ready = true;

        _logger.LogInformation(
            "Engine pool ready: {Workers} worker(s), {Engine}, endgame database up to {Pieces} pieces",
            _workers.Length, _info.Name, _info.DatabasePieces);
    }

    public async Task<SearchResponse> SearchAsync(
        string positionPdn, SearchRequest request, CancellationToken cancellationToken)
    {
        if (!_ready)
        {
            throw new EngineUnavailableException("The engine is still starting up.");
        }

        int index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_workers.Length);
        var worker = _workers[index];

        await worker.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await worker.Adapter.SetPositionAsync(positionPdn, cancellationToken).ConfigureAwait(false);
            return await worker.Adapter.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            worker.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.Adapter.DisposeAsync().ConfigureAwait(false);
            worker.Gate.Dispose();
        }
    }
}
