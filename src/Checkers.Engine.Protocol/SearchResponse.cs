namespace Checkers.Engine.Protocol;

/// <summary>What the engine answered for a position.</summary>
/// <param name="BestMove">The move the engine played, in 22-18 / 22x15x8 notation.</param>
/// <param name="Pv">The engine's principal variation, as it reported it (usually from-to pairs such as 25-22).</param>
/// <param name="Score">Centipawn-style score for the side to move. Large magnitudes (over 2000) mean a seen win or loss.</param>
/// <param name="Depth">Search depth reached; 0 when the answer came straight from an endgame database.</param>
/// <param name="Nodes">Estimated nodes searched.</param>
/// <param name="TablebaseHit">True when the answer came from the endgame database rather than from searching.</param>
public sealed record SearchResponse(
    string BestMove,
    IReadOnlyList<string> Pv,
    int Score,
    int Depth,
    long Nodes,
    bool TablebaseHit);
