using System.Diagnostics;
using Checkers.Engine.Protocol;

namespace Checkers.Application;

/// <summary>A validated suggest request, ready to run.</summary>
public sealed record PreparedSuggest(
    Board Board,
    string CanonicalPdn,
    IReadOnlyList<Move> LegalMoves,
    ResolvedLimits Limits,
    string CacheKey);

/// <summary>The answer to a suggest request.</summary>
/// <param name="ScoreOrWdl">-1 / 0 / +1 (loss / draw / win for the side to move) for a database answer, otherwise the engine's score.</param>
public sealed record SuggestResult(
    string BestMove,
    IReadOnlyList<string> Pv,
    int ScoreOrWdl,
    int Depth,
    long Nodes,
    string PositionKey,
    bool TablebaseHit,
    long TimeMs,
    bool Cached);

/// <summary>
/// The suggest-a-move use case. It validates the request, asks the engine, checks the engine's move is legal,
/// and caches the answer. It contains no search or evaluation of its own: that is entirely the engine's job.
/// </summary>
public sealed class MoveSuggestService
{
    public const string SupportedGameId = "checkers-8x8";

    private readonly IEngineGateway _engine;
    private readonly LevelPolicy _levels;
    private readonly LimitsOptions _limits;
    private readonly LruTtlCache<string, SuggestResult> _cache;

    public MoveSuggestService(
        IEngineGateway engine,
        LevelPolicy levels,
        LimitsOptions limits,
        LruTtlCache<string, SuggestResult> cache)
    {
        _engine = engine;
        _levels = levels;
        _limits = limits;
        _cache = cache;
    }

    public EngineInfo EngineInfo => _engine.Info;

    /// <summary>Flow step 1: parse and normalize the PDN, validate squares and piece counts, resolve limits.</summary>
    /// <exception cref="RequestValidationException">Anything wrong with the request. Maps to HTTP 422.</exception>
    public PreparedSuggest Prepare(
        string? gameId, string? notation, string? position, string? level, LimitsRequest? limits)
    {
        if (!string.IsNullOrWhiteSpace(gameId)
            && !string.Equals(gameId, SupportedGameId, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException(
                "unsupported_game", $"Unsupported gameId \"{gameId}\". Only \"{SupportedGameId}\" is available.");
        }

        if (string.IsNullOrWhiteSpace(position))
        {
            throw new RequestValidationException("invalid_position", "state.position is required.");
        }

        if (!string.IsNullOrWhiteSpace(notation)
            && !string.Equals(notation, "PDN", StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException(
                "unsupported_notation", $"Unsupported notation \"{notation}\". Only PDN is available.");
        }

        var resolved = _levels.Resolve(level, limits);
        var board = PositionService.Parse(position);

        var legal = MoveGenerator.Generate(board);
        if (legal.Count == 0)
        {
            string loser = board.ToMove == Side.Black ? "Black" : "White";
            throw new RequestValidationException("no_legal_moves", $"{loser} has no legal moves; the game is over.");
        }

        string canonical = board.ToPdn();
        string key = $"{resolved.Level}|{resolved.MaxDepth}|{(int)resolved.SoftTime.TotalMilliseconds}|{canonical}";
        return new PreparedSuggest(board, canonical, legal, resolved, key);
    }

    /// <summary>Flow steps 2 to 5: endgame database attempt, search, legality check, cache.</summary>
    /// <exception cref="EngineUnavailableException">The engine is not ready. Maps to HTTP 503.</exception>
    /// <exception cref="EngineFailureException">The engine returned nothing legal. Maps to HTTP 500.</exception>
    public async Task<SuggestResult> SuggestAsync(PreparedSuggest prepared, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        if (_cache.TryGet(prepared.CacheKey, out var cached))
        {
            return cached with { Cached = true, TimeMs = ElapsedMs(started) };
        }

        if (!_engine.IsReady)
        {
            throw new EngineUnavailableException("The engine is still starting up.");
        }

        SearchResponse? answer = null;

        // Step 2: a position the endgame database covers is answered immediately, so try that first with a
        // very short search. If the database has no entry (for example the position has a capture pending),
        // fall through to a normal search.
        int databasePieces = _engine.Info.DatabasePieces;
        if (databasePieces > 0 && prepared.Board.PieceCount() <= databasePieces)
        {
            var probe = await _engine.SearchAsync(
                prepared.CanonicalPdn, new SearchRequest(_limits.TablebaseProbeMs), cancellationToken);
            if (probe.TablebaseHit) answer = probe;
        }

        // Step 3: search with the limits for the level.
        answer ??= await _engine.SearchAsync(
            prepared.CanonicalPdn,
            new SearchRequest((int)prepared.Limits.SoftTime.TotalMilliseconds, prepared.Limits.MaxDepth),
            cancellationToken);

        // Step 4: the move must be legal; if not, try the moves of the principal variation.
        var chosen = PickLegalMove(answer, prepared.LegalMoves);
        var pv = PvNormalizer.Normalize(prepared.Board, chosen, answer.Pv);

        var result = new SuggestResult(
            chosen.Notation,
            pv,
            answer.TablebaseHit ? Math.Sign(answer.Score) : answer.Score,
            answer.Depth,
            answer.Nodes,
            "pdn:" + prepared.CanonicalPdn,
            answer.TablebaseHit,
            ElapsedMs(started),
            false);

        // Step 5.
        _cache.Set(prepared.CacheKey, result);
        return result;
    }

    private static Move PickLegalMove(SearchResponse answer, IReadOnlyList<Move> legal)
    {
        var candidates = new List<string> { answer.BestMove };
        candidates.AddRange(answer.Pv);

        foreach (string text in candidates)
        {
            if (!MoveNotation.TryParse(text, out var squares)) continue;

            var match = MoveNotation.Find(legal, squares);
            if (match is not null) return match;
        }

        throw new EngineFailureException($"The engine returned no legal move (bestMove \"{answer.BestMove}\").");
    }

    private static long ElapsedMs(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}
