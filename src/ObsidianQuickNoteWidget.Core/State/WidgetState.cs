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
}
