namespace Checkers.Application;

/// <summary>Limits a caller may ask for. Anything left null falls back to the level.</summary>
public sealed record LimitsRequest(int? MaxDepth, int? SoftTimeMs, int? HardTimeMs);

public sealed record ResolvedLimits(string Level, int MaxDepth, TimeSpan SoftTime, TimeSpan HardTime);

/// <summary>
/// Turns a strength level plus the optional limits in a request into the limits actually used.
/// The level sets the budget; request limits can only tighten it, never raise it.
/// </summary>
public sealed class LevelPolicy
{
    private readonly LevelsOptions _levels;
    private readonly LimitsOptions _limits;

    public LevelPolicy(LevelsOptions levels, LimitsOptions limits)
    {
        _levels = levels;
        _limits = limits;
    }

    public ResolvedLimits Resolve(string? level, LimitsRequest? requested)
    {
        string name = string.IsNullOrWhiteSpace(level) ? "medium" : level.Trim().ToLowerInvariant();
        LevelOptions preset = name switch
        {
            "weak" => _levels.Weak,
            "medium" => _levels.Medium,
            "strong" => _levels.Strong,
            _ => throw new RequestValidationException(
                "invalid_level", $"Unknown level \"{level}\". Use weak, medium or strong.")
        };

        int maxDepth = preset.MaxDepth;
        int softMs = preset.SoftTimeMs ?? _limits.DefaultSoftTimeMs;
        int hardMs = _limits.DefaultHardTimeMs;

        if (requested?.MaxDepth is int depth)
        {
            RequireRange("limits.maxDepth", depth, 1, 40);
            maxDepth = Math.Min(maxDepth, depth);
        }

        if (requested?.SoftTimeMs is int soft)
        {
            RequireRange("limits.softTimeMs", soft, 10, 60000);
            softMs = Math.Min(softMs, soft);
        }

        if (requested?.HardTimeMs is int hard)
        {
            RequireRange("limits.hardTimeMs", hard, 50, 60000);
            hardMs = Math.Min(hardMs, hard);
        }

        // Leave headroom so the search ends on its own before the hard limit cancels it.
        softMs = Math.Max(10, Math.Min(softMs, (int)(hardMs * 0.8)));

        return new ResolvedLimits(name, maxDepth, TimeSpan.FromMilliseconds(softMs), TimeSpan.FromMilliseconds(hardMs));
    }

    private static void RequireRange(string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            throw new RequestValidationException(
                "invalid_limits", $"{field} must be between {min} and {max}, got {value}.");
        }
    }
}
