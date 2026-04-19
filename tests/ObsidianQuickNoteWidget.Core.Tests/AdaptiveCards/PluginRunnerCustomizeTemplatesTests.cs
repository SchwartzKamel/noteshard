using System.Text.Json;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using ObsidianQuickNoteWidget.Core.Models;
using ObsidianQuickNoteWidget.Core.State;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.AdaptiveCards;

/// <summary>
/// Structural tests for the Plugin Runner customize + confirm-remove cards.
/// Guards the same v1-host contract as the grid templates: schema 1.5, every
/// Action.Execute carries data.widgetId + verb.
/// </summary>
public class PluginRunnerCustomizeTemplatesTests
{
    private static readonly HashSet<string> AllowedVerbs = new()
    {
        "cancelCustomize",
        "addAction",
        "pinAction",
        "unpinAction",
        "removeActionConfirm",
        "removeAction",
        "cancelRemove",
    };

    [Fact]
    public void LoadPluginRunnerCustomize_ReturnsValidJson_WithSchema15()
    {
        var json = CardTemplates.LoadPluginRunnerCustomize();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("AdaptiveCard", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("1.5", doc.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void LoadPluginRunnerConfirmRemove_ReturnsValidJson_WithSchema15()
    {
        var json = CardTemplates.LoadPluginRunnerConfirmRemove();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("AdaptiveCard", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("1.5", doc.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void CustomizeTemplate_EveryActionExecute_HasWidgetIdAndVerb()
    {
        var json = CardTemplates.LoadPluginRunnerCustomize();
        AssertActionExecuteContract(json, AllowedVerbs);
    }

    [Fact]
    public void ConfirmRemoveTemplate_EveryActionExecute_HasWidgetIdAndVerb()
    {
        var json = CardTemplates.LoadPluginRunnerConfirmRemove();
        AssertActionExecuteContract(json, AllowedVerbs);
    }

    [Fact]
    public void BuildPluginRunnerCustomizeData_ShapesItemsWithPinnedFlag()
    {
        var a1 = new RunnerAction(Guid.NewGuid(), "Open Tab", "workspace:new-tab");
        var a2 = new RunnerAction(Guid.NewGuid(), "Close Tab", "workspace:close");
        var state = new WidgetState { WidgetId = "w1", PinnedActionIds = { a1.Id } };

        var json = CardDataBuilder.BuildPluginRunnerCustomizeData(state, new[] { a1, a2 });
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("w1", doc.RootElement.GetProperty("widgetId").GetString());
        Assert.True(doc.RootElement.GetProperty("hasItems").GetBoolean());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.True(items[0].GetProperty("isPinned").GetBoolean());
        Assert.False(items[1].GetProperty("isPinned").GetBoolean());
        Assert.Equal(a1.Id.ToString(), items[0].GetProperty("id").GetString());
    }

    [Fact]
    public void BuildPluginRunnerConfirmData_ResolvesTargetLabel()
    {
        var a = new RunnerAction(Guid.NewGuid(), "Open Tab", "workspace:new-tab");
        var state = new WidgetState { WidgetId = "w1", PendingRemoveId = a.Id };

        var json = CardDataBuilder.BuildPluginRunnerConfirmData(state, new[] { a });
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("w1", doc.RootElement.GetProperty("widgetId").GetString());
        Assert.True(doc.RootElement.GetProperty("hasTarget").GetBoolean());
        Assert.Equal(a.Id.ToString(), doc.RootElement.GetProperty("actionId").GetString());
        Assert.Equal("Open Tab", doc.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public void BuildPluginRunnerConfirmData_MissingTarget_SetsHasTargetFalse()
    {
        var state = new WidgetState { WidgetId = "w1", PendingRemoveId = Guid.NewGuid() };
        var json = CardDataBuilder.BuildPluginRunnerConfirmData(state, Array.Empty<RunnerAction>());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("hasTarget").GetBoolean());
    }

    [Fact]
    public void BuildPluginRunnerData_HighlightsLastRunResult()
    {
        var a = new RunnerAction(Guid.NewGuid(), "Open Tab", "workspace:new-tab");
        var state = new WidgetState
        {
            WidgetId = "w1",
            LastRunResult = new RunnerActionResult { ActionId = a.Id, Success = true, At = DateTimeOffset.UtcNow },
        };

        var json = CardDataBuilder.BuildPluginRunnerData(state, new[] { a }, WidgetSize.Medium);
        using var doc = JsonDocument.Parse(json);
        var actions = doc.RootElement.GetProperty("actions");
        Assert.Equal("ok", actions[0].GetProperty("lastResult").GetString());
    }

    private static void AssertActionExecuteContract(string json, IReadOnlySet<string> allowedVerbs)
    {
        using var doc = JsonDocument.Parse(json);
        var offenders = new List<string>();
        var verbs = new List<string>();
        Walk(doc.RootElement, node =>
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (!node.TryGetProperty("type", out var t) || t.GetString() != "Action.Execute") return;

            var verbField = node.TryGetProperty("verb", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
            var hasData = node.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
            var hasWidgetId = hasData
                && data.TryGetProperty("widgetId", out var wid)
                && wid.ValueKind == JsonValueKind.String
                && (wid.GetString() == "${widgetId}" || wid.GetString() == "${$root.widgetId}");
            var hasDataVerb = hasData
                && data.TryGetProperty("verb", out var dv)
                && dv.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(dv.GetString());

            if (!hasWidgetId || !hasDataVerb || string.IsNullOrWhiteSpace(verbField))
                offenders.Add($"verb={verbField ?? "(none)"}, widgetId={hasWidgetId}, data.verb={hasDataVerb}");
            else
                verbs.Add(verbField!);
        });

        Assert.True(offenders.Count == 0,
            "Action.Execute contract violations: " + string.Join("; ", offenders));
        Assert.NotEmpty(verbs);
        foreach (var v in verbs)
            Assert.Contains(v, allowedVerbs);
    }

    private static void Walk(JsonElement el, Action<JsonElement> visit)
    {
        visit(el);
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Walk(p.Value, visit);
                break;
            case JsonValueKind.Array:
                foreach (var i in el.EnumerateArray()) Walk(i, visit);
                break;
        }
    }
}
