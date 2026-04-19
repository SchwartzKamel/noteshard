namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Result of an <c>obsidian command id=&lt;id&gt;</c> invocation. The Obsidian
/// CLI always exits 0, so <see cref="Success"/> is derived from stdout shape
/// rather than exit code (see <see cref="ObsidianCommandInvoker"/> for the
/// live-CLI contract).
/// </summary>
public sealed record CommandRunResult(bool Success, string? ErrorMessage, string StdoutTrimmed);

/// <summary>
/// Thin wrapper around the Obsidian CLI's <c>command</c> and <c>commands</c>
/// verbs (Obsidian 1.12+). Execution is side-effectful but idempotent with
/// respect to this wrapper: all stateful behavior lives in the target Obsidian
/// command itself. This interface is additive and intentionally decoupled from
/// <see cref="IObsidianCli"/>'s note/folder surface.
/// </summary>
public interface IObsidianCommandInvoker
{
    /// <summary>
    /// Executes <c>obsidian command id=&lt;commandId&gt;</c>. Returns a result
    /// capturing success/failure derived from stdout (CLI exits 0 on errors).
    /// Throws <see cref="ArgumentException"/> for empty, overlong, or
    /// control-character-bearing <paramref name="commandId"/> values.
    /// </summary>
    Task<CommandRunResult> RunCommandAsync(string commandId, CancellationToken ct = default);

    /// <summary>
    /// Executes <c>obsidian commands [filter=&lt;prefix&gt;]</c> and returns
    /// the resulting command IDs in the order emitted by the CLI. Blank lines
    /// are ignored. Returns an empty list on failure (CLI unavailable, timeout,
    /// or stdout-reported error).
    /// </summary>
    Task<IReadOnlyList<string>> ListCommandsAsync(string? prefix = null, CancellationToken ct = default);
}
