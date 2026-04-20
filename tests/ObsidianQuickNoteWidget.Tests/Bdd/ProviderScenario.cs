using ObsidianQuickNoteWidget.Providers;

namespace ObsidianQuickNoteWidget.Tests.Bdd;

/// <summary>
/// BDD "Given / When / Then" scenario builder for
/// <see cref="ObsidianWidgetProvider"/> behavior tests.
///
/// Purpose: make it trivial to write tests that describe a user-visible
/// behavior in terms of the inputs and observable outputs of the widget
/// provider — in particular how many times (and under what circumstances)
/// the provider pushes a card update to the Widget Host. The typed-text-wipe
/// regression (fixed in 1.0.0.9) slipped through because the provider's
/// static dependency on Microsoft.Windows.Widgets.Providers.WidgetManager
/// could not be observed from tests. That dependency is now routed through
/// <see cref="IWidgetUpdateSink"/>; the fake used here
/// (<see cref="RecordingUpdateSink"/>) counts every push attempt so scenarios
/// can make BDD-style assertions such as
/// <c>Then.PushUpdateCount_Is(0)</c>.
///
/// Usage pattern:
/// <code>
///   var assertions = await new ProviderScenario()
///       .WithCliAvailable()
///       .WidgetIsActive("w1", WidgetIdentifiers.QuickNoteWidgetId)
///       .When(p => p.RefreshFolderCacheAsync("w1", pushOnCompletion: false));
///   assertions
///       .PushUpdateCount_Is(0)
///       .CliFolderListCallsIs(1);
/// </code>
/// </summary>
internal sealed class ProviderScenario
{
    private readonly InMemoryStateStore _store = new();
    private readonly RecordingCli _cli = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly RecordingUpdateSink _sink = new();
    private readonly CapturingLog _log = new();
    private ObsidianWidgetProvider? _provider;

    public RecordingUpdateSink Sink => _sink;
    public RecordingCli Cli => _cli;
    public InMemoryStateStore Store => _store;
    public CapturingLog Log => _log;

    public ObsidianWidgetProvider Provider
        => _provider ??= new ObsidianWidgetProvider(_log, _store, _cli, null, _launcher, _sink);

    // ------ Given ------

    public ProviderScenario WithCliAvailable(bool available = true)
    {
        _cli.Available = available;
        return this;
    }

    public ProviderScenario WithFolders(params string[] folders)
    {
        _cli.FoldersReply = folders;
        return this;
    }

    /// <summary>
    /// Register <paramref name="widgetId"/> as an active widget of the given
    /// definition and seed size (default "small"). Mirrors what the Widget
    /// Host would do via CreateWidget/Activate, without needing to construct
    /// WinRT <c>WidgetContext</c> args.
    /// </summary>
    public ProviderScenario WidgetIsActive(
        string widgetId,
        string definitionId,
        string size = "small")
    {
        var state = _store.Get(widgetId);
        state.WidgetId = widgetId;
        state.Size = size;
        _store.Save(state);
        Provider.RegisterActiveForTest(widgetId, definitionId);
        return this;
    }

    /// <summary>
    /// Seed the widget state with arbitrary values (e.g. in-flight user
    /// input) so assertions can verify it was or was not disturbed.
    /// </summary>
    public ProviderScenario WithState(string widgetId, Action<Core.State.WidgetState> mutate)
    {
        var state = _store.Get(widgetId);
        mutate(state);
        _store.Save(state);
        return this;
    }

    // ------ When ------

    /// <summary>
    /// Executes the asynchronous action under test. Catches exceptions so
    /// the scenario can still make Then-assertions against the sink / state
    /// (mirrors how FireAndLog swallows task faults in production).
    /// </summary>
    public async Task<ProviderScenarioAssertions> When(Func<ObsidianWidgetProvider, Task> act)
    {
        try { await act(Provider).ConfigureAwait(false); }
        catch (Exception ex) { _log.Errors.Add(ex); }
        return new ProviderScenarioAssertions(this);
    }

    public ProviderScenarioAssertions Then => new(this);
}

internal sealed class ProviderScenarioAssertions
{
    private readonly ProviderScenario _s;
    public ProviderScenarioAssertions(ProviderScenario s) { _s = s; }

    public ProviderScenarioAssertions PushUpdateCount_Is(int expected)
    {
        Assert.Equal(expected, _s.Sink.Count);
        return this;
    }

    public ProviderScenarioAssertions PushUpdateCountFor_Is(string widgetId, int expected)
    {
        Assert.Equal(expected, _s.Sink.CountFor(widgetId));
        return this;
    }

    public ProviderScenarioAssertions NoPushUpdateFor(string widgetId)
        => PushUpdateCountFor_Is(widgetId, 0);

    public ProviderScenarioAssertions StateSizeIs(string widgetId, string expectedSize)
    {
        Assert.Equal(expectedSize, _s.Store.Get(widgetId).Size);
        return this;
    }

    public ProviderScenarioAssertions CliFolderListCallsIs(int expected)
    {
        Assert.Equal(expected, _s.Cli.ListFoldersCalls);
        return this;
    }

    public ProviderScenarioAssertions StateField<T>(string widgetId, Func<Core.State.WidgetState, T> extract, T expected)
    {
        Assert.Equal(expected, extract(_s.Store.Get(widgetId)));
        return this;
    }

    public ProviderScenarioAssertions CachedFoldersContains(string widgetId, string folder)
    {
        var folders = _s.Store.Get(widgetId).CachedFolders ?? new List<string>();
        Assert.Contains(folder, folders);
        return this;
    }
}
