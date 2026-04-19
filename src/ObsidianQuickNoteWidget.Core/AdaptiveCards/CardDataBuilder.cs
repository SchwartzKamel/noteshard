using System.Text.Json;
using System.Text.Json.Nodes;
using ObsidianQuickNoteWidget.Core.Models;
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

    /// <summary>
    /// Builds the data payload for the Plugin Runner card. Shape contract:
    /// <code>
    /// {
    ///   "widgetId": "&lt;guid&gt;",
    ///   "actions":  [ { id, label, commandId, lastResult } , ... ],  // capped to size slots
    ///   "row0":     [ ... ],   // first grid row
    ///   "row1":     [ ... ],   // second grid row (medium/large only — [] for small)
    ///   "more":     0,         // catalog entries beyond the cap
    ///   "hasMore":  false,
    ///   "hasActions": true,
    ///   "hasRow1":  false,
    ///   "isEmpty":  false,
    ///   "inputs":   {}
    /// }
    /// </code>
    /// Filtering rules:
    /// <list type="bullet">
    ///   <item>If <see cref="WidgetState.PinnedActionIds"/> is empty, all catalog
    ///         entries are shown (up to the density's slot cap).</item>
    ///   <item>If populated, only pinned ids are shown, in pin order. Missing
    ///         catalog entries are skipped silently.</item>
    /// </list>
    /// </summary>
    public static string BuildPluginRunnerData(
        WidgetState state,
        IReadOnlyList<RunnerAction> catalog,
        WidgetSize size)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);

        var (cap, cols) = size switch
        {
            WidgetSize.Small => (2, 2),
            WidgetSize.Large => (6, 3),
            _ => (4, 2),
        };

        var filtered = FilterAndOrderActions(catalog, state.PinnedActionIds);
        var visible = filtered.Take(cap).ToList();
        var more = Math.Max(0, filtered.Count - cap);

        var actions = new JsonArray();
        foreach (var a in visible) actions.Add(ActionToJson(a, state.LastRunResult));

        var row0 = new JsonArray();
        var row1 = new JsonArray();
        for (var i = 0; i < visible.Count; i++)
        {
            var node = ActionToJson(visible[i], state.LastRunResult);
            if (i < cols) row0.Add(node);
            else row1.Add(node);
        }

        var root = new JsonObject
        {
            ["widgetId"] = state.WidgetId ?? string.Empty,
            ["actions"] = actions,
            ["row0"] = row0,
            ["row1"] = row1,
            ["more"] = more,
            ["hasMore"] = more > 0,
            ["hasActions"] = visible.Count > 0,
            ["hasRow1"] = row1.Count > 0,
            ["isEmpty"] = visible.Count == 0,
            ["inputs"] = new JsonObject(),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Builds the data payload for the Plugin Runner customization card. Lists
    /// every catalog entry with its pinned-state and provides echo slots for
    /// the add-form inputs.
    /// </summary>
    public static string BuildPluginRunnerCustomizeData(
        WidgetState state,
        IReadOnlyList<RunnerAction> catalog,
        string newLabelEcho = "",
        string newCommandIdEcho = "")
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);

        var pinned = new HashSet<Guid>(state.PinnedActionIds);
        var items = new JsonArray();
        foreach (var a in catalog)
        {
            var isPinned = pinned.Contains(a.Id);
            items.Add(new JsonObject
            {
                ["id"] = a.Id.ToString(),
                ["label"] = a.Label ?? string.Empty,
                ["commandId"] = a.CommandId ?? string.Empty,
                ["isPinned"] = isPinned,
                ["isUnpinned"] = !isPinned,
            });
        }

        var (msg, color, hasStatus) = RenderStatus(state, status: null);

        var root = new JsonObject
        {
            ["widgetId"] = state.WidgetId ?? string.Empty,
            ["items"] = items,
            ["hasItems"] = catalog.Count > 0,
            ["isEmpty"] = catalog.Count == 0,
            ["inputs"] = new JsonObject
            {
                ["newLabel"] = newLabelEcho ?? string.Empty,
                ["newCommandId"] = newCommandIdEcho ?? string.Empty,
            },
            ["statusMessage"] = msg ?? string.Empty,
            ["statusColor"] = color,
            ["hasStatus"] = hasStatus,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Builds the data payload for the Plugin Runner "remove action?"
    /// confirmation card. When <see cref="WidgetState.PendingRemoveId"/>
    /// does not match any catalog entry, <c>hasTarget=false</c> so the
    /// template can render a graceful fallback.
    /// </summary>
    public static string BuildPluginRunnerConfirmData(
        WidgetState state,
        IReadOnlyList<RunnerAction> catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);

        var id = state.PendingRemoveId;
        RunnerAction? target = null;
        if (id is not null)
        {
            foreach (var a in catalog)
            {
                if (a.Id == id.Value) { target = a; break; }
            }
        }

        var root = new JsonObject
        {
            ["widgetId"] = state.WidgetId ?? string.Empty,
            ["actionId"] = target?.Id.ToString() ?? string.Empty,
            ["label"] = target?.Label ?? string.Empty,
            ["commandId"] = target?.CommandId ?? string.Empty,
            ["hasTarget"] = target is not null,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static List<RunnerAction> FilterAndOrderActions(
        IReadOnlyList<RunnerAction> catalog, List<Guid> pinnedIds)
    {
        if (pinnedIds is null || pinnedIds.Count == 0)
            return catalog.ToList();

        var byId = catalog.ToDictionary(a => a.Id);
        var result = new List<RunnerAction>(pinnedIds.Count);
        foreach (var id in pinnedIds)
            if (byId.TryGetValue(id, out var a))
                result.Add(a);
        return result;
    }

    private static JsonObject ActionToJson(RunnerAction a, RunnerActionResult? last = null)
    {
        var result = "none";
        if (last is not null && last.ActionId == a.Id)
            result = last.Success ? "ok" : "error";

        return new JsonObject
        {
            ["id"] = a.Id.ToString(),
            ["label"] = a.Label ?? string.Empty,
            ["commandId"] = a.CommandId ?? string.Empty,
            ["lastResult"] = result,
        };
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
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            // Normalize to forward-slashes so the card subtitle matches the
            // vault-relative convention that the Obsidian CLI emits.
            var folder = dir.Replace('\\', '/');
            arr.Add(new JsonObject
            {
                ["title"] = title,
                ["path"] = path,
                ["folder"] = folder,
                ["hasFolder"] = !string.IsNullOrEmpty(folder),
            });
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
