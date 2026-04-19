namespace ObsidianQuickNoteWidget.Core.State;

/// <summary>Per-widget persisted state. Serialized as JSON.</summary>
public sealed class WidgetState
{
    public string WidgetId { get; set; } = string.Empty;
    public string Size { get; set; } = "medium";
    public string LastFolder { get; set; } = string.Empty;
    public string LastFolderNew { get; set; } = string.Empty;
    public bool OpenAfterCreate { get; set; }
    public bool AutoDatePrefix { get; set; }
    public bool AppendToDaily { get; set; }
    public string TagsCsv { get; set; } = string.Empty;
    public string Template { get; set; } = "Blank";
    public List<string> CachedFolders { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();
    public List<string> PinnedFolders { get; set; } = new();
    public List<string> RecentNotes { get; set; } = new();
    public DateTimeOffset? CachedFoldersAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
    public string? LastCreatedPath { get; set; }

    /// <summary>
    /// Ordered list of <see cref="Models.RunnerAction"/> ids that the user has
    /// pinned to the Plugin Runner widget surface. Empty => show all catalog
    /// actions (up to the size's slot cap). When populated, the runner card
    /// filters to and preserves this order.
    /// </summary>
    public List<Guid> PinnedActionIds { get; set; } = new();

    /// <summary>
    /// True when the Plugin Runner widget is currently showing its
    /// customization card (add/remove/pin surface). Mutated by the
    /// <c>openCustomize</c>/<c>cancelCustomize</c>/<c>addAction</c> verbs.
    /// </summary>
    public bool IsCustomizing { get; set; }

    /// <summary>
    /// When non-null, the Plugin Runner is showing a "Remove &lt;label&gt;?"
    /// confirmation card for this action id. Cleared by
    /// <c>removeAction</c> (on confirm) or <c>cancelRemove</c>.
    /// </summary>
    public Guid? PendingRemoveId { get; set; }

    /// <summary>
    /// Snapshot of the outcome of the most recent Plugin Runner
    /// <c>runAction</c> invocation for this widget. Null until the user has
    /// run at least one action.
    /// </summary>
    public RunnerActionResult? LastRunResult { get; set; }
}

/// <summary>
/// Outcome snapshot of a single <c>runAction</c> invocation. Persisted on the
/// widget so the next render can surface a ✓/! badge or error message.
/// </summary>
public sealed class RunnerActionResult
{
    public Guid ActionId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset At { get; set; }
}
