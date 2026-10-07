using Checkers.Engine.Protocol;

namespace Checkers.Engine;

/// <summary>
/// One engine worker: set a position, then search it. A worker is stateful, so <see cref="EnginePool"/> never lets
/// two requests use the same adapter at once.
/// </summary>
public interface IEngineAdapter : IAsyncDisposable
{
    /// <summary>Facts about the engine. Null until the worker has started.</summary>
    HostInfo? Info { get; }

    /// <summary>Starts the worker and waits until it has warmed up. Calling it again restarts the worker.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Sets the position to analyse, as a canonical PDN string.</summary>
    Task SetPositionAsync(string positionPdn, CancellationToken cancellationToken);

    /// <summary>
    /// Searches the position set last. Cancelling the token makes the worker stop and the call throw
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}

public interface IEngineAdapterFactory
{
    IEngineAdapter Create(int workerId);
}
