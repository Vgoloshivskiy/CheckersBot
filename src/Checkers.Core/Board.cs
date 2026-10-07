namespace Checkers;

/// <summary>
/// An 8x8 English checkers position using the standard 1..32 square numbering, plus the side to move.
/// Immutable: <see cref="Apply"/> returns a new board.
/// </summary>
public sealed class Board : IEquatable<Board>
{
    // Index 1..32 (0 unused). Positive = Black, negative = White. Magnitude 1 = man, 2 = king.
    private readonly int[] _squares;

    private Board(int[] squares, Side toMove)
    {
        _squares = squares;
        ToMove = toMove;
    }

    public Side ToMove { get; }

    public int this[int square] => _squares[square];

    public static Board FromSquares(int[] squares, Side toMove)
    {
        if (squares.Length != 33)
        {
            throw new ArgumentException("Expected an array of 33 entries (index 0 unused).", nameof(squares));
        }

        return new Board((int[])squares.Clone(), toMove);
    }

    public static Board Initial()
    {
        var squares = new int[33];
        for (int s = 1; s <= 12; s++) squares[s] = 1;
        for (int s = 21; s <= 32; s++) squares[s] = -1;
        return new Board(squares, Side.Black);
    }

    public int[] CopySquares() => (int[])_squares.Clone();

    public int PieceCount()
    {
        int count = 0;
        for (int s = 1; s <= 32; s++)
        {
            if (_squares[s] != 0) count++;
        }
        return count;
    }

    public Board Apply(Move move)
    {
        var squares = (int[])_squares.Clone();
        int piece = squares[move.From];
        squares[move.From] = 0;
        foreach (int captured in move.Captured)
        {
            squares[captured] = 0;
        }

        int crownRow = ToMove == Side.Black ? 7 : 0;
        if (Math.Abs(piece) == 1 && MoveGenerator.RowOf(move.To) == crownRow)
        {
            piece *= 2;
        }

        squares[move.To] = piece;
        return new Board(squares, ToMove.Opposite());
    }

    /// <summary>Canonical PDN: side to move, then White and Black pieces sorted ascending, kings prefixed with K.</summary>
    public string ToPdn()
    {
        var white = new List<string>();
        var black = new List<string>();
        for (int s = 1; s <= 32; s++)
        {
            int piece = _squares[s];
            if (piece == 0) continue;

            string token = (Math.Abs(piece) == 2 ? "K" : "") + s;
            if (piece > 0) black.Add(token);
            else white.Add(token);
        }

        return $"{ToMove.ToPdnLetter()}:W{string.Join(",", white)}:B{string.Join(",", black)}";
    }

    public bool Equals(Board? other)
    {
        if (other is null || other.ToMove != ToMove) return false;

        for (int s = 1; s <= 32; s++)
        {
            if (_squares[s] != other._squares[s]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Board);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ToMove);
        for (int s = 1; s <= 32; s++)
        {
            hash.Add(_squares[s]);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => ToPdn();
}
