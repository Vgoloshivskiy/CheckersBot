using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Checkers;

public static class MoveNotation
{
    /// <summary>Parses 22-18, 22x15 or 22x15x8. The separator style is ignored; only the squares matter.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out int[]? squares)
    {
        squares = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Replace('x', '-').Replace('X', '-').Replace(':', '-').Split('-');
        if (parts.Length < 2) return false;

        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value < 1 || value > 32)
            {
                return false;
            }
            result[i] = value;
        }

        squares = result;
        return true;
    }

    /// <summary>
    /// Finds the legal move that matches the squares. Exact path match first; a bare from/to pair also
    /// matches when exactly one legal move starts and ends there.
    /// </summary>
    public static Move? Find(IReadOnlyList<Move> legal, int[] squares)
    {
        foreach (var move in legal)
        {
            if (SamePath(move.Path, squares)) return move;
        }

        if (squares.Length != 2) return null;

        Move? only = null;
        foreach (var move in legal)
        {
            if (move.From != squares[0] || move.To != squares[1]) continue;
            if (only is not null) return null;   // ambiguous
            only = move;
        }

        return only;
    }

    private static bool SamePath(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
