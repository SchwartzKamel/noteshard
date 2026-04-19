namespace ObsidianQuickNoteWidget;

/// <summary>Stable identifiers used for COM registration + widget definitions.</summary>
internal static class WidgetIdentifiers
{
    /// <summary>CLSID of the <c>ObsidianWidgetProvider</c> COM server.</summary>
    /// <remarks>Must match the CLSID in <c>Package.appxmanifest</c>.</remarks>
    public const string ProviderClsid = "B3E8F4D4-3E9B-4A5E-9F3A-1F2E7B6A2C91";

    /// <summary>Widget definition IDs — must match the <c>Definition</c> blocks in <c>Package.appxmanifest</c>.</summary>
    public const string QuickNoteWidgetId = "ObsidianQuickNote";
    public const string RecentNotesWidgetId = "ObsidianRecentNotes";
    public const string PluginRunnerDefinitionId = "PluginRunner";
}
