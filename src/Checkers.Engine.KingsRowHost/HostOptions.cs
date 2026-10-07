namespace Checkers.Engine.KingsRowHost;

/// <summary>Command-line settings the API passes to the host: --home, --db, --hash-mb and so on.</summary>
internal sealed record HostOptions(
    string Home,
    string KingsRowDll,
    string EgdbDll,
    string? DatabaseDirectory,
    int HashMb,
    int DatabaseCacheMb,
    TimeSpan DepthPoll)
{
    /// <exception cref="ArgumentException">A setting is missing or points at something that does not exist.</exception>
    public static HostOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'.");
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for '{arg}'.");
            }

            values[arg.Substring(2)] = args[++i];
        }

        if (!values.TryGetValue("home", out var homeArg) || string.IsNullOrWhiteSpace(homeArg))
        {
            throw new ArgumentException("--home is required: the folder that holds the KingsRow files.");
        }

        string home = Path.GetFullPath(homeArg);
        if (!Directory.Exists(home))
        {
            throw new ArgumentException($"The KingsRow folder '{home}' does not exist.");
        }

        string kingsRow = values.TryGetValue("dll", out var dll) && !string.IsNullOrWhiteSpace(dll)
            ? Path.GetFullPath(dll)
            : Find(home, "Kingsrow64.dll");
        string egdb = values.TryGetValue("egdb", out var egdbArg) && !string.IsNullOrWhiteSpace(egdbArg)
            ? Path.GetFullPath(egdbArg)
            : Find(home, "egdb64.dll");

        values.TryGetValue("db", out var db);

        return new HostOptions(
            home,
            kingsRow,
            egdb,
            string.IsNullOrWhiteSpace(db) ? null : db,
            GetInt(values, "hash-mb", 128),
            GetInt(values, "db-cache-mb", 256),
            TimeSpan.FromMilliseconds(GetInt(values, "depth-poll-ms", 10)));
    }

    // A CheckerBoard installation keeps egdb64.dll in its own folder and the engine files in an "engines" subfolder.
    private static string Find(string home, string fileName)
    {
        string[] candidates =
        [
            Path.Combine(home, "engines", fileName),
            Path.Combine(home, fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new ArgumentException(
            $"{fileName} was not found. Looked in: {string.Join("; ", candidates)}.");
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var text)) return fallback;

        if (!int.TryParse(text, out int value) || value < 1)
        {
            throw new ArgumentException($"--{key} must be a positive whole number, got '{text}'.");
        }

        return value;
    }
}

/// <summary>Settings that <see cref="KingsRowEngine"/> reads. Set once at startup.</summary>
internal static class HostSettings
{
    public static int HashMb { get; private set; } = 128;

    public static int DatabaseCacheMb { get; private set; } = 256;

    /// <summary>How often a search with a depth cap checks how deep KingsRow has got.</summary>
    public static TimeSpan DepthPollInterval { get; private set; } = TimeSpan.FromMilliseconds(10);

    public static void Apply(HostOptions options)
    {
        HashMb = options.HashMb;
        DatabaseCacheMb = options.DatabaseCacheMb;
        DepthPollInterval = options.DepthPoll;
    }
}
