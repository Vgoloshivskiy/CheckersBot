using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Checkers;

/// <summary>
/// Parses PDN position strings such as "B:W18,19,K22:B1,5,6" (side to move, then the two piece lists in
/// either order). Kings take a K prefix and runs such as "21-32" are accepted.
/// </summary>
public static class Pdn
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out Board? board, out string? error)
    {
        board = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Position is empty.";
            return false;
        }

        var parts = text.Trim().TrimEnd('.').Split(':');
        if (parts.Length != 3)
        {
            error = "Expected three ':'-separated sections like \"B:W18,19:B1,5\" (side to move, White pieces, Black pieces).";
            return false;
        }

        Side turn;
        switch (parts[0].Trim().ToUpperInvariant())
        {
            case "B":
                turn = Side.Black;
                break;
            case "W":
                turn = Side.White;
                break;
            default:
                error = $"Side to move must be B or W, got \"{parts[0].Trim()}\".";
                return false;
        }

        var squares = new int[33];
        var seenColors = new HashSet<char>();

        for (int i = 1; i <= 2; i++)
        {
            string section = parts[i].Trim();
            if (section.Length == 0)
            {
                error = "A piece section is empty.";
                return false;
            }

            char color = char.ToUpperInvariant(section[0]);
            if (color != 'W' && color != 'B')
            {
                error = $"Piece sections must start with W or B, got \"{section[0]}\".";
                return false;
            }

            if (!seenColors.Add(color))
            {
                error = "Both piece sections use the same color.";
                return false;
            }

            string colorName = color == 'B' ? "Black" : "White";
            int sign = color == 'B' ? 1 : -1;
            string list = section.Substring(1).Trim();
            int count = 0;

            if (list.Length > 0)
            {
                foreach (string rawToken in list.Split(','))
                {
                    string token = rawToken.Trim();
                    bool king = token.StartsWith('K') || token.StartsWith('k');
                    if (king) token = token.Substring(1);

                    int first = 0;
                    int last = 0;
                    bool ok;
                    int dash = token.IndexOf('-');
                    if (dash >= 0)
                    {
                        ok = TryParseSquare(token.Substring(0, dash), out first)
                             && TryParseSquare(token.Substring(dash + 1), out last);
                    }
                    else
                    {
                        ok = TryParseSquare(token, out first);
                        last = first;
                    }

                    if (!ok || first > last)
                    {
                        error = $"\"{rawToken.Trim()}\" is not a square (1-32) or a run like 21-32.";
                        return false;
                    }

                    for (int s = first; s <= last; s++)
                    {
                        if (squares[s] != 0)
                        {
                            error = $"Square {s} is occupied twice.";
                            return false;
                        }

                        bool onFarRow = color == 'B' ? s >= 29 : s <= 4;
                        if (!king && onFarRow)
                        {
                            error = $"{colorName} man on square {s} is on the far row, so it must be a king (K{s}).";
                            return false;
                        }

                        squares[s] = sign * (king ? 2 : 1);
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                error = $"{colorName} has no pieces.";
                return false;
            }

            if (count > 12)
            {
                error = $"{colorName} has {count} pieces; the maximum is 12.";
                return false;
            }
        }

        board = Board.FromSquares(squares, turn);
        return true;
    }

    private static bool TryParseSquare(string text, out int square)
    {
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out square)
               && square >= 1 && square <= 32;
    }
}
