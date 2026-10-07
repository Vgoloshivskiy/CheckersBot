using Checkers.Engine.Protocol;

namespace Checkers.Application;

/// <summary>What is known about the running engine.</summary>
/// <param name="Name">Engine name.</param>
/// <param name="DatabasePieces">Largest piece count covered by the endgame database; 0 means no database.</param>
/// <param name="About">The engine's own description.</param>
public sealed record EngineInfo(string Name, int DatabasePieces, string About)
{
    public static EngineInfo Unknown { get; } = new("unknown", 0, string.Empty);
}

/// <summary>
/// The application's view of the chess engine: give it a position and limits, get a move back.
/// The implementation (a pool of KingsRow worker processes) lives in Checkers.Engine.
/// </summary>
public interface IEngineGateway
{
    /// <summary>True once every worker has started and warmed up.</summary>
    bool IsReady { get; }

    int WorkerCount { get; }

    EngineInfo Info { get; }

    /// <exception cref="EngineUnavailableException">The engine is not running.</exception>
    /// <exception cref="EngineFailureException">The engine failed on this position.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled, for example by the hard time limit.</exception>
    Task<SearchResponse> SearchAsync(string positionPdn, SearchRequest request, CancellationToken cancellationToken);
}
