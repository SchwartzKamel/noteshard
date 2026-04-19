namespace ObsidianQuickNoteWidget.Core.Notes;

/// <summary>Sanitizes a user-supplied note title into a safe file name stem.</summary>
public static class FilenameSanitizer
{
    private const int MaxLength = 120;
    private static readonly char[] Illegal =
    {
        '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\r', '\n', '\t', '\0',
    };

    /// <summary>Sanitizes. Returns null if nothing printable remains.</summary>
    public static string? Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var ch in title)
        {
            if (char.IsControl(ch)) continue;
            if (Array.IndexOf(Illegal, ch) >= 0) continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        cleaned = cleaned.TrimEnd('.', ' ');

        if (cleaned.Length == 0) return null;
        if (cleaned.Length > MaxLength) cleaned = cleaned[..MaxLength].TrimEnd('.', ' ');

        if (IsReservedWindowsName(cleaned)) cleaned = "_" + cleaned;
        return cleaned;
    }

    internal static bool IsReservedWindowsName(string name)
    {
        var upper = name.ToUpperInvariant();
        var bare = upper;
        var dot = bare.IndexOf('.');
        if (dot >= 0) bare = bare[..dot];
        return bare is "CON" or "PRN" or "AUX" or "NUL"
            || (bare.Length == 4 && (bare.StartsWith("COM") || bare.StartsWith("LPT")) && char.IsDigit(bare[3]) && bare[3] != '0');
    }
}
