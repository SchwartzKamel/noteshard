using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// URI-scheme launcher for Obsidian. Resolves the active vault from
/// <c>%APPDATA%\obsidian\obsidian.json</c> (prefer <c>"open": true</c>; else
/// newest <c>ts</c>; else first) and builds an <c>obsidian://open</c> URI
/// that the OS shell hands to the registered protocol handler. The handler
/// launches Obsidian if it isn't running, so this works whether or not
/// Obsidian is up — unlike the bundled <c>obsidian</c> CLI, which requires a
/// running instance (see README / F-03).
///
/// Overrides (testability + user opt-out):
/// <list type="bullet">
///   <item><c>OBSIDIAN_VAULT</c> — forces vault name, bypasses config discovery.</item>
///   <item><c>OBSIDIAN_VAULTS_CONFIG</c> — custom path to <c>obsidian.json</c>.</item>
/// </list>
/// </summary>
public sealed class ObsidianLauncher : IObsidianLauncher
{
    private readonly ILog _log;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readAllText;
    private readonly Action<ProcessStartInfo> _spawn;
    private readonly Func<string?> _getVaultEnv;
    private readonly Func<string?> _getConfigPath;

    public ObsidianLauncher(ILog? log = null)
        : this(
            log,
            File.Exists,
            File.ReadAllText,
            psi => { using var _ = Process.Start(psi); },
            () => Environment.GetEnvironmentVariable("OBSIDIAN_VAULT"),
            () => Environment.GetEnvironmentVariable("OBSIDIAN_VAULTS_CONFIG") ?? DefaultConfigPath())
    {
    }

    internal ObsidianLauncher(
        ILog? log,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        Action<ProcessStartInfo> spawn,
        Func<string?> getVaultEnv,
        Func<string?> getConfigPath)
    {
        _log = log ?? NullLog.Instance;
        _fileExists = fileExists;
        _readAllText = readAllText;
        _spawn = spawn;
        _getVaultEnv = getVaultEnv;
        _getConfigPath = getConfigPath;
    }

    internal static string DefaultConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obsidian", "obsidian.json");

    public string? ResolveVaultName()
    {
        var fromEnv = _getVaultEnv();
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var cfgPath = _getConfigPath();
        if (string.IsNullOrWhiteSpace(cfgPath) || !_fileExists(cfgPath))
        {
            _log.Warn($"Obsidian vaults config not found at '{cfgPath}'");
            return null;
        }

        string raw;
        try
        {
            raw = _readAllText(cfgPath);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to read obsidian.json: {ex.Message}");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("vaults", out var vaults) ||
                vaults.ValueKind != JsonValueKind.Object)
            {
                _log.Warn("obsidian.json missing 'vaults' object");
                return null;
            }

            string? bestPath = null;
            long bestTs = long.MinValue;
            bool bestOpen = false;
            bool any = false;

            foreach (var vault in vaults.EnumerateObject())
            {
                if (vault.Value.ValueKind != JsonValueKind.Object) continue;
                if (!vault.Value.TryGetProperty("path", out var pathEl) ||
                    pathEl.ValueKind != JsonValueKind.String) continue;
                var path = pathEl.GetString();
                if (string.IsNullOrWhiteSpace(path)) continue;

                long ts = 0;
                if (vault.Value.TryGetProperty("ts", out var tsEl) &&
                    tsEl.ValueKind == JsonValueKind.Number &&
                    tsEl.TryGetInt64(out var tsVal))
                {
                    ts = tsVal;
                }
                bool open = vault.Value.TryGetProperty("open", out var openEl) &&
                    openEl.ValueKind == JsonValueKind.True;

                // Preference: open==true wins; otherwise newest ts; first entry otherwise.
                bool replace = !any ||
                    (open && !bestOpen) ||
                    (open == bestOpen && ts > bestTs);
                any = true;
                if (replace)
                {
                    bestPath = path;
                    bestTs = ts;
                    bestOpen = open;
                }
            }

            if (!any || bestPath is null)
            {
                _log.Warn("obsidian.json has no usable vault entries");
                return null;
            }

            var name = Path.GetFileName(bestPath.TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (JsonException ex)
        {
            _log.Warn($"obsidian.json is malformed: {ex.Message}");
            return null;
        }
    }

    public Task<bool> LaunchVaultAsync(CancellationToken ct = default)
    {
        var name = ResolveVaultName();
        if (string.IsNullOrWhiteSpace(name))
        {
            _log.Warn("LaunchVaultAsync: no vault name resolvable");
            return Task.FromResult(false);
        }

        var uri = $"obsidian://open?vault={Uri.EscapeDataString(name)}";
        return Task.Run(() => Shell(uri), ct);
    }

    public Task<bool> LaunchNoteAsync(string vaultRelativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRelativePath))
        {
            _log.Warn("LaunchNoteAsync: empty path");
            return Task.FromResult(false);
        }
        if (!IsSafeRelativePath(vaultRelativePath))
        {
            _log.Warn($"LaunchNoteAsync: rejecting unsafe path '{vaultRelativePath}'");
            return Task.FromResult(false);
        }

        var name = ResolveVaultName();
        if (string.IsNullOrWhiteSpace(name))
        {
            _log.Warn("LaunchNoteAsync: no vault name resolvable");
            return Task.FromResult(false);
        }

        // Obsidian URI spec: `file=` takes a vault-relative path; extension is
        // optional (the handler resolves both "foo" and "foo.md"). We forward
        // exactly what we were given so the handler can disambiguate.
        var uri = $"obsidian://open?vault={Uri.EscapeDataString(name)}&file={Uri.EscapeDataString(vaultRelativePath)}";
        return Task.Run(() => Shell(uri), ct);
    }

    private bool Shell(string uri)
    {
        try
        {
            _spawn(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Win32Exception ex)
        {
            _log.Warn($"Shell-execute of obsidian:// URI failed: {ex.Message}");
            return false;
        }
        catch (ArgumentException ex)
        {
            _log.Warn($"Shell-execute of obsidian:// URI rejected: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _log.Warn($"Shell-execute of obsidian:// URI failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Defensive check for values forwarded to an <c>obsidian://</c> URI.
    /// Rejects control characters, absolute paths, and <c>..</c> traversal
    /// segments. Path separators and spaces are allowed — they're the norm
    /// for vault-relative notes (e.g. <c>Inbox/My note.md</c>).
    /// </summary>
    internal static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (var c in path)
        {
            if (c < 0x20 || c == 0x7f) return false;
        }
        if (Path.IsPathRooted(path)) return false;
        // Normalize separators then check segments.
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (seg == "..") return false;
        }
        return true;
    }
}
