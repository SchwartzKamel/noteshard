using System.Buffers;

namespace ObsidianQuickNoteWidget.Core.Notes;

/// <summary>
/// Builds a YAML frontmatter block, then appends the body.
/// Emits nothing (no frontmatter fence) if no fields have values.
/// </summary>
public static class FrontmatterBuilder
{
    private static readonly SearchValues<char> YamlQuoteTriggers = SearchValues.Create(
        ":#,[]{}&*!|>'\"%@`");

    public static string Build(IReadOnlyList<string>? tags, DateTimeOffset? createdAt, string body)
    {
        var hasTags = tags is { Count: > 0 };
        var hasDate = createdAt.HasValue;
        if (!hasTags && !hasDate) return body ?? string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("---\n");
        if (hasDate)
            sb.Append("created: ").Append(createdAt!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz")).Append('\n');
        if (hasTags)
        {
            sb.Append("tags: [");
            for (var i = 0; i < tags!.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(YamlQuote(tags[i]));
            }
            sb.Append("]\n");
        }
        sb.Append("---\n\n");
        sb.Append(body ?? string.Empty);
        return sb.ToString();
    }

    public static IReadOnlyList<string> ParseTagsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTag)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeTag(string raw)
    {
        var s = raw.TrimStart('#').Trim();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/') sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    private static string YamlQuote(string v)
    {
        var needsQuote = v.Length == 0 || v.AsSpan().IndexOfAny(YamlQuoteTriggers) >= 0;
        if (!needsQuote) return v;
        return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
