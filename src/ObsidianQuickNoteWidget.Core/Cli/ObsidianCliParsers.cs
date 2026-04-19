namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Pure string-transform helpers for parsing <c>obsidian</c> CLI stdout and
/// escaping <c>content=</c> argument values. Kept free of I/O so they can be
/// unit tested deterministically.
/// </summary>
internal static class ObsidianCliParsers
{
    /// <summary>
    /// Extracts the first non-empty, trimmed line from <paramref name="stdout"/>.
    /// Used for <c>obsidian vault info=path</c>, which prints a single line.
    /// Returns <c>null</c> if no non-empty line exists.
    /// </summary>
    public static string? ParseVaultPath(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;

        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length > 0) return line;
        }
        return null;
    }

    /// <summary>
    /// Parses newline-delimited folder output from <c>obsidian folders</c>:
    /// trims each line, strips a leading <c>/</c>, filters out empty lines,
    /// dotfiles (<c>.obsidian</c>, <c>.trash</c>, …), and the vault root line
    /// (which collapses to empty after <c>TrimStart('/')</c>). Deduplicates
    /// case-insensitively and sorts ordinal-case-insensitive.
    /// </summary>
    public static IReadOnlyList<string> ParseFolders(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return Array.Empty<string>();

        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('/'))
            .Where(s => s.Length > 0 && !s.StartsWith('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Parses newline-delimited output from <c>obsidian recents</c>: trims each
    /// line, drops empty lines, keeps only entries ending in <c>.md</c>
    /// (case-insensitive — the verb mixes files and folders), deduplicates
    /// case-insensitively while preserving order (newest-first), and caps to
    /// <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<string> ParseRecents(string? stdout, int max)
    {
        if (string.IsNullOrEmpty(stdout) || max <= 0) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!line.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(line)) continue;
            result.Add(line);
            if (result.Count >= max) break;
        }
        return result;
    }

    /// <summary>
    /// Parses newline-delimited output from <c>obsidian files</c>: trims each
    /// line, drops empty lines, and keeps only entries ending in <c>.md</c>
    /// (case-insensitive). Deduplicates case-insensitively while preserving
    /// order. Used to build the live-files set that <c>recents</c> is
    /// intersected against so deleted-file ghost entries don't render.
    /// </summary>
    public static IReadOnlyList<string> ParseFiles(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!line.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(line)) continue;
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Obsidian CLI <c>content=</c> values use literal <c>\n</c>/<c>\t</c> as
    /// newline/tab escapes. We must escape backslashes FIRST — otherwise the
    /// backslashes introduced by the newline/tab replacements would themselves
    /// be doubled on a second pass, producing <c>\\n</c> where the user meant
    /// a real newline.
    /// </summary>
    /// <summary>
    /// Parses a successful <c>create</c> stdout line. The Obsidian CLI prints
    /// either <c>Created: &lt;vault-relative-path&gt;</c> (new file) or
    /// <c>Overwrote: &lt;vault-relative-path&gt;</c> (existing file with
    /// <c>overwrite</c> flag). The path segment is authoritative — on
    /// collisions without <c>overwrite</c> the CLI silently renames
    /// (<c>p1.md</c> → <c>p1 1.md</c>) and reports the real name here.
    /// Returns <c>true</c> and the trimmed path on match.
    /// </summary>
    public static bool TryParseCreated(string? stdout, out string createdPath)
    {
        createdPath = string.Empty;
        if (string.IsNullOrEmpty(stdout)) return false;

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            string? payload = null;
            if (line.StartsWith("Created:", StringComparison.Ordinal)) payload = line.Substring("Created:".Length);
            else if (line.StartsWith("Overwrote:", StringComparison.Ordinal)) payload = line.Substring("Overwrote:".Length);

            if (payload is null) continue;

            var trimmed = payload.Trim();
            if (trimmed.Length == 0) continue;

            createdPath = trimmed;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses a successful <c>daily:append</c> stdout line of the form
    /// <c>Added to: &lt;vault-relative-path&gt;</c>. Returns <c>true</c> and
    /// the trimmed path on match.
    /// </summary>
    public static bool TryParseAppendedDaily(string? stdout, out string dailyPath)
    {
        dailyPath = string.Empty;
        if (string.IsNullOrEmpty(stdout)) return false;

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!line.StartsWith("Added to:", StringComparison.Ordinal)) continue;

            var trimmed = line.Substring("Added to:".Length).Trim();
            if (trimmed.Length == 0) continue;

            dailyPath = trimmed;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if any non-empty line in <paramref name="stdout"/>
    /// begins with <c>Error:</c>. The Obsidian CLI reports all errors on
    /// stdout with exit=0, so this is the authoritative failure signal for
    /// mutating commands (<c>create</c>, <c>open</c>, <c>daily:append</c>).
    /// </summary>
    public static bool HasCliError(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return false;

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Error:", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static string EscapeContent(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        return s.Replace("\\", "\\\\")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\t", "\\t");
    }
}
