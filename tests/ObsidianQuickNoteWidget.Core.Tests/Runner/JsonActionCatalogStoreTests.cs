using System.Text.Json;
using ObsidianQuickNoteWidget.Core.Models;
using ObsidianQuickNoteWidget.Core.Runner;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.Runner;

public class JsonActionCatalogStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _tmp;

    public JsonActionCatalogStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "oqnw-runner-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _tmp = Path.Combine(_tmpDir, "action-catalog.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task List_EmptyCatalog_ReturnsEmpty()
    {
        var store = new JsonActionCatalogStore(_tmp);
        var items = await store.ListAsync();
        Assert.Empty(items);
    }

    [Fact]
    public async Task AddListGetRemove_RoundTrip()
    {
        var store = new JsonActionCatalogStore(_tmp);

        var added = await store.AddAsync("Open Tab", "workspace:new-tab", icon: "tab.png");
        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal("Open Tab", added.Label);
        Assert.Equal("workspace:new-tab", added.CommandId);
        Assert.Equal("tab.png", added.Icon);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(added, list[0]);

        var got = await store.GetAsync(added.Id);
        Assert.Equal(added, got);

        var missing = await store.GetAsync(Guid.NewGuid());
        Assert.Null(missing);

        var reopened = new JsonActionCatalogStore(_tmp);
        var after = await reopened.ListAsync();
        Assert.Single(after);
        Assert.Equal(added, after[0]);

        Assert.True(await reopened.RemoveAsync(added.Id));
        Assert.False(await reopened.RemoveAsync(added.Id));
        Assert.Empty(await reopened.ListAsync());

        var reopened2 = new JsonActionCatalogStore(_tmp);
        Assert.Empty(await reopened2.ListAsync());
    }

    [Fact]
    public async Task Add_TrimsIconAndNormalizesEmptyIconToNull()
    {
        var store = new JsonActionCatalogStore(_tmp);
        var a = await store.AddAsync("Label", "cmd:id", icon: "   ");
        Assert.Null(a.Icon);

        var b = await store.AddAsync("Label2", "cmd:id2", icon: "  glyph  ");
        Assert.Equal("glyph", b.Icon);
    }

    [Fact]
    public async Task Add_RejectsInvalidInput()
    {
        var store = new JsonActionCatalogStore(_tmp);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("", "cmd:id"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("Label", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("Label", "has space"));
    }

    [Fact]
    public async Task ConcurrentAdd_AllEntriesPersisted()
    {
        var store = new JsonActionCatalogStore(_tmp);

        const int total = 50;
        var tasks = new List<Task<RunnerAction>>(total);
        for (var i = 0; i < total; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() => store.AddAsync($"Label {idx}", $"cmd:action-{idx}")));
        }

        var results = await Task.WhenAll(tasks);
        Assert.Equal(total, results.Length);
        Assert.Equal(total, results.Select(r => r.Id).Distinct().Count());

        var list = await store.ListAsync();
        Assert.Equal(total, list.Count);

        var reopened = new JsonActionCatalogStore(_tmp);
        var persisted = await reopened.ListAsync();
        Assert.Equal(total, persisted.Count);
        foreach (var expected in results)
        {
            Assert.Contains(persisted, p => p.Id == expected.Id);
        }
    }

    [Fact]
    public async Task List_ReturnsSnapshot_MutationsDoNotLeak()
    {
        var store = new JsonActionCatalogStore(_tmp);
        await store.AddAsync("A", "cmd:a");

        var first = await store.ListAsync();
        Assert.Single(first);

        if (first is List<RunnerAction> mutable)
        {
            mutable.Clear();
        }

        var second = await store.ListAsync();
        Assert.Single(second);
    }

    [Fact]
    public async Task Load_CorruptFile_DegradesToEmptyCatalog()
    {
        await File.WriteAllTextAsync(_tmp, "{ not json");
        var store = new JsonActionCatalogStore(_tmp);
        Assert.Empty(await store.ListAsync());

        var added = await store.AddAsync("Recovered", "cmd:recover");
        var reopened = new JsonActionCatalogStore(_tmp);
        var list = await reopened.ListAsync();
        Assert.Single(list);
        Assert.Equal(added.Id, list[0].Id);
    }

    [Fact]
    public async Task AtomicWrite_StrayTempFileIsIgnored_MainFileAuthoritative()
    {
        // Simulate a prior crash that left a `.tmp` file behind. The store must
        // load the authoritative `.json` file and must not surface data from the
        // leftover `.tmp`.
        var store = new JsonActionCatalogStore(_tmp);
        var keep = await store.AddAsync("Keep", "cmd:keep");

        var strayTmp = _tmp + ".tmp";
        await File.WriteAllTextAsync(
            strayTmp,
            JsonSerializer.Serialize(new[]
            {
                new RunnerAction(Guid.NewGuid(), "ShouldNotAppear", "cmd:stray"),
            }));

        var reopened = new JsonActionCatalogStore(_tmp);
        var list = await reopened.ListAsync();
        Assert.Single(list);
        Assert.Equal(keep.Id, list[0].Id);
        Assert.DoesNotContain(list, a => a.Label == "ShouldNotAppear");

        await reopened.AddAsync("Also", "cmd:also");
        var reopened2 = new JsonActionCatalogStore(_tmp);
        var list2 = await reopened2.ListAsync();
        Assert.Equal(2, list2.Count);
        Assert.DoesNotContain(list2, a => a.Label == "ShouldNotAppear");
    }

    [Fact]
    public async Task AtomicWrite_OnlyTempFileOnDisk_LoadsEmpty()
    {
        // If a crash happened before the temp → main rename, only the tmp
        // exists. The store must treat the catalog as empty (no main file),
        // never picking up a half-written tmp.
        var strayTmp = _tmp + ".tmp";
        await File.WriteAllTextAsync(strayTmp, "[ half-written");

        var store = new JsonActionCatalogStore(_tmp);
        var list = await store.ListAsync();
        Assert.Empty(list);
        Assert.False(File.Exists(_tmp));
    }

    [Fact]
    public async Task SerializationShape_IsStable()
    {
        var store = new JsonActionCatalogStore(_tmp);
        var a = await store.AddAsync("Open Tab", "workspace:new-tab", icon: "tab.png");

        var raw = await File.ReadAllTextAsync(_tmp);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());

        var el = doc.RootElement[0];
        Assert.Equal(a.Id, el.GetProperty("Id").GetGuid());
        Assert.Equal("Open Tab", el.GetProperty("Label").GetString());
        Assert.Equal("workspace:new-tab", el.GetProperty("CommandId").GetString());
        Assert.Equal("tab.png", el.GetProperty("Icon").GetString());
    }

    [Fact]
    public async Task SerializationShape_NullIconRoundTrips()
    {
        var store = new JsonActionCatalogStore(_tmp);
        var a = await store.AddAsync("L", "cmd:x");
        Assert.Null(a.Icon);

        var reopened = new JsonActionCatalogStore(_tmp);
        var got = await reopened.GetAsync(a.Id);
        Assert.NotNull(got);
        Assert.Null(got!.Icon);
    }
}
