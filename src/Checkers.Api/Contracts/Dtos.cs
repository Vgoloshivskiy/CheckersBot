namespace Checkers.Api.Contracts;

// ---- POST /v1/move/suggest ------------------------------------------------------------------

public sealed class SuggestRequest
{
    public string? GameId { get; set; }
    public PositionState? State { get; set; }
    public string? Level { get; set; }
    public LimitsDto? Limits { get; set; }
}

public sealed class PositionState
{
    public string? Notation { get; set; }
    public string? Position { get; set; }
}

public sealed class LimitsDto
{
    public int? MaxDepth { get; set; }
    public int? SoftTimeMs { get; set; }
    public int? HardTimeMs { get; set; }
}

public sealed record SuggestResponse(
    string Engine,
    string BestMove,
    IReadOnlyList<string> Pv,
    int ScoreOrWDL,
    int Depth,
    long Nodes,
    string PositionKey,
    SuggestInfo Info);

public sealed record SuggestInfo(bool TablebaseHit, long TimeMs, bool Cached);

// ---- POST /v1/move/validate -----------------------------------------------------------------

public sealed class ValidateRequest
{
    public string? Position { get; set; }
    public string? Move { get; set; }
}

public sealed record ValidateResponse(bool Legal, string? Reason);

// ---- POST /v1/move/legal (extra endpoint used by the test board) ----------------------------

public sealed class LegalMovesRequest
{
    public string? Position { get; set; }
}

public sealed record LegalMoveDto(string Move, int[] Path, int[] Captured, string Result);

public sealed record LegalMovesResponse(
    string Position,
    string SideToMove,
    IReadOnlyList<LegalMoveDto> Moves,
    bool GameOver,
    string? Winner);
