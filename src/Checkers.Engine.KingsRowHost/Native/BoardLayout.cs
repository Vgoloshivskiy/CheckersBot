namespace Checkers.Engine.KingsRowHost.Native;

/// <summary>
/// One way of laying the 32 playable squares out in KingsRow's 8x8 array. The array's orientation is a convention of
/// the CheckerBoard interface that is easy to get backwards (which corner is [0][0], which index is the column),
/// so the host does not assume one: at startup it tries each candidate and keeps the first that KingsRow answers sensibly
/// for. See <c>HostSession.CalibrateAsync</c>.
/// </summary>
internal sealed class BoardLayout
{
    private readonly int[] _cellOfSquare = new int[33];
    private readonly int[] _squareOfCell = new int[64];

    private BoardLayout(string name, Func<int, int, (int Row, int Column)> map)
    {
        Name = name;

        for (int square = 1; square <= 32; square++)
        {
            // Standard numbering with White at the bottom: row 0 holds squares 1-4 (the top, Black's side).
            int row = (square - 1) / 4;
            int k = (square - 1) % 4;
            int column = row % 2 == 0 ? 2 * k + 1 : 2 * k;

            var (first, second) = map(row, column);
            int cell = first * 8 + second;
            _cellOfSquare[square] = cell;
            _squareOfCell[cell] = square;
        }
    }

    public string Name { get; }

    /// <summary>Index into the 64-int array of a playable square (1-32).</summary>
    public int CellOf(int square) => _cellOfSquare[square];

    /// <summary>The playable square (1-32) at an array index, or 0 for a light square.</summary>
    public int SquareOf(int cell) => _squareOfCell[cell];

    /// <summary>
    /// All eight ways to place the standard board in the array: the four rotations and the four reflections.
    /// The first is the expected one (board[x][y] with x to the right and y up, White at y = 0).
    /// </summary>
    public static readonly BoardLayout[] Candidates =
    [
        new("board[x][y], White at y=0", (r, c) => (c, 7 - r)),
        new("board[x][y], rotated 180 degrees", (r, c) => (7 - c, r)),
        new("board[y][x], White at y=0", (r, c) => (7 - r, c)),
        new("board[y][x], rotated 180 degrees", (r, c) => (r, 7 - c)),
        new("mirrored, White at y=7", (r, c) => (7 - c, 7 - r)),
        new("mirrored, White at y=0", (r, c) => (c, r)),
        new("transposed, mirrored", (r, c) => (7 - r, 7 - c)),
        new("board[row][column], Black at row 0", (r, c) => (r, c)),
    ];
}
