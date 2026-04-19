namespace ObsidianQuickNoteWidget.Core.Models;

/// <summary>
/// Immutable descriptor of a single action a future "Plugin Runner" widget
/// can invoke. <see cref="CommandId"/> is an opaque identifier (typically a
/// colon-separated id like <c>workspace:new-tab</c>) interpreted downstream
/// by the runner host. <see cref="Icon"/> is an optional glyph / asset key.
/// </summary>
public sealed record RunnerAction(Guid Id, string Label, string CommandId, string? Icon = null);
