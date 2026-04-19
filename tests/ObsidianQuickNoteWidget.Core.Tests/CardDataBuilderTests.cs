using System.Text.Json;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using ObsidianQuickNoteWidget.Core.Models;
using ObsidianQuickNoteWidget.Core.State;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class CardDataBuilderTests
{
    [Fact]
    public void QuickNoteData_BindsStateDefaults()
    {
        var s = new WidgetState
        {
            WidgetId = "w",
            LastFolder = "Notes",
            OpenAfterCreate = true,
            CachedFolders = new List<string> { "Notes", "Archive" },
            RecentFolders = new List<string> { "Notes" },
            PinnedFolders = new List<string> { "Inbox" },
        };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.Equal("Notes", inputs.GetProperty("folder").GetString());
        Assert.Equal("true", inputs.GetProperty("openAfterCreate").GetString());

        var choices = doc.RootElement.GetProperty("folderChoices").EnumerateArray().ToList();
        Assert.Equal("(vault root)", choices[0].GetProperty("title").GetString());
        Assert.Contains(choices, c => c.GetProperty("title").GetString() == "📌 Inbox");
        Assert.Contains(choices, c => c.GetProperty("title").GetString() == "🕑 Notes");
        Assert.Contains(choices, c => c.GetProperty("title").GetString() == "Archive");
    }

    [Fact]
    public void QuickNoteData_FolderChoices_OrderedPinnedThenRecentThenCached_NoDuplicates()
    {
        // Overlap between the three buckets: "B" appears in pinned+recent,
        // "C" appears in recent+cached. The promotion rules (pinned wins over
        // recent, recent wins over cached) must produce a single entry per
        // folder in the order [pinned…, recent-only…, cached-only…].
        var s = new WidgetState
        {
            WidgetId = "w",
            PinnedFolders = new List<string> { "A", "B" },
            RecentFolders = new List<string> { "B", "C" },
            CachedFolders = new List<string> { "C", "D", "E" },
        };

        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        var choices = doc.RootElement.GetProperty("folderChoices").EnumerateArray().ToList();

        // index 0 is always "(vault root)" — skip it for the ordering check.
        var values = choices.Skip(1).Select(c => c.GetProperty("value").GetString()).ToArray();
        string[] expected = ["A", "B", "C", "D", "E"];
        Assert.Equal(expected, values);

        // Verify bucket assignment via the title prefix (pin/clock/none).
        var titles = choices.Skip(1).Select(c => c.GetProperty("title").GetString()).ToArray();
        Assert.Equal("📌 A", titles[0]);
        Assert.Equal("📌 B", titles[1]);
        Assert.Equal("🕑 C", titles[2]);
        Assert.Equal("D", titles[3]);
        Assert.Equal("E", titles[4]);
    }

    [Fact]
    public void QuickNoteData_FolderChoices_DeduplicatesCaseInsensitive()
    {
        var s = new WidgetState
        {
            WidgetId = "w",
            PinnedFolders = new List<string> { "Notes", "notes", "NOTES" },
        };

        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        var choices = doc.RootElement.GetProperty("folderChoices").EnumerateArray().ToList();
        // (vault root) + exactly one "Notes" variant
        Assert.Equal(2, choices.Count);
    }

    [Fact]
    public void QuickNoteData_HasRecents_FalseWhenEmpty()
    {
        var s = new WidgetState { WidgetId = "w" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("hasRecents").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("hasNoRecents").GetBoolean());
        Assert.Empty(doc.RootElement.GetProperty("recentNotes").EnumerateArray());
    }

    [Fact]
    public void QuickNoteData_HasRecents_TrueWhenPopulated_StripsExtension()
    {
        var s = new WidgetState
        {
            WidgetId = "w",
            RecentNotes = new List<string> { "Inbox/Hello.md", "Notes/World.md" },
        };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("hasRecents").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("hasNoRecents").GetBoolean());

        var recents = doc.RootElement.GetProperty("recentNotes").EnumerateArray().ToList();
        Assert.Equal(2, recents.Count);
        Assert.Equal("Hello", recents[0].GetProperty("title").GetString());
        Assert.Equal("Inbox/Hello.md", recents[0].GetProperty("path").GetString());
        Assert.Equal("World", recents[1].GetProperty("title").GetString());
    }

    [Fact]
    public void QuickNoteData_RecentNotes_CappedAtEight()
    {
        var s = new WidgetState
        {
            WidgetId = "w",
            RecentNotes = Enumerable.Range(1, 20).Select(i => $"n{i}.md").ToList(),
        };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);

        var recents = doc.RootElement.GetProperty("recentNotes").EnumerateArray().ToList();
        Assert.Equal(8, recents.Count);
        // Order preserved — first in state is first out.
        Assert.Equal("n1", recents[0].GetProperty("title").GetString());
    }

    [Fact]
    public void QuickNoteData_ShowAdvanced_FlipsLabel()
    {
        var s = new WidgetState { WidgetId = "w" };

        var collapsed = JsonDocument.Parse(CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false));
        var expanded = JsonDocument.Parse(CardDataBuilder.BuildQuickNoteData(s, showAdvanced: true));

        Assert.False(collapsed.RootElement.GetProperty("showAdvanced").GetBoolean());
        Assert.Equal("Show advanced", collapsed.RootElement.GetProperty("advancedLabel").GetString());

        Assert.True(expanded.RootElement.GetProperty("showAdvanced").GetBoolean());
        Assert.Equal("Hide advanced", expanded.RootElement.GetProperty("advancedLabel").GetString());
    }

    [Fact]
    public void QuickNoteData_RendersError()
    {
        var s = new WidgetState { WidgetId = "w", LastError = "boom" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("boom", doc.RootElement.GetProperty("statusMessage").GetString());
        Assert.Equal("Attention", doc.RootElement.GetProperty("statusColor").GetString());
        Assert.True(doc.RootElement.GetProperty("hasStatus").GetBoolean());
    }

    [Fact]
    public void QuickNoteData_ExplicitStatus_OverridesStateError()
    {
        var s = new WidgetState { WidgetId = "w", LastError = "stale" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false,
            status: new CardStatus("fresh", "Good"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("fresh", doc.RootElement.GetProperty("statusMessage").GetString());
        Assert.Equal("Good", doc.RootElement.GetProperty("statusColor").GetString());
    }

    [Fact]
    public void QuickNoteData_NoStatus_HasStatusIsFalse()
    {
        var s = new WidgetState { WidgetId = "w" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("hasStatus").GetBoolean());
        Assert.Equal("Default", doc.RootElement.GetProperty("statusColor").GetString());
    }

    [Fact]
    public void QuickNoteData_EmitsWidgetId_FromState()
    {
        var s = new WidgetState { WidgetId = "widget-abc-123" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("widget-abc-123", doc.RootElement.GetProperty("widgetId").GetString());
    }

    [Fact]
    public void QuickNoteData_EchoesFolderNew_FromState()
    {
        var s = new WidgetState { WidgetId = "w", LastFolderNew = "Projects/NewIdea" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: true);
        using var doc = JsonDocument.Parse(json);
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.Equal("Projects/NewIdea", inputs.GetProperty("folderNew").GetString());
    }

    [Fact]
    public void QuickNoteData_FolderNew_DefaultsToEmpty()
    {
        var s = new WidgetState { WidgetId = "w" };
        var json = CardDataBuilder.BuildQuickNoteData(s, showAdvanced: false);
        using var doc = JsonDocument.Parse(json);
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.True(inputs.TryGetProperty("folderNew", out var fn));
        Assert.Equal(string.Empty, fn.GetString());
    }

    [Fact]
    public void CliMissingData_IncludesDetail()
    {
        var json = CardDataBuilder.BuildCliMissingData("not on PATH");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("not on PATH", doc.RootElement.GetProperty("detail").GetString());
        Assert.True(doc.RootElement.GetProperty("hasDetail").GetBoolean());
    }

    [Fact]
    public void CliMissingData_Null_MarksHasDetailFalse()
    {
        var json = CardDataBuilder.BuildCliMissingData(null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.GetProperty("hasDetail").GetBoolean());
    }

    // ── BuildPluginRunnerData ────────────────────────────────────────────

    private static RunnerAction A(string label, string cmd = "cmd") =>
        new(Guid.NewGuid(), label, cmd);

    [Theory]
    [InlineData(WidgetSize.Small, 2, 2, 0)]
    [InlineData(WidgetSize.Medium, 4, 2, 2)]
    [InlineData(WidgetSize.Large, 6, 3, 3)]
    public void PluginRunnerData_SlotCaps_RespectedPerDensity(
        WidgetSize size, int expectedCap, int expectedRow0, int expectedRow1)
    {
        // Build 10 catalog entries — far more than any cap.
        var catalog = Enumerable.Range(0, 10).Select(i => A($"a{i}")).ToList();
        var s = new WidgetState { WidgetId = "w" };

        var json = CardDataBuilder.BuildPluginRunnerData(s, catalog, size);
        using var doc = JsonDocument.Parse(json);

        var actions = doc.RootElement.GetProperty("actions").EnumerateArray().ToList();
        Assert.Equal(expectedCap, actions.Count);

        var row0 = doc.RootElement.GetProperty("row0").EnumerateArray().ToList();
        var row1 = doc.RootElement.GetProperty("row1").EnumerateArray().ToList();
        Assert.Equal(expectedRow0, row0.Count);
        Assert.Equal(expectedRow1, row1.Count);

        Assert.Equal(10 - expectedCap, doc.RootElement.GetProperty("more").GetInt32());
        Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("hasActions").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("isEmpty").GetBoolean());
    }

    [Fact]
    public void PluginRunnerData_EmptyCatalog_EmitsIsEmptyAndZeroArrays()
    {
        var s = new WidgetState { WidgetId = "w" };
        var json = CardDataBuilder.BuildPluginRunnerData(s, Array.Empty<RunnerAction>(), WidgetSize.Medium);
        using var doc = JsonDocument.Parse(json);

        Assert.Empty(doc.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("row0").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("row1").EnumerateArray());
        Assert.Equal(0, doc.RootElement.GetProperty("more").GetInt32());
        Assert.False(doc.RootElement.GetProperty("hasActions").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("hasRow1").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("inputs").ValueKind);
    }

    [Fact]
    public void PluginRunnerData_MoreCount_AccurateWhenCatalogUnderCap()
    {
        var catalog = new[] { A("one") };
        var s = new WidgetState { WidgetId = "w" };

        var json = CardDataBuilder.BuildPluginRunnerData(s, catalog, WidgetSize.Large);
        using var doc = JsonDocument.Parse(json);

        Assert.Single(doc.RootElement.GetProperty("actions").EnumerateArray());
        Assert.Equal(0, doc.RootElement.GetProperty("more").GetInt32());
        Assert.False(doc.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void PluginRunnerData_PinnedActionIds_FiltersAndPreservesPinOrder()
    {
        var a1 = A("alpha");
        var a2 = A("beta");
        var a3 = A("gamma");
        var a4 = A("delta");
        var catalog = new[] { a1, a2, a3, a4 };

        // Deliberately reverse order + skip one (a2).
        var s = new WidgetState
        {
            WidgetId = "w",
            PinnedActionIds = new List<Guid> { a4.Id, a1.Id, a3.Id },
        };

        var json = CardDataBuilder.BuildPluginRunnerData(s, catalog, WidgetSize.Large);
        using var doc = JsonDocument.Parse(json);

        var labels = doc.RootElement.GetProperty("actions").EnumerateArray()
            .Select(e => e.GetProperty("label").GetString())
            .ToArray();
        string[] expected = ["delta", "alpha", "gamma"];
        Assert.Equal(expected, labels);
    }

    [Fact]
    public void PluginRunnerData_PinnedIncludesUnknownId_SkipsUnknownSilently()
    {
        var a1 = A("one");
        var s = new WidgetState
        {
            WidgetId = "w",
            PinnedActionIds = new List<Guid> { Guid.NewGuid(), a1.Id },
        };

        var json = CardDataBuilder.BuildPluginRunnerData(s, new[] { a1 }, WidgetSize.Small);
        using var doc = JsonDocument.Parse(json);

        var labels = doc.RootElement.GetProperty("actions").EnumerateArray()
            .Select(e => e.GetProperty("label").GetString())
            .ToArray();
        string[] expected = ["one"];
        Assert.Equal(expected, labels);
    }

    [Fact]
    public void PluginRunnerData_UnpinnedState_ShowsAllCatalogUpToCap()
    {
        var catalog = Enumerable.Range(0, 5).Select(i => A($"x{i}")).ToList();
        var s = new WidgetState { WidgetId = "w" }; // no pins

        var json = CardDataBuilder.BuildPluginRunnerData(s, catalog, WidgetSize.Medium);
        using var doc = JsonDocument.Parse(json);

        // cap=4 from medium → all 4 visible, 1 extra reported.
        Assert.Equal(4, doc.RootElement.GetProperty("actions").EnumerateArray().Count());
        Assert.Equal(1, doc.RootElement.GetProperty("more").GetInt32());
    }

    [Fact]
    public void PluginRunnerData_ActionJson_Shape()
    {
        var a = new RunnerAction(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Label", "workspace:new-tab");
        var s = new WidgetState { WidgetId = "wid-1" };

        var json = CardDataBuilder.BuildPluginRunnerData(s, new[] { a }, WidgetSize.Small);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("wid-1", doc.RootElement.GetProperty("widgetId").GetString());
        var item = doc.RootElement.GetProperty("actions")[0];
        Assert.Equal(a.Id.ToString(), item.GetProperty("id").GetString());
        Assert.Equal("Label", item.GetProperty("label").GetString());
        Assert.Equal("workspace:new-tab", item.GetProperty("commandId").GetString());
        Assert.Equal("none", item.GetProperty("lastResult").GetString());
    }

    [Fact]
    public void PluginRunnerData_Small_NeverPopulatesRow1()
    {
        var catalog = Enumerable.Range(0, 4).Select(i => A($"x{i}")).ToList();
        var s = new WidgetState { WidgetId = "w" };

        var json = CardDataBuilder.BuildPluginRunnerData(s, catalog, WidgetSize.Small);
        using var doc = JsonDocument.Parse(json);

        // cap=2, cols=2 → both go to row0, row1 empty.
        Assert.Equal(2, doc.RootElement.GetProperty("row0").EnumerateArray().Count());
        Assert.Empty(doc.RootElement.GetProperty("row1").EnumerateArray());
        Assert.False(doc.RootElement.GetProperty("hasRow1").GetBoolean());
    }
}

