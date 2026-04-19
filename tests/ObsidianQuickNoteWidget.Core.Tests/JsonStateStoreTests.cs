using ObsidianQuickNoteWidget.Core.State;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class JsonStateStoreTests : IDisposable
{
    private readonly string _tmp;

    public JsonStateStoreTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "oqnw-tests-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tmp)) File.Delete(_tmp); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Get_UnknownWidget_ReturnsDefault()
    {
        var s = new JsonStateStore(_tmp);
        var state = s.Get("w1");
        Assert.Equal("w1", state.WidgetId);
        Assert.Equal(string.Empty, state.LastFolder);
    }

    [Fact]
    public void SaveAndGet_RoundTrips()
    {
        var s = new JsonStateStore(_tmp);
        var w = new WidgetState
        {
            WidgetId = "w1",
            LastFolder = "Notes/Daily",
            OpenAfterCreate = true,
            AutoDatePrefix = true,
            TagsCsv = "idea",
            Template = "Meeting",
            RecentFolders = new List<string> { "Notes/Daily", "Inbox" },
            CachedFolders = new List<string> { "Notes/Daily", "Inbox", "Archive" },
        };
        s.Save(w);

        var s2 = new JsonStateStore(_tmp);
        var got = s2.Get("w1");
        Assert.Equal("Notes/Daily", got.LastFolder);
        Assert.True(got.OpenAfterCreate);
        Assert.True(got.AutoDatePrefix);
        Assert.Equal("Meeting", got.Template);
        Assert.Contains("Inbox", got.CachedFolders);
    }

    [Fact]
    public void Delete_RemovesWidget()
    {
        var s = new JsonStateStore(_tmp);
        s.Save(new WidgetState { WidgetId = "w1", LastFolder = "x" });
        s.Delete("w1");
        var g = s.Get("w1");
        Assert.Equal(string.Empty, g.LastFolder);
    }

    [Fact]
    public void Get_MissingFile_ReturnsDefaultState()
    {
        Assert.False(File.Exists(_tmp));
        var s = new JsonStateStore(_tmp);
        var state = s.Get("w1");
        Assert.Equal("w1", state.WidgetId);
        Assert.Equal(string.Empty, state.LastFolder);
        Assert.Empty(state.PinnedFolders);
        Assert.Empty(state.RecentFolders);
        Assert.False(state.OpenAfterCreate);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void Load_CorruptJson_ReturnsDefaultState(string bogus)
    {
        // Contract: the widget must never crash over a corrupt state file.
        // JsonStateStore.Load swallows the exception and returns an empty
        // dictionary, so a subsequent Get returns default state.
        File.WriteAllText(_tmp, bogus);
        var s = new JsonStateStore(_tmp);
        var state = s.Get("w1");
        Assert.Equal("w1", state.WidgetId);
        Assert.Equal(string.Empty, state.LastFolder);
    }

    [Fact]
    public void Constructor_MissingDirectory_AutoCreated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oqnw-tests-dir-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(dir, "sub", "state.json");
        Assert.False(Directory.Exists(dir));

        try
        {
            var s = new JsonStateStore(nested);
            s.Save(new WidgetState { WidgetId = "w1", LastFolder = "x" });
            Assert.True(File.Exists(nested));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SaveAndGet_PropertyByPropertyRoundTrip()
    {
        var s = new JsonStateStore(_tmp);
        var now = new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);
        var w = new WidgetState
        {
            WidgetId = "w1",
            Size = "large",
            LastFolder = "Notes/Daily",
            OpenAfterCreate = true,
            AutoDatePrefix = true,
            AppendToDaily = true,
            TagsCsv = "idea,research",
            Template = "Meeting",
            CachedFolders = new List<string> { "A", "B", "C" },
            RecentFolders = new List<string> { "A" },
            PinnedFolders = new List<string> { "B" },
            RecentNotes = new List<string> { "A/x.md", "B/y.md" },
            CachedFoldersAt = now,
            LastStatus = "ok",
            LastError = null,
            LastCreatedPath = "A/x.md",
        };
        s.Save(w);

        var fresh = new JsonStateStore(_tmp).Get("w1");

        Assert.Equal(w.WidgetId, fresh.WidgetId);
        Assert.Equal(w.Size, fresh.Size);
        Assert.Equal(w.LastFolder, fresh.LastFolder);
        Assert.Equal(w.OpenAfterCreate, fresh.OpenAfterCreate);
        Assert.Equal(w.AutoDatePrefix, fresh.AutoDatePrefix);
        Assert.Equal(w.AppendToDaily, fresh.AppendToDaily);
        Assert.Equal(w.TagsCsv, fresh.TagsCsv);
        Assert.Equal(w.Template, fresh.Template);
        Assert.Equal(w.CachedFolders, fresh.CachedFolders);
        Assert.Equal(w.RecentFolders, fresh.RecentFolders);
        Assert.Equal(w.PinnedFolders, fresh.PinnedFolders);
        Assert.Equal(w.RecentNotes, fresh.RecentNotes);
        Assert.Equal(w.CachedFoldersAt, fresh.CachedFoldersAt);
        Assert.Equal(w.LastStatus, fresh.LastStatus);
        Assert.Equal(w.LastError, fresh.LastError);
        Assert.Equal(w.LastCreatedPath, fresh.LastCreatedPath);
    }

    [Fact]
    public void Get_ReturnsClone_MutationsDoNotLeakIntoStore()
    {
        var s = new JsonStateStore(_tmp);
        s.Save(new WidgetState
        {
            WidgetId = "w1",
            CachedFolders = new List<string> { "A", "B" },
        });

        var first = s.Get("w1");
        first.CachedFolders.Add("ShouldNotLeak");
        first.LastFolder = "mutated";

        var second = s.Get("w1");
        Assert.DoesNotContain("ShouldNotLeak", second.CachedFolders);
        Assert.NotEqual("mutated", second.LastFolder);
    }
}
