namespace ObsidianQuickNoteWidget.Core.Notes;

/// <summary>
/// Given a desired vault-relative note path and a "does this exist?" probe,
/// returns a path with `-2`, `-3`, ... suffix applied until unique.
/// </summary>
public static class DuplicateFilenameResolver
{
    private const int MaxAttempts = 1000;

    public static string ResolveUnique(string folder, string stem, string extension, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (string.IsNullOrEmpty(extension)) extension = ".md";
        if (!extension.StartsWith('.')) extension = "." + extension;

        string Combine(string s) =>
            string.IsNullOrEmpty(folder) ? s + extension : $"{folder}/{s}{extension}";

        var candidate = Combine(stem);
        if (!exists(candidate)) return candidate;

        for (var i = 2; i <= MaxAttempts; i++)
        {
            candidate = Combine($"{stem}-{i}");
            if (!exists(candidate)) return candidate;
        }

        // Gave up — fall back to timestamp suffix (virtually always unique).
        return Combine($"{stem}-{DateTimeOffset.Now:yyyyMMddHHmmssfff}");
    }
}
