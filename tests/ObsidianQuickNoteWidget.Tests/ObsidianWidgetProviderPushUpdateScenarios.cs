using ObsidianQuickNoteWidget;
using ObsidianQuickNoteWidget.Providers;
using ObsidianQuickNoteWidget.Tests.Bdd;

namespace ObsidianQuickNoteWidget.Tests;

/// <summary>
/// BDD scenario tests covering the typed-text-wipe regression fixed in
/// 1.0.0.9 and the general PushUpdate orchestration contract of
/// <see cref="ObsidianWidgetProvider"/>.
///
/// Each test follows a strict Given / When / Then shape via
/// <see cref="ProviderScenario"/>. The invariant under test is:
/// <list type="bullet">
///   <item>Background paths (timer, silent refresh, focus/visibility
///         context change) must NOT push a card update, because Windows 11
///         Widget Host resets all <c>Input.*</c> values on every
///         <c>UpdateWidget</c> call — that would wipe text the user is
///         currently typing.</item>
///   <item>Foreground paths (size change, user action, post-create refresh,
///         first-pin) MUST push, because the card needs a re-render and
///         either inputs are already cleared, or a re-render is unavoidable
///         (size swap).</item>
/// </list>
/// </summary>
public class ObsidianWidgetProviderPushUpdateScenarios
{
    private const string Id = "widget-1";
    private const string QuickNote = WidgetIdentifiers.QuickNoteWidgetId;

    [Fact(DisplayName =
        "Given an active widget, " +
        "When a silent folder-cache refresh runs, " +
        "Then no card update is pushed but the cache is populated")]
    public async Task SilentFolderCacheRefresh_DoesNotPush()
    {
        var then = await new ProviderScenario()
            .WithCliAvailable()
            .WithFolders("Inbox", "Journal")
            .WidgetIsActive(Id, QuickNote)
            .When(p => p.RefreshFolderCacheAsync(Id, pushOnCompletion: false));

        then.PushUpdateCount_Is(0)
            .CliFolderListCallsIs(1)
            .CachedFoldersContains(Id, "Inbox");
    }

    [Fact(DisplayName =
        "Given an active widget, " +
        "When an explicit folder-cache refresh runs with pushOnCompletion, " +
        "Then exactly one card update is pushed")]
    public async Task ExplicitFolderCacheRefresh_Pushes()
    {
        var then = await new ProviderScenario()
            .WithCliAvailable()
            .WithFolders("Inbox")
            .WidgetIsActive(Id, QuickNote)
            .When(p => p.RefreshFolderCacheAsync(Id, pushOnCompletion: true));

        then.PushUpdateCountFor_Is(Id, 1);
    }

    [Fact(DisplayName =
        "Given a small widget already in state, " +
        "When the Widget Host fires context-changed with the same size " +
        "(a focus/visibility transition), " +
        "Then no card update is pushed (in-flight typed text is preserved)")]
    public async Task ContextChanged_SameSize_DoesNotPush()
    {
        var then = await new ProviderScenario()
            .WidgetIsActive(Id, QuickNote, size: "small")
            .When(p => p.HandleContextChangeCore(Id, "small"));

        then.NoPushUpdateFor(Id)
            .StateSizeIs(Id, "small");
    }

    [Fact(DisplayName =
        "Given a small widget, " +
        "When the user resizes it to large, " +
        "Then a card update is pushed and the new size is persisted")]
    public async Task ContextChanged_DifferentSize_Pushes()
    {
        var then = await new ProviderScenario()
            .WidgetIsActive(Id, QuickNote, size: "small")
            .When(p => p.HandleContextChangeCore(Id, "large"));

        then.PushUpdateCountFor_Is(Id, 1)
            .StateSizeIs(Id, "large");
    }

    [Fact(DisplayName =
        "Given two active widgets, " +
        "When the 2-minute folder-refresh timer fires (RefreshAllActiveAsync), " +
        "Then no card updates are pushed for any widget")]
    public async Task TimerRefresh_DoesNotPushAnyWidget()
    {
        var then = await new ProviderScenario()
            .WithCliAvailable()
            .WithFolders("Inbox", "Archive")
            .WidgetIsActive("w1", QuickNote)
            .WidgetIsActive("w2", QuickNote)
            .When(p => p.RefreshAllActiveAsync());

        then.PushUpdateCount_Is(0)
            .CachedFoldersContains("w1", "Archive")
            .CachedFoldersContains("w2", "Archive");
    }

    [Fact(DisplayName =
        "Given a widget is active and the user has typed a folder draft, " +
        "When a silent refresh runs, " +
        "Then no card update is pushed and the typed draft is preserved " +
        "(no background clobber of Input.* fields via PushUpdate)")]
    public async Task SilentRefresh_PreservesInFlightDraftInState()
    {
        var then = await new ProviderScenario()
            .WithCliAvailable()
            .WithFolders("Inbox")
            .WidgetIsActive(Id, QuickNote)
            .WithState(Id, s => s.LastFolderNew = "DraftFolder")
            .When(p => p.RefreshFolderCacheAsync(Id, pushOnCompletion: false));

        then.PushUpdateCount_Is(0)
            .StateField(Id, s => s.LastFolderNew, "DraftFolder");
    }
}
