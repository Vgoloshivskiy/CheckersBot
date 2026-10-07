namespace Checkers.Engine.Protocol;

/// <summary>Message types the API sends to an engine host process.</summary>
public static class HostMessageTypes
{
    public const string SetPosition = "setPosition";
    public const string Search = "search";

    /// <summary>Asks the running search to stop and return its best move so far. Has no reply of its own.</summary>
    public const string Stop = "stop";
}

/// <summary>One line of JSON on the host's standard input.</summary>
/// <param name="Id">Correlates the reply. Stop messages use 0.</param>
/// <param name="Type">One of <see cref="HostMessageTypes"/>.</param>
/// <param name="Position">PDN position, for setPosition.</param>
/// <param name="Search">Limits, for search.</param>
public sealed record HostRequest(long Id, string Type, string? Position = null, SearchRequest? Search = null);

/// <summary>One line of JSON on the host's standard output.</summary>
/// <param name="Id">The request id this answers. Id 0 with Info is the "ready" message sent once at startup.</param>
/// <param name="Ok">False when <paramref name="Error"/> explains why the request failed.</param>
public sealed record HostResponse(
    long Id,
    bool Ok,
    string? Error = null,
    SearchResponse? Search = null,
    HostInfo? Info = null);

/// <summary>Facts about the engine, sent in the ready message.</summary>
/// <param name="Engine">Engine name as KingsRow reports it.</param>
/// <param name="About">KingsRow's own description, including which endgame database it is using.</param>
/// <param name="DatabasePieces">Largest piece count the configured endgame database covers; 0 if none.</param>
/// <param name="Layout">Which board layout calibration settled on (diagnostic).</param>
public sealed record HostInfo(string Engine, string About, int DatabasePieces, string Layout);
