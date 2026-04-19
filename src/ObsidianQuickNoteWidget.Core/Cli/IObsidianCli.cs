namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Thin abstraction around the official `obsidian` CLI bundled with Obsidian 1.12+.
/// All methods are best-effort: if the CLI is missing or a sub-command doesn't
/// exist, the caller should surface a friendly error state on the widget.
/// </summary>
public interface IObsidianCli
{
    /// <summary>Returns true when an `obsidian` executable is resolvable on PATH.</summary>
    bool IsAvailable { get; }

    /// <summary>Executes `obsidian &lt;args&gt;` with optional stdin and returns captured output.</summary>
    Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>Resolves the active vault's absolute root path. Returns null when unknown.</summary>
    Task<string?> GetVaultRootAsync(CancellationToken ct = default);

    /// <summary>Lists vault folders (relative to vault root). Returns empty list if unavailable.</summary>
    Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists every live <c>.md</c> file in the vault (vault-relative paths).
    /// Backed by <c>obsidian files</c>. Unlike <see cref="ListRecentsAsync"/>,
    /// this reflects the current on-disk state — callers intersect the recents
    /// list with this to drop ghost entries for deleted files. Returns an
    /// empty list on CLI failure.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists the most recently opened notes (vault-relative paths, newest first).
    /// The native <c>obsidian recents</c> verb returns both files and folders
    /// mixed; this method filters to <c>.md</c> files only and caps the result
    /// at <paramref name="max"/>. Returns an empty list if the CLI is missing
    /// or reports an error.
    /// </summary>
    Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default);

    /// <summary>Creates a note; returns the vault-relative path of the created note, or null on failure.</summary>
    Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default);

    /// <summary>Opens an existing note by its vault-relative path.</summary>
    Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default);

    /// <summary>Appends text to today's daily note.</summary>
    Task<bool> AppendDailyAsync(string text, CancellationToken ct = default);
}
