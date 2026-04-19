using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Notes;

public sealed record FolderValidationResult(bool IsValid, string? NormalizedPath, string? Error)
{
    public static FolderValidationResult Ok(string normalized) => new(true, normalized, null);
    public static FolderValidationResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Validates and normalizes a user-supplied vault-relative folder path.
/// Rejects `..`, absolute paths, rooted paths, and Windows reserved names.
/// Normalizes `\` to `/` and strips leading/trailing slashes.
/// An empty string (or "/") maps to vault-root (returned as "").
/// </summary>
public static class FolderPathValidator
{
    private static readonly char[] IllegalSegmentChars = { ':', '*', '?', '"', '<', '>', '|' };

    public static FolderValidationResult Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return FolderValidationResult.Ok(string.Empty);

        // Normalize separators but preserve whitespace so we can catch trailing spaces in segments.
        var normalized = input.Replace('\\', '/');

        // Reject Windows drive-letter absolute paths ("C:\..." / "C:/...").
        if (normalized.Length >= 2 && normalized[1] == ':')
            return FolderValidationResult.Fail("Absolute paths are not allowed");

        // Treat a single leading/trailing slash as vault-root-relative notation; strip.
        normalized = normalized.Trim('/');
        if (normalized.Length == 0) return FolderValidationResult.Ok(string.Empty);

        var segments = normalized.Split('/');
        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg))
                return FolderValidationResult.Fail("Empty folder segment");

            if (seg == "." || seg == "..")
                return FolderValidationResult.Fail("Relative segments (./..) are not allowed");

            // F-04: reject leading-dot segments (e.g. ".obsidian", ".git", ".trash")
            // to keep notes out of Obsidian's own config tree and other hidden dirs.
            // `.` and `..` are handled by the guard above so this only catches genuine
            // hidden directory names.
            if (seg.StartsWith('.'))
                return FolderValidationResult.Fail($"Hidden folder segment '{FileLog.SanitizeForLogLine(seg)}' is not allowed");

            if (seg.IndexOfAny(IllegalSegmentChars) >= 0)
                return FolderValidationResult.Fail($"Illegal character in folder segment '{FileLog.SanitizeForLogLine(seg)}'");

            foreach (var c in seg)
            {
                if (char.IsControl(c))
                    return FolderValidationResult.Fail($"Control character in folder segment '{FileLog.SanitizeForLogLine(seg)}'");
            }

            if (FilenameSanitizer.IsReservedWindowsName(seg))
                return FolderValidationResult.Fail($"Reserved Windows name '{FileLog.SanitizeForLogLine(seg)}' cannot be used as a folder");

            if (seg.EndsWith('.') || seg.EndsWith(' ') || seg.StartsWith(' '))
                return FolderValidationResult.Fail($"Folder segment '{FileLog.SanitizeForLogLine(seg)}' cannot start/end with space or end with '.'");
        }

        return FolderValidationResult.Ok(string.Join('/', segments));
    }
}
