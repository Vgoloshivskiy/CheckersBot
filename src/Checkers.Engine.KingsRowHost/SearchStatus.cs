using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Checkers.Engine.KingsRowHost;

/// <summary>
/// Reads the status line KingsRow writes while it searches and when it finishes, for example
/// "value=-6, depth 17/19.4/33, 4.8s, 1033 kN/s, pv 25-22 2-6 23-18".
/// KingsRow uses several formats (a plain search, an endgame database draw that lists moves with probabilities,
/// a database win with a conversion count), so everything except the move itself is read leniently.
/// The move is never taken from this text: it comes from the board KingsRow hands back.
/// </summary>
internal sealed class SearchStatus
{
    // A database win or loss has no search score, so a large magnitude stands in for it.
    private const int DatabaseWinValue = 20000;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex ValueRx = new(@"value\s*[=:]?\s*(?<v>[+-]?\d+)", Options);
    private static readonly Regex DepthRx = new(@"depth\s+(?<d>\d+)", Options);
    private static readonly Regex KnodesRx = new(@"(?<k>\d+(?:\.\d+)?)\s*kN/s", Options);
    private static readonly Regex NodesRx = new(@"nodes\s+(?<n>\d+)", Options);
    private static readonly Regex PvRx = new(@"\bpv\s+(?<pv>.+)$", Options);
    private static readonly Regex DtwRx = new(@"\((?<side>black|white)\s+(?<result>wins|loses)\s+in\s+\d+\)", Options);
    private static readonly Regex ConversionRx = new(@"\(conv\.\s*in\s*\d+\)", Options);
    private static readonly Regex DrawListRx = new(@"^\s*\d+[-x]\d+\*?\s*\(\d+\.\d+\)", Options);
    private static readonly Regex MoveTokenRx = new(@"^(?:\d+[-x]\d+(?:[-x]\d+)*|xxxx)$", Options);

    private SearchStatus(
        int value, int depth, double kiloNodesPerSecond, long? nodes, IReadOnlyList<string> pv, bool isDatabaseResult)
    {
        Value = value;
        Depth = depth;
        KiloNodesPerSecond = kiloNodesPerSecond;
        Nodes = nodes;
        Pv = pv;
        IsDatabaseResult = isDatabaseResult;
    }

    /// <summary>KingsRow's value: positive is good for Black, negative good for White.</summary>
    public int Value { get; }

    /// <summary>Search depth KingsRow reports first (the nominal depth). 0 when the line has none.</summary>
    public int Depth { get; }

    public double KiloNodesPerSecond { get; }

    public long? Nodes { get; }

    /// <summary>The principal variation as KingsRow printed it: from-to pairs such as "25-22", or "xxxx".</summary>
    public IReadOnlyList<string> Pv { get; }

    /// <summary>True for the formats KingsRow only prints for endgame database results.</summary>
    public bool IsDatabaseResult { get; }

    /// <summary>
    /// False for an empty status, "Thinking..." (the search has not produced anything yet) and
    /// "Illegal position." (KingsRow rejected the board).
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SearchStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string line = text.Trim();
        if (line.StartsWith("Thinking", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Illegal position", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int value = 0;
        bool hasValue = false;
        bool database = false;

        var valueMatch = ValueRx.Match(line);
        if (valueMatch.Success)
        {
            value = int.Parse(valueMatch.Groups["v"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            hasValue = true;
        }

        // "11-15 (black wins in 12)*": a depth-to-win database result.
        var dtw = DtwRx.Match(line);
        if (dtw.Success)
        {
            database = true;
            if (!hasValue)
            {
                bool blackBetter = dtw.Groups["side"].Value.Equals("black", StringComparison.OrdinalIgnoreCase)
                                   == dtw.Groups["result"].Value.Equals("wins", StringComparison.OrdinalIgnoreCase);
                value = blackBetter ? DatabaseWinValue : -DatabaseWinValue;
                hasValue = true;
            }
        }

        if (ConversionRx.IsMatch(line)) database = true;

        // "11-7* (0.111), 11-6* (0.111), ...": a database draw, listing candidate moves with probabilities.
        if (DrawListRx.IsMatch(line))
        {
            database = true;
            if (!hasValue)
            {
                value = 0;
                hasValue = true;
            }
        }

        int depth = 0;
        var depthMatch = DepthRx.Match(line);
        if (depthMatch.Success)
        {
            depth = int.Parse(depthMatch.Groups["d"].Value, CultureInfo.InvariantCulture);
        }

        double kiloNodes = 0;
        var kiloMatch = KnodesRx.Match(line);
        if (kiloMatch.Success)
        {
            kiloNodes = double.Parse(kiloMatch.Groups["k"].Value, CultureInfo.InvariantCulture);
        }

        long? nodes = null;
        var nodesMatch = NodesRx.Match(line);
        if (nodesMatch.Success)
        {
            nodes = long.Parse(nodesMatch.Groups["n"].Value, CultureInfo.InvariantCulture);
        }

        status = new SearchStatus(value, depth, kiloNodes, nodes, ParsePv(line), database);
        return true;
    }

    /// <summary>The value from the point of view of the side to move. Database draws (+1 or -1) and repetition draws (+3 or -3) count as 0.</summary>
    public int ScoreFor(Side side)
    {
        int magnitude = Math.Abs(Value);
        int value = magnitude == 1 || magnitude == 3 ? 0 : Value;
        return side == Side.Black ? value : -value;
    }

    /// <summary>Nodes searched: KingsRow's own count when it prints one, otherwise its speed times the time taken.</summary>
    public long NodesIn(TimeSpan elapsed)
    {
        if (Nodes is long counted) return counted;

        return (long)(KiloNodesPerSecond * 1000.0 * elapsed.TotalSeconds);
    }

    /// <summary>
    /// True when the answer came from the endgame database. KingsRow does not say so directly, so this
    /// recognizes the database-only status formats, the draw value (+1 / -1) and the win/loss magnitudes,
    /// and only trusts them when the position has few enough pieces for the database to cover it.
    /// </summary>
    public bool IsTablebaseHit(bool withinDatabaseRange)
    {
        if (!withinDatabaseRange) return false;

        int magnitude = Math.Abs(Value);
        return IsDatabaseResult || magnitude == 1 || magnitude > 2000;
    }

    private static IReadOnlyList<string> ParsePv(string line)
    {
        var match = PvRx.Match(line);
        if (!match.Success) return Array.Empty<string>();

        var moves = new List<string>();
        foreach (string token in match.Groups["pv"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!MoveTokenRx.IsMatch(token)) break;
            moves.Add(token);
        }

        return moves;
    }
}
