using System.Text.Json;
using System.Text.Json.Nodes;
using ObsidianQuickNoteWidget.Core.State;

namespace ObsidianQuickNoteWidget.Core.AdaptiveCards;

/// <summary>
/// Builds the Adaptive Card *data* JSON payload that the widget host binds against
/// the template JSON. Separating data from template keeps the UI declarative.
/// </summary>
public static class CardDataBuilder
{
    public static string BuildQuickNoteData(WidgetState s, bool showAdvanced, CardStatus? status = null)
    {
        var (msg, color, hasStatus) = RenderStatus(s, status);

        var folderChoices = BuildFolderChoices(s);

        var root = new JsonObject
        {
            ["widgetId"] = s.WidgetId ?? string.Empty,
            ["inputs"] = new JsonObject
            {
                ["title"] = string.Empty,
                ["folder"] = s.LastFolder ?? string.Empty,
                ["folderNew"] = s.LastFolderNew ?? string.Empty,
                ["body"] = string.Empty,
                ["tagsCsv"] = s.TagsCsv ?? string.Empty,
                ["template"] = s.Template ?? "Blank",
                ["autoDatePrefix"] = s.AutoDatePrefix ? "true" : "false",
                ["openAfterCreate"] = s.OpenAfterCreate ? "true" : "false",
                ["appendToDaily"] = s.AppendToDaily ? "true" : "false",
            },
            ["folderChoices"] = folderChoices,
            ["statusMessage"] = msg ?? string.Empty,
            ["statusColor"] = color,
            ["hasStatus"] = hasStatus,
            ["showAdvanced"] = showAdvanced,
            ["advancedLabel"] = showAdvanced ? "Hide advanced" : "Show advanced",
            ["recentNotes"] = BuildRecentNotes(s),
            ["hasNoRecents"] = s.RecentNotes.Count == 0,
            ["hasRecents"] = s.RecentNotes.Count > 0,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public static string BuildCliMissingData(string? detail = null, string widgetId = "")
    {
        var root = new JsonObject
        {
            ["widgetId"] = widgetId ?? string.Empty,
            ["detail"] = detail ?? string.Empty,
            ["hasDetail"] = !string.IsNullOrWhiteSpace(detail),
        };
        return root.ToJsonString();
    }

    private static JsonArray BuildFolderChoices(WidgetState s)
    {
        var arr = new JsonArray
        {
            new JsonObject { ["title"] = "(vault root)", ["value"] = string.Empty },
        };

        void Add(string value, string? titlePrefix = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var title = titlePrefix is null ? value : titlePrefix + value;
            arr.Add(new JsonObject { ["title"] = title, ["value"] = value });
        }

        foreach (var p in s.PinnedFolders.Distinct(StringComparer.OrdinalIgnoreCase))
            Add(p, "📌 ");

        foreach (var r in s.RecentFolders.Distinct(StringComparer.OrdinalIgnoreCase))
            if (!s.PinnedFolders.Contains(r, StringComparer.OrdinalIgnoreCase))
                Add(r, "🕑 ");

        foreach (var f in s.CachedFolders)
            if (!s.PinnedFolders.Contains(f, StringComparer.OrdinalIgnoreCase) &&
                !s.RecentFolders.Contains(f, StringComparer.OrdinalIgnoreCase))
                Add(f);

        return arr;
    }

    private static JsonArray BuildRecentNotes(WidgetState s)
    {
        var arr = new JsonArray();
        foreach (var path in s.RecentNotes.Take(8))
        {
            var title = Path.GetFileNameWithoutExtension(path);
            arr.Add(new JsonObject { ["title"] = title, ["path"] = path });
        }
        return arr;
    }

    private static (string? message, string color, bool hasStatus) RenderStatus(WidgetState s, CardStatus? status)
    {
        if (status is not null) return (status.Message, status.Color, true);
        if (!string.IsNullOrWhiteSpace(s.LastError)) return (s.LastError, "Attention", true);
        if (!string.IsNullOrWhiteSpace(s.LastStatus)) return (s.LastStatus, "Good", true);
        return (null, "Default", false);
    }
}

public sealed record CardStatus(string Message, string Color = "Good");
