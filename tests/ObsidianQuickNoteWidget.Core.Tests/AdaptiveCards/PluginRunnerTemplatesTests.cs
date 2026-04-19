using System.Text.Json;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.AdaptiveCards;

/// <summary>
/// Structural tests for the Plugin Runner adaptive-card templates. These
/// guard the v1-host contract: every <c>Action.Execute</c> must carry
/// <c>data.widgetId</c> and a <c>verb</c> string field, and the schema
/// version must be exactly <c>"1.5"</c> (Widget Host silently drops 1.6).
/// </summary>
public class PluginRunnerTemplatesTests
{
    private static readonly string[] AllNames =
    {
        CardTemplates.PluginRunnerSmallTemplate,
        CardTemplates.PluginRunnerMediumTemplate,
        CardTemplates.PluginRunnerLargeTemplate,
    };

    [Theory]
    [InlineData(WidgetSize.Small)]
    [InlineData(WidgetSize.Medium)]
    [InlineData(WidgetSize.Large)]
    public void LoadPluginRunner_ReturnsValidJson_WithSchema15(WidgetSize size)
    {
        var json = CardTemplates.LoadPluginRunner(size);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("AdaptiveCard", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("1.5", doc.RootElement.GetProperty("version").GetString());
        Assert.True(doc.RootElement.TryGetProperty("body", out var body));
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public void AllThreeTemplates_LoadAsEmbeddedResources()
    {
        foreach (var name in AllNames)
        {
            var json = CardTemplates.Load(name);
            Assert.False(string.IsNullOrWhiteSpace(json), $"{name} returned empty content");
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("AdaptiveCard", doc.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void Small_HasExactlyOneGridColumnSet()
    {
        var doc = JsonDocument.Parse(CardTemplates.LoadPluginRunner(WidgetSize.Small));
        var rowCount = CountGridRows(doc.RootElement);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public void Medium_HasTwoGridColumnSets()
    {
        var doc = JsonDocument.Parse(CardTemplates.LoadPluginRunner(WidgetSize.Medium));
        var rowCount = CountGridRows(doc.RootElement);
        Assert.Equal(2, rowCount);
    }

    [Fact]
    public void Large_HasTwoRowsOfThreeCols_AndPlusMoreIndicator()
    {
        var json = CardTemplates.LoadPluginRunner(WidgetSize.Large);
        var doc = JsonDocument.Parse(json);

        var rowCount = CountGridRows(doc.RootElement);
        Assert.Equal(2, rowCount);

        // "+N more" indicator: a TextBlock bound to `${more}` with a `hasMore` gate.
        Assert.Contains("${more}", json);
        Assert.Contains("hasMore", json);
    }

    [Fact]
    public void EveryActionExecute_HasWidgetIdAndVerbInData()
    {
        foreach (var name in AllNames)
        {
            var json = CardTemplates.Load(name);
            using var doc = JsonDocument.Parse(json);
            var offenders = new List<string>();
            WalkActionExecutes(doc.RootElement, offenders);
            Assert.True(offenders.Count == 0,
                $"Template '{name}' has Action.Execute without data.widgetId+verb: {string.Join("; ", offenders)}");
        }
    }

    [Fact]
    public void EveryActionExecute_HasRunActionOrOpenCustomizeVerb()
    {
        // Runner contract: only two verbs fire from this card — `runAction`
        // (tile select) and `openCustomize` (gear + empty-state CTA).
        var allowed = new HashSet<string> { "runAction", "openCustomize" };
        foreach (var name in AllNames)
        {
            var json = CardTemplates.Load(name);
            using var doc = JsonDocument.Parse(json);
            var verbs = new List<string>();
            CollectVerbs(doc.RootElement, verbs);
            Assert.NotEmpty(verbs);
            foreach (var v in verbs)
                Assert.Contains(v, allowed);
        }
    }

    private static int CountGridRows(JsonElement el)
    {
        // A "grid row" is a ColumnSet whose columns bind `$data` to `${$root.rowN}`.
        var count = 0;
        Walk(el, node =>
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (!node.TryGetProperty("type", out var t) || t.GetString() != "ColumnSet") return;
            if (!node.TryGetProperty("columns", out var cols) || cols.ValueKind != JsonValueKind.Array) return;
            foreach (var c in cols.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.Object
                    && c.TryGetProperty("$data", out var d)
                    && d.ValueKind == JsonValueKind.String
                    && d.GetString() is string s
                    && s.Contains("$root.row", StringComparison.Ordinal))
                {
                    count++;
                    return;
                }
            }
        });
        return count;
    }

    private static void WalkActionExecutes(JsonElement el, List<string> offenders)
    {
        Walk(el, node =>
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
        });
    }

    private static void CollectVerbs(JsonElement el, List<string> verbs)
    {
        Walk(el, node =>
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (!node.TryGetProperty("type", out var t) || t.GetString() != "Action.Execute") return;
            if (node.TryGetProperty("verb", out var v) && v.ValueKind == JsonValueKind.String)
                verbs.Add(v.GetString()!);
        });
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
