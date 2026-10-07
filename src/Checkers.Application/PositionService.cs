namespace Checkers.Application;

public sealed record ValidationResult(bool Legal, string? Reason);

public sealed record LegalMoveInfo(string Move, int[] Path, int[] Captured, string ResultPosition);

public sealed record LegalMovesResult(
    string Position,
    Side SideToMove,
    IReadOnlyList<LegalMoveInfo> Moves,
    bool GameOver,
    Side? Winner);

/// <summary>Rules questions about a position. Answered by the rules code in Checkers.Core; the engine is not involved.</summary>
public sealed class PositionService
{
    /// <exception cref="RequestValidationException">The position is not valid PDN.</exception>
    public ValidationResult Validate(string? position, string? move)
    {
        var board = Parse(position);

        if (!MoveNotation.TryParse(move, out var squares))
        {
            return new ValidationResult(false, "Move must look like 22-18 or 22x15x8.");
        }

        var legal = MoveGenerator.Generate(board);
        if (MoveNotation.Find(legal, squares) is not null)
        {
            return new ValidationResult(true, null);
        }

        string reason = legal.Count == 0
            ? "There are no legal moves; the game is over."
            : $"Not a legal move here. Legal moves: {string.Join(", ", legal.Select(m => m.Notation))}.";
        return new ValidationResult(false, reason);
    }

    /// <exception cref="RequestValidationException">The position is not valid PDN.</exception>
    public LegalMovesResult GetLegalMoves(string? position)
    {
        var board = Parse(position);

        var moves = MoveGenerator.Generate(board)
            .Select(m => new LegalMoveInfo(m.Notation, m.Path, m.Captured, board.Apply(m).ToPdn()))
            .ToList();

        bool gameOver = moves.Count == 0;
        return new LegalMovesResult(
            board.ToPdn(),
            board.ToMove,
            moves,
            gameOver,
            gameOver ? board.ToMove.Opposite() : null);
    }

    internal static Board Parse(string? position)
    {
        if (!Pdn.TryParse(position, out var board, out var error))
        {
            throw new RequestValidationException("invalid_position", error ?? "Invalid position.");
        }

        return board;
    }
}
