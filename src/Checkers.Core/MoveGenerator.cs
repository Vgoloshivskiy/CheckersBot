namespace Checkers;

/// <summary>
/// English checkers rules: mandatory capture, multi-jump chains must be completed,
/// men capture forward only, and a man that reaches the far row is crowned and its move ends.
/// Validated against published perft counts (7, 49, 302, 1469, 7361, 36768, 179740, 845931).
/// This is only a rules engine: it never searches or evaluates, the engine does that.
/// </summary>
public static class MoveGenerator
{
    // Diagonal directions as (row delta, column delta). Indexes 0-1 move down the board
    // (Black's forward), indexes 2-3 move up (White's forward).
    private static readonly int[] RowDelta = { 1, 1, -1, -1 };
    private static readonly int[] ColDelta = { -1, 1, -1, 1 };

    private static readonly int[] Rows = new int[33];
    private static readonly int[,] Step = new int[33, 4];   // adjacent square, 0 if off board
    private static readonly int[,] Jump = new int[33, 4];   // landing square two steps away, 0 if off board

    static MoveGenerator()
    {
        var grid = new int[8, 8];
        var cols = new int[33];

        for (int s = 1; s <= 32; s++)
        {
            int index = s - 1;
            int row = index / 4;
            int k = index % 4;
            int col = row % 2 == 0 ? 2 * k + 1 : 2 * k;
            Rows[s] = row;
            cols[s] = col;
            grid[row, col] = s;
        }

        for (int s = 1; s <= 32; s++)
        {
            for (int d = 0; d < 4; d++)
            {
                Step[s, d] = Lookup(grid, Rows[s] + RowDelta[d], cols[s] + ColDelta[d]);
                Jump[s, d] = Lookup(grid, Rows[s] + 2 * RowDelta[d], cols[s] + 2 * ColDelta[d]);
            }
        }
    }

    private static int Lookup(int[,] grid, int row, int col)
    {
        if (row < 0 || row > 7 || col < 0 || col > 7) return 0;
        return grid[row, col];
    }

    /// <summary>Row 0 is the top of the board (Black's back rank), row 7 is White's back rank.</summary>
    public static int RowOf(int square) => Rows[square];

    /// <summary>Column 0 is the left edge when White is at the bottom.</summary>
    public static int ColumnOf(int square) => (square - 1) / 4 % 2 == 0 ? 2 * ((square - 1) % 4) + 1 : 2 * ((square - 1) % 4);

    private static bool IsForward(int turn, int direction) => turn == 1 ? direction < 2 : direction >= 2;

    public static List<Move> Generate(Board board)
    {
        var squares = board.CopySquares();
        int turn = (int)board.ToMove;

        var captures = new List<Move>();
        for (int s = 1; s <= 32; s++)
        {
            int piece = squares[s];
            if (piece * turn <= 0) continue;

            // The moving piece leaves its origin so a king may land back on it during a multi-jump.
            squares[s] = 0;
            CollectJumps(squares, turn, s, piece, new List<int> { s }, new List<int>(), captures);
            squares[s] = piece;
        }

        if (captures.Count > 0) return captures;

        var moves = new List<Move>();
        for (int s = 1; s <= 32; s++)
        {
            int piece = squares[s];
            if (piece * turn <= 0) continue;

            bool king = Math.Abs(piece) == 2;
            for (int d = 0; d < 4; d++)
            {
                if (!king && !IsForward(turn, d)) continue;

                int target = Step[s, d];
                if (target != 0 && squares[target] == 0)
                {
                    moves.Add(new Move(new[] { s, target }, Array.Empty<int>()));
                }
            }
        }

        return moves;
    }

    /// <summary>True if the side to move has a capture available.</summary>
    public static bool HasCapture(Board board)
    {
        var moves = Generate(board);
        return moves.Count > 0 && moves[0].IsCapture;
    }

    private static void CollectJumps(
        int[] squares, int turn, int current, int piece, List<int> path, List<int> taken, List<Move> output)
    {
        bool king = Math.Abs(piece) == 2;
        bool jumped = false;

        for (int d = 0; d < 4; d++)
        {
            if (!king && !IsForward(turn, d)) continue;

            int middle = Step[current, d];
            int landing = Jump[current, d];
            if (middle == 0 || landing == 0) continue;

            // Must jump an opposing piece that has not been taken yet, onto an empty square.
            // Taken pieces stay on the board until the move ends, so they block but cannot be jumped twice.
            if (squares[middle] * turn >= 0 || taken.Contains(middle) || squares[landing] != 0) continue;

            jumped = true;
            path.Add(landing);
            taken.Add(middle);

            bool crowns = !king && Rows[landing] == (turn == 1 ? 7 : 0);
            if (crowns)
            {
                output.Add(new Move(path.ToArray(), taken.ToArray()));
            }
            else
            {
                CollectJumps(squares, turn, landing, piece, path, taken, output);
            }

            path.RemoveAt(path.Count - 1);
            taken.RemoveAt(taken.Count - 1);
        }

        if (!jumped && path.Count > 1)
        {
            output.Add(new Move(path.ToArray(), taken.ToArray()));
        }
    }
}
