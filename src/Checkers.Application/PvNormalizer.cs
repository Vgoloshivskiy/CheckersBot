namespace Checkers.Application;

/// <summary>
/// KingsRow reports its principal variation as bare from-to pairs ("14-23" even for a jump). This replays
/// those pairs on the board and rewrites them in the same notation as bestMove ("14x23", "22x15x8").
/// </summary>
public static class PvNormalizer
{
    public static IReadOnlyList<string> Normalize(Board root, Move best, IReadOnlyList<string> rawPv)
    {
        var result = new List<string> { best.Notation };

        // Only trust the engine's line if it starts with the move we are returning.
        if (rawPv.Count == 0 || !TryResolve(root, rawPv[0], out var first) || first.Notation != best.Notation)
        {
            return result;
        }

        var current = root.Apply(best);
        for (int i = 1; i < rawPv.Count; i++)
        {
            if (!TryResolve(current, rawPv[i], out var move)) break;

            result.Add(move.Notation);
            current = current.Apply(move);
        }

        return result;
    }

    private static bool TryResolve(Board board, string token, out Move move)
    {
        move = null!;
        if (!MoveNotation.TryParse(token, out var squares)) return false;

        var found = MoveNotation.Find(MoveGenerator.Generate(board), squares);
        if (found is null) return false;

        move = found;
        return true;
    }
}
