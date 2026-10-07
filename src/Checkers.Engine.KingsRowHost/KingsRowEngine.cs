using System.Diagnostics;
using System.Runtime.InteropServices.Marshalling;
using Checkers.Engine.KingsRowHost.Native;
using Checkers.Engine.Protocol;

namespace Checkers.Engine.KingsRowHost;

/// <summary>The KingsRow engine of this process: its position and its searches.</summary>
internal sealed class KingsRowEngine
{
    private readonly KingsRowLibrary _library;
    private readonly int _databasePieces;

    // The engine reads and writes these while a search runs, and other threads read the status and set play-now,
    // so they live on the pinned object heap at fixed addresses.
    private readonly int[] _cells = GC.AllocateArray<int>(KingsRowBoard.CellCount, pinned: true);
    private readonly byte[] _status = GC.AllocateArray<byte>(KingsRowLibrary.TextSize, pinned: true);
    private readonly int[] _playNow = GC.AllocateArray<int>(1, pinned: true);

    private Board? _position;

    private KingsRowEngine(KingsRowLibrary library, int databasePieces, string name)
    {
        _library = library;
        _databasePieces = databasePieces;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Sets every engine option, because KingsRow otherwise uses the settings it persisted in the registry.</summary>
    /// <exception cref="InvalidOperationException">The engine rejected an option.</exception>
    public static KingsRowEngine Start(KingsRowLibrary library, string databaseDirectory, int databasePieces)
    {
        var commands = new List<string>
        {
            // The opening book picks among book moves at random.
            "set book 0",
            $"set hashsize {HostSettings.HashMb}",
            $"set dbmbytes {HostSettings.DatabaseCacheMb}",
        };

        // Edit: only point KingsRow at a database folder when there is one. An empty "set dbpath" is not a valid command.
        if (databasePieces > 0)
        {
            commands.Add($"set dbpath {databaseDirectory}");
        }

        commands.Add(
            // Without a database, KingsRow's first search would spend seconds looking for one.
            $"set enable_wld {(databasePieces > 0 ? 1 : 0)}");
        commands.Add($"set max_dbpieces {databasePieces}");
        // One core per worker process.
        commands.Add("set searchthreads 1");
        commands.Add("set enable_mtc 0");

        foreach (var command in commands)
        {
            _ = Command(library, command);
        }

        return new KingsRowEngine(library, databasePieces, Command(library, "name"));
    }

    public void SetPosition(Board board) => _position = board;

    /// <summary>Searches the current position for <see cref="SearchRequest.MoveTimeMs"/>, and no deeper than its depth cap.</summary>
    /// <param name="stopToken">Makes the search return its best move so far.</param>
    /// <exception cref="HostRequestException">There is no position, or the engine returned no move.</exception>
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken stopToken)
    {
        var position = _position ?? throw new HostRequestException("No position has been set.");
        var before = KingsRowBoard.ToCells(position);
        before.CopyTo(_cells);
        Array.Clear(_status);
        Volatile.Write(ref _playNow[0], 0);
        using var stopRegistration = stopToken.Register(PlayNow);

        var started = Stopwatch.GetTimestamp();
        var search = Task.Factory.StartNew(
            () => GetMove(position.ToMove, request.MoveTimeMs),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        if (request.MaxDepth is { } maxDepth)
        {
            await StopAtDepthAsync(search, maxDepth);
        }

        await search;
        var elapsed = Stopwatch.GetElapsedTime(started);

        var text = ReadStatus();
        if (!SearchStatus.TryParse(text, out var status))
        {
            throw new HostRequestException($"KingsRow did not search: '{text}'.");
        }

        // The PV is no fallback: KingsRow writes the one move the board cannot show, a king capture that ends on its
        // start square, as "xxxx".
        var bestMove = KingsRowBoard.FindMove(before, _cells, position.ToMove)
            ?? throw new HostRequestException($"The move KingsRow played cannot be read from the board: '{text}'.");
        return new SearchResponse(
            bestMove,
            status.Pv,
            status.ScoreFor(position.ToMove),
            status.Depth,
            status.NodesIn(elapsed),
            status.IsTablebaseHit(position.PieceCount() <= _databasePieces));
    }

    private static string Command(KingsRowLibrary library, string command) =>
        library.Command(command) ?? throw new InvalidOperationException($"KingsRow rejected the command '{command}'.");

    private unsafe void GetMove(Side side, int moveTimeMs)
    {
        fixed (int* cells = _cells)
        fixed (byte* status = _status)
        fixed (int* playNow = _playNow)
        {
            _library.GetMove(cells, KingsRowBoard.Color(side), moveTimeMs / 1000.0, status, playNow);
        }
    }

    private async Task StopAtDepthAsync(Task search, int maxDepth)
    {
        using var poll = new PeriodicTimer(HostSettings.DepthPollInterval);
        while (await Task.WhenAny(search, poll.WaitForNextTickAsync().AsTask()) != search)
        {
            if (SearchStatus.TryParse(ReadStatus(), out var status) && status.Depth >= maxDepth)
            {
                PlayNow();
                return;
            }
        }
    }

    private void PlayNow() => Volatile.Write(ref _playNow[0], 1);

    private unsafe string ReadStatus()
    {
        fixed (byte* status = _status)
        {
            return AnsiStringMarshaller.ConvertToManaged(status) ?? string.Empty;
        }
    }
}
