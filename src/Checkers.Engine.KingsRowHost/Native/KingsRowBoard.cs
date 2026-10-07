namespace Checkers.Engine.KingsRowHost.Native;

/// <summary>
/// Converts between this program's boards and the 8x8 int array of the CheckerBoard engine interface.
/// Cell values are CB_WHITE=1 or CB_BLACK=2, plus CB_MAN=4 or CB_KING=8; 0 is empty.
/// </summary>
internal static class KingsRowBoard
{
    public const int CellCount = 64;

    private const int CbWhite = 1;
    private const int CbBlack = 2;
    private const int CbMan = 4;
    private const int CbKing = 8;

    /// <summary>The array layout in use. Set once, by calibration, before the first real request.</summary>
    public static BoardLayout Layout { get; set; } = BoardLayout.Candidates[0];

    /// <summary>The color value KingsRow's getmove expects for the side to move.</summary>
    public static int Color(Side side) => side == Side.Black ? CbBlack : CbWhite;

    public static int[] ToCells(Board board)
    {
        var cells = new int[CellCount];
        for (int square = 1; square <= 32; square++)
        {
            int piece = board[square];
            if (piece == 0) continue;

            int color = piece > 0 ? CbBlack : CbWhite;
            int kind = Math.Abs(piece) == 2 ? CbKing : CbMan;
            cells[Layout.CellOf(square)] = color | kind;
        }

        return cells;
    }

    /// <summary>
    /// Works out which legal move turns <paramref name="before"/> into <paramref name="after"/> and returns it in
    /// 22-18 / 22x15x8 notation. Returns null if the position after is not the result of any legal move, which means
    /// KingsRow rejected the position or the layout is wrong.
    /// </summary>
    public static string? FindMove(int[] before, int[] after, Side side)
    {
        var board = FromCells(before, side);
        var expected = ReadSquares(after);

        foreach (var move in MoveGenerator.Generate(board))
        {
            var result = board.Apply(move);
            if (Matches(result, expected)) return move.Notation;
        }

        return null;
    }

    public static Board FromCells(int[] cells, Side toMove) => Board.FromSquares(ReadSquares(cells), toMove);

    private static int[] ReadSquares(int[] cells)
    {
        var squares = new int[33];
        for (int cell = 0; cell < CellCount; cell++)
        {
            int value = cells[cell];
            int square = Layout.SquareOf(cell);
            if (value == 0 || square == 0) continue;

            int sign = (value & CbBlack) != 0 ? 1 : -1;
            squares[square] = sign * ((value & CbKing) != 0 ? 2 : 1);
        }

        return squares;
    }

    private static bool Matches(Board board, int[] squares)
    {
        for (int square = 1; square <= 32; square++)
        {
            if (board[square] != squares[square]) return false;
        }

        return true;
    }
}
