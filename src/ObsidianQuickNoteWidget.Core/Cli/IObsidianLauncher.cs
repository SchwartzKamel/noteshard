namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Launches Obsidian via the registered <c>obsidian://</c> URI handler.
/// Unlike <see cref="IObsidianCli"/> — which shells out to the bundled
/// <c>obsidian.exe</c> CLI and fails when Obsidian isn't running (the CLI
/// requires a running Obsidian instance to receive IPC) — the URI scheme
/// hands the request to the OS shell, which starts Obsidian if it isn't
/// already up and focuses the vault/note either way.
/// </summary>
public interface IObsidianLauncher
{
    /// <summary>Launches Obsidian and focuses the active vault.</summary>
    Task<bool> LaunchVaultAsync(CancellationToken ct = default);

    /// <summary>Launches Obsidian and opens a vault-relative note path.</summary>
    Task<bool> LaunchNoteAsync(string vaultRelativePath, CancellationToken ct = default);

    /// <summary>Returns the vault name that would be used in the URI, or null when unresolvable. For diagnostics only.</summary>
    string? ResolveVaultName();
}
