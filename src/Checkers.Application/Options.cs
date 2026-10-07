namespace Checkers.Application;

public sealed class CacheOptions
{
    public int Capacity { get; set; } = 20000;
    public int TtlMinutes { get; set; } = 15;
}

public sealed class LimitsOptions
{
    /// <summary>Soft time used when a level does not define its own.</summary>
    public int DefaultSoftTimeMs { get; set; } = 300;

    /// <summary>Upper bound for the hard time. Requests can lower it but not raise it.</summary>
    public int DefaultHardTimeMs { get; set; } = 1200;

    /// <summary>
    /// Search time for the endgame database attempt. A position that is in the database is answered
    /// immediately, so this only bounds the cost when it turns out not to be.
    /// </summary>
    public int TablebaseProbeMs { get; set; } = 40;
}

public sealed class LevelOptions
{
    public int MaxDepth { get; set; } = 12;
    public int? SoftTimeMs { get; set; }
}

public sealed class LevelsOptions
{
    public LevelOptions Weak { get; set; } = new() { MaxDepth = 8, SoftTimeMs = 100 };
    public LevelOptions Medium { get; set; } = new() { MaxDepth = 12, SoftTimeMs = 250 };
    public LevelOptions Strong { get; set; } = new() { MaxDepth = 18, SoftTimeMs = 500 };
}
