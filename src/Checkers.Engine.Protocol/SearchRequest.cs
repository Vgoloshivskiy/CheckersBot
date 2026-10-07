namespace Checkers.Engine.Protocol;

/// <summary>How long and how deep the engine may search the current position.</summary>
/// <param name="MoveTimeMs">Search time. The engine stops at this time and plays its best move so far.</param>
/// <param name="MaxDepth">Optional depth cap. The engine stops as soon as it has searched this deep.</param>
public sealed record SearchRequest(int MoveTimeMs, int? MaxDepth = null);
