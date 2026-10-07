using Checkers.Engine.KingsRowHost.Native;
using Checkers.Engine.Protocol;

namespace Checkers.Engine.KingsRowHost;

/// <summary>Starts KingsRow and answers the API's requests for it.</summary>
internal sealed class HostSession
{
    // Long enough for KingsRow to settle on a move in the opening position, short enough that trying
    // all eight layouts costs about a second.
    private const int CalibrationTimeMs = 100;

    private readonly KingsRowEngine _engine;
    private readonly HostChannel _channel;
    private readonly object _stopGate = new();
    private CancellationTokenSource? _search;
    private int _busy;

    private HostSession(KingsRowEngine engine, HostChannel channel, HostInfo info)
    {
        _engine = engine;
        _channel = channel;
        Info = info;
    }

    public HostInfo Info { get; }

    /// <summary>
    /// Loads the DLLs, configures KingsRow, finds out how its board array is laid out and warms it up with real searches.
    /// The first search also makes KingsRow load its endgame database, which can take a while.
    /// </summary>
    public static async Task<HostSession> StartAsync(HostOptions options, HostChannel channel)
    {
        // KingsRow looks for its weights, opening book and log relative to the current directory.
        Directory.SetCurrentDirectory(options.Home);

        // Kingsrow64.dll imports egdb64.dll. Loading egdb64.dll first, by full path, makes Windows reuse this copy.
        var egdb = EgdbLibrary.Load(options.EgdbDll);

        int databasePieces = 0;
        string databaseDirectory = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.DatabaseDirectory))
        {
            string directory = Path.GetFullPath(options.DatabaseDirectory);
            if (egdb.TryIdentify(directory, out int type, out int maxPieces))
            {
                databaseDirectory = directory;
                databasePieces = maxPieces;
                Console.Error.WriteLine($"endgame database: type {type}, {maxPieces} pieces, in {directory}");
            }
            else
            {
                Console.Error.WriteLine($"warning: no endgame database found in {directory}; continuing without one");
            }
        }

        var library = KingsRowLibrary.Load(options.KingsRowDll);
        var engine = KingsRowEngine.Start(library, databaseDirectory, databasePieces);
        string layout = await CalibrateAsync(engine);

        string about = library.Command("about") ?? string.Empty;
        Console.Error.WriteLine(about.Replace('\n', ' ').Trim());

        // KingsRow says what it ended up using once it has searched. Believe it over our own guess.
        if (databasePieces > 0 && about.Contains("Not using an endgame database", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("warning: KingsRow reports it is not using the endgame database");
            databasePieces = 0;
        }

        return new HostSession(engine, channel, new HostInfo(engine.Name, about, databasePieces, layout));
    }

    /// <summary>Asks the running search, if any, to stop and return its best move so far.</summary>
    public void Stop()
    {
        lock (_stopGate)
        {
            _search?.Cancel();
        }
    }

    /// <summary>Handles one request and sends the reply. Requests are handled one at a time.</summary>
    public async Task HandleAsync(HostRequest request)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            await _channel.SendAsync(new HostResponse(request.Id, false, "The engine is busy with another request."));
            return;
        }

        HostResponse response;
        try
        {
            response = await ExecuteAsync(request);
        }
        catch (HostRequestException ex)
        {
            response = new HostResponse(request.Id, false, ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            response = new HostResponse(request.Id, false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }

        await _channel.SendAsync(response);
    }

    private async Task<HostResponse> ExecuteAsync(HostRequest request)
    {
        switch (request.Type)
        {
            case HostMessageTypes.SetPosition:
                if (!Pdn.TryParse(request.Position, out var board, out var error))
                {
                    throw new HostRequestException($"Invalid position: {error}");
                }

                _engine.SetPosition(board);
                return new HostResponse(request.Id, true);

            case HostMessageTypes.Search:
                var limits = request.Search ?? throw new HostRequestException("A search request needs limits.");
                using (var cancellation = new CancellationTokenSource())
                {
                    lock (_stopGate)
                    {
                        _search = cancellation;
                    }

                    try
                    {
                        var answer = await _engine.SearchAsync(limits, cancellation.Token);
                        return new HostResponse(request.Id, true, Search: answer);
                    }
                    finally
                    {
                        lock (_stopGate)
                        {
                            _search = null;
                        }
                    }
                }

            default:
                throw new HostRequestException($"Unknown request type '{request.Type}'.");
        }
    }

    /// <summary>
    /// Finds the board layout KingsRow uses. Each candidate is tried on two real positions, one per side to move;
    /// the right one is the layout for which KingsRow's reply is a legal move. A wrong layout either makes KingsRow
    /// reject the position or produces a board that is not the result of any legal move.
    /// </summary>
    private static async Task<string> CalibrateAsync(KingsRowEngine engine)
    {
        var positions = new[]
        {
            Board.Initial(),
            Parse("W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,13"),
        };

        var failures = new List<string>();
        foreach (var layout in BoardLayout.Candidates)
        {
            KingsRowBoard.Layout = layout;
            try
            {
                foreach (var position in positions)
                {
                    engine.SetPosition(position);
                    await engine.SearchAsync(new SearchRequest(CalibrationTimeMs), CancellationToken.None);
                }

                Console.Error.WriteLine($"board layout: {layout.Name}");
                return layout.Name;
            }
            catch (HostRequestException ex)
            {
                failures.Add($"{layout.Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "KingsRow's replies do not fit any known board layout, so its moves cannot be read. "
            + string.Join(" | ", failures));
    }

    private static Board Parse(string pdn)
    {
        if (!Pdn.TryParse(pdn, out var board, out var error))
        {
            throw new InvalidOperationException($"Internal position '{pdn}' is invalid: {error}");
        }

        return board;
    }
}
