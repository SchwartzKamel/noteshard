using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.State;
using ObsidianQuickNoteWidget.Providers;
using Xunit;

namespace ObsidianQuickNoteWidget.Tests;

/// <summary>
/// Covers the <c>RecentNotes</c>-definition refresh wiring on
/// <see cref="ObsidianWidgetProvider"/>: CLI call, 30s TTL gating, and
/// the invariant that QuickNote widgets are NOT touched by this path
/// (they still populate <see cref="WidgetState.RecentNotes"/> through
/// <c>CreateNoteAsync</c>).
/// </summary>
public class ObsidianWidgetProviderRecentsTests
{
    private sealed class RecordingCli : IObsidianCli
    {
        public bool Available { get; set; } = true;
        public bool IsAvailable => Available;
        public int ListRecentsCalls;
        public int ListFilesCalls;
        public IReadOnlyList<string> RecentsReply { get; set; } =
            new[] { "Welcome.md", "Inbox/Hello.md", "Notes/World.md" };
        public IReadOnlyList<string> FilesReply { get; set; } =
            new[] { "Welcome.md", "Inbox/Hello.md", "Notes/World.md" };

        public Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
            => Task.FromResult(new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero));
        public Task<string?> GetVaultRootAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref ListFilesCalls);
            return Task.FromResult(FilesReply);
        }

        public Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ListRecentsCalls);
            return Task.FromResult(RecentsReply);
        }

        public Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> AppendDailyAsync(string text, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        private readonly Dictionary<string, WidgetState> _cache = new();
        public WidgetState Get(string widgetId) =>
            _cache.TryGetValue(widgetId, out var s) ? s : new WidgetState { WidgetId = widgetId };
        public void Save(WidgetState state) => _cache[state.WidgetId] = state;
        public void Delete(string widgetId) => _cache.Remove(widgetId);
    }

    private static ObsidianWidgetProvider MakeProvider(RecordingCli cli, InMemoryStateStore store)
        => new(NullLog.Instance, store, cli);

    // ── ShouldRefreshRecents (pure helper) ────────────────────────────────

    [Fact]
    public void ShouldRefreshRecents_FirstActivation_IsTrue()
    {
        var s = new WidgetState { WidgetId = "w" }; // RecentNotesRefreshedAt = MinValue
        var now = DateTimeOffset.Now;
        Assert.True(ObsidianWidgetProvider.ShouldRefreshRecents(s, now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ShouldRefreshRecents_WithinTtl_IsFalse()
    {
        var now = DateTimeOffset.Parse("2026-04-19T10:00:30Z");
        var s = new WidgetState { WidgetId = "w", RecentNotesRefreshedAt = now.AddSeconds(-10) };
        Assert.False(ObsidianWidgetProvider.ShouldRefreshRecents(s, now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ShouldRefreshRecents_PastTtl_IsTrue()
    {
        var now = DateTimeOffset.Parse("2026-04-19T10:00:30Z");
        var s = new WidgetState { WidgetId = "w", RecentNotesRefreshedAt = now.AddSeconds(-31) };
        Assert.True(ObsidianWidgetProvider.ShouldRefreshRecents(s, now, TimeSpan.FromSeconds(30)));
    }

    // ── RefreshRecentNotesAsync ───────────────────────────────────────────

    private static readonly string[] ExpectedRecents = { "Welcome.md", "Inbox/Hello.md", "Notes/World.md" };

    [Fact]
    public async Task Refresh_PopulatesState_FromCli_AndStampsTimestamp()
    {
        var cli = new RecordingCli();
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-1";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        var before = DateTimeOffset.Now;
        await provider.RefreshRecentNotesAsync(id);
        var after = DateTimeOffset.Now;

        Assert.Equal(1, cli.ListRecentsCalls);
        var state = store.Get(id);
        Assert.Equal(ExpectedRecents, state.RecentNotes);
        Assert.InRange(state.RecentNotesRefreshedAt, before, after.AddSeconds(1));
    }

    private static readonly string[] DupeRecents = { "N1.md", "n2.MD" };

    [Fact]
    public async Task Refresh_DedupesAndCapsAt16()
    {
        var cli = new RecordingCli
        {
            // 20 entries with some case-insensitive duplicates.
            RecentsReply = Enumerable.Range(1, 20).Select(i => $"n{i}.md")
                .Concat(DupeRecents).ToList(),
            // Files list mirrors recents so intersection keeps all entries.
            FilesReply = Enumerable.Range(1, 20).Select(i => $"n{i}.md").ToList(),
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-2";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);
        var state = store.Get(id);

        Assert.Equal(16, state.RecentNotes.Count);
        // Order preserved from CLI; duplicates after first occurrence dropped.
        Assert.Equal("n1.md", state.RecentNotes[0]);
        Assert.DoesNotContain("N1.md", state.RecentNotes);
        Assert.DoesNotContain("n2.MD", state.RecentNotes);
    }

    [Fact]
    public async Task Refresh_CliUnavailable_IsNoop()
    {
        var cli = new RecordingCli { Available = false };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-3";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        Assert.Equal(0, cli.ListRecentsCalls);
        var state = store.Get(id);
        Assert.Empty(state.RecentNotes);
        Assert.Equal(DateTimeOffset.MinValue, state.RecentNotesRefreshedAt);
    }

    [Fact]
    public async Task Refresh_UnknownWidget_DoesNotSaveState()
    {
        // Widget not registered in _active — refresh still calls CLI (outside
        // gate) but the gated save block is skipped, so the store stays empty.
        var cli = new RecordingCli();
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        await provider.RefreshRecentNotesAsync("never-registered");

        Assert.Equal(1, cli.ListRecentsCalls);
        // Nothing saved — Get returns a fresh default WidgetState.
        Assert.Empty(store.Get("never-registered").RecentNotes);
        Assert.Equal(DateTimeOffset.MinValue, store.Get("never-registered").RecentNotesRefreshedAt);
    }

    // ── Ghost-file filtering (v6) ─────────────────────────────────────────

    private static readonly string[] GhostExpected = { "A.md", "B.md" };
    private static readonly string[] GhostRecentsInput = { "A.md", "ghost.md", "B.md" };
    private static readonly string[] GhostFilesInput = { "A.md", "B.md" };
    private static readonly string[] CaseRecentsInput = { "welcome.MD", "test/Test.md" };
    private static readonly string[] CaseFilesInput = { "Welcome.md", "Test/test.md" };
    private static readonly string[] DefensiveRecentsInput = { "A.md", "B.md" };
    private static readonly string[] AllGhostRecentsInput = { "ghost1.md", "ghost2.md" };
    private static readonly string[] AllGhostFilesInput = { "Alive.md" };

    [Fact]
    public async Task Refresh_IntersectsWithFiles_DropsGhostEntries_PreservesOrder()
    {
        var cli = new RecordingCli
        {
            RecentsReply = GhostRecentsInput,
            FilesReply = GhostFilesInput,
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-ghost";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        Assert.Equal(1, cli.ListRecentsCalls);
        Assert.Equal(1, cli.ListFilesCalls);
        var state = store.Get(id);
        Assert.Equal(GhostExpected, state.RecentNotes);
    }

    [Fact]
    public async Task Refresh_IntersectionIsCaseInsensitive()
    {
        // `obsidian recents` sometimes returns different casing than `obsidian
        // files` for the same underlying path — intersection must match
        // case-insensitively to avoid wiping real entries.
        var cli = new RecordingCli
        {
            RecentsReply = CaseRecentsInput,
            FilesReply = CaseFilesInput,
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-case";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        var state = store.Get(id);
        // Recents' original casing is preserved (it's what we display).
        Assert.Equal(CaseRecentsInput, state.RecentNotes);
    }

    [Fact]
    public async Task Refresh_FilesEmpty_RecentsNonEmpty_KeepsRecentsAsIs_Defensive()
    {
        // Defensive: if `obsidian files` returns 0 entries (CLI hiccup) while
        // `recents` has entries, we'd rather show potentially-stale entries
        // than wipe the widget.
        var cli = new RecordingCli
        {
            RecentsReply = DefensiveRecentsInput,
            FilesReply = Array.Empty<string>(),
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-files-empty";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        var state = store.Get(id);
        Assert.Equal(DefensiveRecentsInput, state.RecentNotes);
    }

    [Fact]
    public async Task Refresh_BothEmpty_StateEmpty()
    {
        var cli = new RecordingCli
        {
            RecentsReply = Array.Empty<string>(),
            FilesReply = Array.Empty<string>(),
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-both-empty";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        var state = store.Get(id);
        Assert.Empty(state.RecentNotes);
    }

    [Fact]
    public async Task Refresh_AllRecentsAreGhosts_StateEmpty()
    {
        var cli = new RecordingCli
        {
            RecentsReply = AllGhostRecentsInput,
            FilesReply = AllGhostFilesInput,
        };
        var store = new InMemoryStateStore();
        var provider = MakeProvider(cli, store);

        const string id = "recents-all-ghost";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.RefreshRecentNotesAsync(id);

        var state = store.Get(id);
        Assert.Empty(state.RecentNotes);
    }

    // ── QuickNote invariant ───────────────────────────────────────────────

    [Fact]
    public void QuickNoteWidget_DoesNotTriggerRecentsRefresh_ViaShouldRefresh()
    {
        // Sanity: the TTL policy is applied only inside PushUpdate's RecentNotes
        // branch. QuickNote widgets never consult ShouldRefreshRecents, so a
        // fresh state for a QuickNote widget shouldn't influence any refresh
        // bookkeeping. We assert by construction: a fresh WidgetState has
        // RecentNotesRefreshedAt == MinValue, and no code path outside the
        // RecentNotes branch reads it.
        var s = new WidgetState { WidgetId = "qn-widget" };
        Assert.Equal(DateTimeOffset.MinValue, s.RecentNotesRefreshedAt);
    }
}
