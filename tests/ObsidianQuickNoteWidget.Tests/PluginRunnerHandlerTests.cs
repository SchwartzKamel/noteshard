using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.Models;
using ObsidianQuickNoteWidget.Core.Runner;
using ObsidianQuickNoteWidget.Core.State;
using ObsidianQuickNoteWidget.Providers;
using Xunit;

namespace ObsidianQuickNoteWidget.Tests;

/// <summary>
/// Verb-by-verb coverage for <see cref="PluginRunnerHandler"/>. Fakes stand in
/// for the catalog + command invoker so every assertion is about handler
/// logic: state mutation, dispatch branching, and error containment.
/// </summary>
public class PluginRunnerHandlerTests
{
    private static readonly string[] ExpectedNewTabInvocation = { "workspace:new-tab" };

    private static string Json(params (string key, string value)[] pairs)
    {
        var parts = pairs.Select(p =>
            $"\"{p.key}\":\"{p.value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    private static WidgetState NewState(string id = "w1") => new() { WidgetId = id };

    [Fact]
    public async Task RunAction_HappyPath_RecordsSuccess()
    {
        var catalog = new FakeCatalog();
        var action = await catalog.AddAsync("Open Tab", "workspace:new-tab");
        var invoker = new FakeInvoker { Reply = new CommandRunResult(true, null, "Executed: workspace:new-tab") };
        var h = new PluginRunnerHandler(catalog, invoker, NullLog.Instance);
        var state = NewState();

        await h.HandleVerbAsync("runAction", Json(("actionId", action.Id.ToString())), state);

        Assert.Equal(ExpectedNewTabInvocation, invoker.Invocations);
        Assert.NotNull(state.LastRunResult);
        Assert.Equal(action.Id, state.LastRunResult!.ActionId);
        Assert.True(state.LastRunResult.Success);
        Assert.Null(state.LastRunResult.Error);
    }

    [Fact]
    public async Task RunAction_UnknownActionId_DoesNotInvoke_RecordsError()
    {
        var catalog = new FakeCatalog();
        var invoker = new FakeInvoker();
        var h = new PluginRunnerHandler(catalog, invoker, NullLog.Instance);
        var state = NewState();

        var ghost = Guid.NewGuid();
        await h.HandleVerbAsync("runAction", Json(("actionId", ghost.ToString())), state);

        Assert.Empty(invoker.Invocations);
        Assert.NotNull(state.LastRunResult);
        Assert.Equal(ghost, state.LastRunResult!.ActionId);
        Assert.False(state.LastRunResult.Success);
        Assert.Equal("Action not found", state.LastRunResult.Error);
    }

    [Fact]
    public async Task RunAction_CliReportsError_RecordsFailureWithMessage()
    {
        var catalog = new FakeCatalog();
        var action = await catalog.AddAsync("Broken", "plugin:does-not-exist");
        var invoker = new FakeInvoker { Reply = new CommandRunResult(false, "Command \"plugin:does-not-exist\" not found", "") };
        var h = new PluginRunnerHandler(catalog, invoker, NullLog.Instance);
        var state = NewState();

        await h.HandleVerbAsync("runAction", Json(("actionId", action.Id.ToString())), state);

        Assert.NotNull(state.LastRunResult);
        Assert.False(state.LastRunResult!.Success);
        Assert.Contains("not found", state.LastRunResult.Error);
    }

    [Fact]
    public async Task AddAction_ValidInput_PersistsAutoPinsAndExitsCustomize()
    {
        var catalog = new FakeCatalog();
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.IsCustomizing = true;

        await h.HandleVerbAsync(
            "addAction",
            Json(("newLabel", "Daily Note"), ("newCommandId", "daily-notes:open-today")),
            state);

        var list = await catalog.ListAsync();
        Assert.Single(list);
        Assert.Equal("Daily Note", list[0].Label);
        Assert.Contains(list[0].Id, state.PinnedActionIds);
        Assert.False(state.IsCustomizing);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task AddAction_InvalidInput_DoesNotMutateCatalog_StaysInCustomize()
    {
        var catalog = new FakeCatalog();
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.IsCustomizing = true;

        await h.HandleVerbAsync(
            "addAction",
            Json(("newLabel", ""), ("newCommandId", "daily-notes:open-today")),
            state);

        Assert.Empty(await catalog.ListAsync());
        Assert.Empty(state.PinnedActionIds);
        Assert.True(state.IsCustomizing);
        Assert.False(string.IsNullOrWhiteSpace(state.LastError));
    }

    [Fact]
    public async Task RemoveActionConfirm_SetsPendingId_ThenRemoveAction_Removes()
    {
        var catalog = new FakeCatalog();
        var action = await catalog.AddAsync("Open", "workspace:new-tab");
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.PinnedActionIds.Add(action.Id);

        await h.HandleVerbAsync("removeActionConfirm", Json(("actionId", action.Id.ToString())), state);
        Assert.Equal(action.Id, state.PendingRemoveId);

        await h.HandleVerbAsync("removeAction", Json(("actionId", action.Id.ToString())), state);
        Assert.Null(state.PendingRemoveId);
        Assert.Empty(await catalog.ListAsync());
        Assert.DoesNotContain(action.Id, state.PinnedActionIds);
    }

    [Fact]
    public async Task CancelRemove_ClearsPendingRemoveId()
    {
        var catalog = new FakeCatalog();
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.PendingRemoveId = Guid.NewGuid();

        await h.HandleVerbAsync("cancelRemove", "{}", state);

        Assert.Null(state.PendingRemoveId);
    }

    [Fact]
    public async Task PinAction_AppendsOnce_UnpinAction_Removes()
    {
        var catalog = new FakeCatalog();
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        var id = Guid.NewGuid();

        await h.HandleVerbAsync("pinAction", Json(("actionId", id.ToString())), state);
        await h.HandleVerbAsync("pinAction", Json(("actionId", id.ToString())), state);
        Assert.Single(state.PinnedActionIds);
        Assert.Equal(id, state.PinnedActionIds[0]);

        await h.HandleVerbAsync("unpinAction", Json(("actionId", id.ToString())), state);
        Assert.Empty(state.PinnedActionIds);
    }

    [Fact]
    public async Task OpenCustomize_ThenCancelCustomize_TogglesFlag()
    {
        var h = new PluginRunnerHandler(new FakeCatalog(), new FakeInvoker(), NullLog.Instance);
        var state = NewState();

        await h.HandleVerbAsync("openCustomize", "{}", state);
        Assert.True(state.IsCustomizing);

        await h.HandleVerbAsync("cancelCustomize", "{}", state);
        Assert.False(state.IsCustomizing);
    }

    [Fact]
    public async Task BuildCardAsync_SelectsCustomizeTemplate_WhenIsCustomizing()
    {
        var h = new PluginRunnerHandler(new FakeCatalog(), new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.IsCustomizing = true;

        var (template, _) = await h.BuildCardAsync(state, Core.AdaptiveCards.WidgetSize.Medium);
        Assert.Contains("Customize actions", template);
    }

    [Fact]
    public async Task BuildCardAsync_SelectsConfirmTemplate_WhenPendingRemoveSet()
    {
        var catalog = new FakeCatalog();
        var a = await catalog.AddAsync("x", "y:z");
        var h = new PluginRunnerHandler(catalog, new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        state.PendingRemoveId = a.Id;

        var (template, data) = await h.BuildCardAsync(state, Core.AdaptiveCards.WidgetSize.Medium);
        Assert.Contains("Remove action?", template);
        Assert.Contains(a.Id.ToString(), data);
    }

    [Fact]
    public async Task UnknownVerb_DoesNotThrow()
    {
        var h = new PluginRunnerHandler(new FakeCatalog(), new FakeInvoker(), NullLog.Instance);
        var state = NewState();
        await h.HandleVerbAsync("nonsense", "{}", state);
        // No mutation, no throw.
        Assert.Null(state.LastError);
    }

    // ---- Fakes ----

    private sealed class FakeCatalog : IActionCatalogStore
    {
        private readonly List<RunnerAction> _items = new();

        public Task<IReadOnlyList<RunnerAction>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunnerAction>>(_items.ToArray());

        public Task<RunnerAction?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(a => a.Id == id));

        public Task<RunnerAction> AddAsync(string label, string commandId, string? icon = null, CancellationToken ct = default)
        {
            var (l, c) = RunnerActionValidator.Normalize(label, commandId);
            var action = new RunnerAction(Guid.NewGuid(), l, c, icon);
            _items.Add(action);
            return Task.FromResult(action);
        }

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
        {
            var removed = _items.RemoveAll(a => a.Id == id) > 0;
            return Task.FromResult(removed);
        }
    }

    private sealed class FakeInvoker : IObsidianCommandInvoker
    {
        public List<string> Invocations { get; } = new();
        public CommandRunResult Reply { get; set; } = new CommandRunResult(true, null, "ok");

        public Task<CommandRunResult> RunCommandAsync(string commandId, CancellationToken ct = default)
        {
            Invocations.Add(commandId);
            return Task.FromResult(Reply);
        }

        public Task<IReadOnlyList<string>> ListCommandsAsync(string? prefix = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
