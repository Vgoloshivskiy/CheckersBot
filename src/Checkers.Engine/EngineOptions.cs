namespace Checkers.Engine;

/// <summary>The "Engine" section of appsettings.json.</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    /// <summary>Only "kingsrow" exists.</summary>
    public string Type { get; set; } = "kingsrow";

    /// <summary>The engine host program. A relative path is relative to the API's own folder.</summary>
    public string HostPath { get; set; } = "engine-host/Checkers.Engine.KingsRowHost.exe";

    /// <summary>
    /// The folder with the KingsRow files, laid out like a CheckerBoard installation: egdb64.dll in this folder,
    /// Kingsrow64.dll (with weights_v4.bin, weights_nk_v4.bin and optionally Kingsrow.odb) in this folder or its "engines" subfolder.
    /// </summary>
    public string? Home { get; set; }

    /// <summary>Overrides where Kingsrow64.dll is, if it is not in <see cref="Home"/> or Home\engines.</summary>
    public string? KingsRowDll { get; set; }

    /// <summary>Overrides where egdb64.dll is, if it is not in <see cref="Home"/> or Home\engines.</summary>
    public string? EgdbDll { get; set; }

    /// <summary>The folder with the endgame database files (for example the unzipped Chinook 8-piece database). Optional.</summary>
    public string? Databases { get; set; }

    /// <summary>Number of KingsRow processes. Each handles one request at a time.</summary>
    public int Workers { get; set; } = 2;

    /// <summary>KingsRow's transposition table size per worker.</summary>
    public int HashMb { get; set; } = 128;

    /// <summary>KingsRow's endgame database cache size per worker.</summary>
    public int DatabaseCacheMb { get; set; } = 256;

    /// <summary>How long a worker may take to start and warm up. Loading a large database cache can be slow.</summary>
    public int StartupTimeoutSeconds { get; set; } = 180;

    /// <summary>After a cancelled request, how long a worker gets to stop its search before it is killed and restarted.</summary>
    public int StopGraceMs { get; set; } = 2000;

    /// <summary>How often the host checks search depth, for requests with a depth cap.</summary>
    public int DepthPollMs { get; set; } = 10;
}
