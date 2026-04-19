using System.Diagnostics;
using System.Text;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Process-based implementation of <see cref="IObsidianCli"/> targeting the
/// official Obsidian CLI 1.12+. The CLI uses positional <c>key=value</c> args
/// (NOT <c>--flag</c>), returns key/value or newline-delimited output, and
/// interprets literal <c>\n</c>/<c>\t</c> inside <c>content=</c> values as
/// newlines/tabs — we escape accordingly.
/// </summary>
public sealed class ObsidianCli : IObsidianCli
{
    private const string ExecutableName = "obsidian";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] VaultInfoPathArgs = ["vault", "info=path"];
    private static readonly string[] FoldersArgs = ["folders"];
    private static readonly string[] RecentsArgs = ["recents"];
    private static readonly string[] FilesArgs = ["files"];
    // Only genuine PE executables are accepted. `.cmd` / `.bat` are rejected to
    // close a PATH-hijack vector (CWE-426/427): an attacker-writable PATH entry
    // containing `obsidian.cmd` would otherwise run arbitrary shell script with
    // user-controlled args on every widget refresh. See audit-reports/security-auditor.md F-02.
    // TODO(F-02 follow-up): verify Authenticode signature (WinVerifyTrust, expected
    // subject "Obsidian.md Inc.") before first spawn per process lifetime.
    private static readonly string[] WindowsPathExtensions = [".com", ".exe"];
    private static readonly string[] UnixPathExtensions = [""];
    private static int s_warnedPathResolution;
    private readonly string? _resolvedExe;
    private readonly ILog _log;

    public ObsidianCli(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _resolvedExe = ResolveExecutable(DefaultObsidianCliEnvironment.Instance, _log);
    }

    public bool IsAvailable => _resolvedExe is not null;

    public async Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_resolvedExe is null)
        {
            return new CliResult(-1, string.Empty, "obsidian CLI not found on PATH", TimeSpan.Zero);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _resolvedExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            _log.Warn($"obsidian start failed: {ex.Message}");
            return new CliResult(-1, string.Empty, ex.Message, sw.Elapsed);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            proc.StandardInput.Close();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new CliResult(-1, stdout.ToString(), "obsidian CLI timed out", sw.Elapsed);
        }

        return new CliResult(proc.ExitCode, stdout.ToString(), stderr.ToString(), sw.Elapsed);
    }

    public async Task<string?> GetVaultRootAsync(CancellationToken ct = default)
    {
        // `obsidian vault info=path` returns a single line: the vault root path.
        var r = await RunAsync(VaultInfoPathArgs, ct: ct).ConfigureAwait(false);
        if (r.Succeeded)
        {
            var line = ObsidianCliParsers.ParseVaultPath(r.StdOut);
            if (!string.IsNullOrWhiteSpace(line) && Directory.Exists(line)) return line;
            _log.Warn($"vault info=path returned unexpected output: '{line}'");
        }
        return null;
    }

    public async Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        // `obsidian folders` lists every folder in the vault, one per line,
        // forward-slash separated. `/` denotes the vault root — we filter it out
        // since the UI already provides an explicit "(vault root)" option.
        var r = await RunAsync(FoldersArgs, ct: ct).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            _log.Warn($"obsidian folders failed: {r.StdErr.Trim()}");
            return Array.Empty<string>();
        }

        var list = ObsidianCliParsers.ParseFolders(r.StdOut);

        _log.Info($"ListFoldersAsync: {list.Count} folder(s)");
        return list;
    }

    public async Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
    {
        // `obsidian recents` prints up to 10 vault-relative paths (one per line),
        // mixing files and folders. We keep `.md` files only and cap to `max`.
        var r = await RunAsync(RecentsArgs, ct: ct).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            _log.Warn($"obsidian recents failed: {r.StdErr.Trim()}");
            return Array.Empty<string>();
        }

        if (ObsidianCliParsers.HasCliError(r.StdOut))
        {
            _log.Warn($"obsidian recents reported error on stdout: {r.StdOut.Trim()}");
            return Array.Empty<string>();
        }

        var list = ObsidianCliParsers.ParseRecents(r.StdOut, max);
        _log.Info($"ListRecentsAsync: {list.Count} note(s)");
        return list;
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default)
    {
        // `obsidian files` lists every file in the vault, one per line. We
        // keep only `.md` entries — callers intersect this with `recents` to
        // drop ghost paths for deleted files.
        var r = await RunAsync(FilesArgs, ct: ct).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            _log.Warn($"obsidian files failed: {r.StdErr.Trim()}");
            return Array.Empty<string>();
        }

        if (ObsidianCliParsers.HasCliError(r.StdOut))
        {
            _log.Warn($"obsidian files reported error on stdout: {r.StdOut.Trim()}");
            return Array.Empty<string>();
        }

        var list = ObsidianCliParsers.ParseFiles(r.StdOut);
        _log.Info($"ListFilesAsync: {list.Count} file(s)");
        return list;
    }

    public async Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
    {
        // Obsidian CLI expects `create path=<vault-relative> content=<text>`.
        // Content values interpret literal `\n`/`\t` as newlines/tabs, so escape.
        var args = new List<string> { "create", "path=" + vaultRelativePath };
        if (!string.IsNullOrEmpty(body)) args.Add("content=" + ObsidianCliParsers.EscapeContent(body));

        var r = await RunAsync(args, ct: ct).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            _log.Warn($"create failed (exit={r.ExitCode}): {r.StdErr.Trim()}");
            return null;
        }

        // The CLI returns exit=0 for every error; authoritative signal is stdout.
        if (ObsidianCliParsers.HasCliError(r.StdOut))
        {
            _log.Warn($"create reported error on stdout: {r.StdOut.Trim()}");
            return null;
        }

        // On success, stdout is `Created: <path>` or `Overwrote: <path>`. The CLI
        // silently auto-renames on collision (e.g. `p1.md` → `p1 1.md`), so the
        // path returned here may differ from `vaultRelativePath`. Propagate what
        // the CLI actually created.
        if (!ObsidianCliParsers.TryParseCreated(r.StdOut, out var createdPath))
        {
            _log.Warn($"create succeeded but stdout did not contain Created:/Overwrote: line: {r.StdOut.Trim()}");
            return null;
        }

        return createdPath;
    }

    public async Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
    {
        // Precondition: a valid vault-relative path. Callers that want to
        // launch Obsidian itself should use IObsidianLauncher (URI scheme);
        // this CLI method only opens specific notes and requires Obsidian
        // to already be running.
        if (string.IsNullOrWhiteSpace(vaultRelativePath))
        {
            return false;
        }

        var r = await RunAsync(["open", "path=" + vaultRelativePath], ct: ct).ConfigureAwait(false);
        if (!r.Succeeded) return false;

        if (ObsidianCliParsers.HasCliError(r.StdOut))
        {
            _log.Warn($"open reported error on stdout: {r.StdOut.Trim()}");
            return false;
        }
        return true;
    }

    public async Task<bool> AppendDailyAsync(string text, CancellationToken ct = default)
    {
        var r = await RunAsync(["daily:append", "content=" + ObsidianCliParsers.EscapeContent(text)], ct: ct).ConfigureAwait(false);
        if (!r.Succeeded) return false;

        if (ObsidianCliParsers.HasCliError(r.StdOut))
        {
            _log.Warn($"daily:append reported error on stdout: {r.StdOut.Trim()}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Preference order for locating the <c>obsidian</c> executable (see F-02):
    /// <list type="number">
    ///   <item>Explicit override via <c>OBSIDIAN_CLI</c> env var (if it points at an existing file).</item>
    ///   <item>Per-machine install: <c>%ProgramFiles%\Obsidian\Obsidian.(com|exe)</c>.</item>
    ///   <item>Per-user install: <c>%LocalAppData%\Programs\Obsidian\Obsidian.(com|exe)</c>.</item>
    ///   <item>Registry: default value of <c>HKCU\Software\Classes\obsidian\shell\open\command</c>.</item>
    ///   <item>PATH scan, <c>.com</c> / <c>.exe</c> only. Emits a one-shot warning.</item>
    /// </list>
    /// </summary>
    internal static string? ResolveExecutable(IObsidianCliEnvironment env, ILog log)
    {
        var overridePath = env.GetEnvironmentVariable("OBSIDIAN_CLI");
        if (!string.IsNullOrWhiteSpace(overridePath) && env.FileExists(overridePath))
        {
            return overridePath;
        }

        if (env.IsWindows)
        {
            foreach (var candidate in EnumerateKnownWindowsInstallPaths(env))
            {
                if (env.FileExists(candidate)) return candidate;
            }

            var regCommand = env.GetObsidianProtocolOpenCommand();
            var regExe = ExtractExeFromRegistryCommand(regCommand);
            if (regExe is not null && env.FileExists(regExe))
            {
                return regExe;
            }
        }

        // PATH fallback (last resort).
        var pathVar = env.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exts = env.IsWindows ? WindowsPathExtensions : UnixPathExtensions;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, ExecutableName + ext);
                if (env.FileExists(candidate))
                {
                    if (Interlocked.Exchange(ref s_warnedPathResolution, 1) == 0)
                    {
                        log.Warn(
                            $"Resolved 'obsidian' via PATH ({candidate}) " +
                            "— consider setting OBSIDIAN_CLI to a fully-qualified path");
                    }
                    return candidate;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateKnownWindowsInstallPaths(IObsidianCliEnvironment env)
    {
        var programFiles = env.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Obsidian", "Obsidian.com");
            yield return Path.Combine(programFiles, "Obsidian", "Obsidian.exe");
        }
        var localAppData = env.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Obsidian", "Obsidian.com");
            yield return Path.Combine(localAppData, "Programs", "Obsidian", "Obsidian.exe");
        }
    }

    /// <summary>
    /// Parses the exe path out of a registry shell-open command string, e.g.
    /// <c>"C:\Users\x\AppData\Local\Programs\Obsidian\Obsidian.exe" --protocol %1</c>.
    /// </summary>
    internal static string? ExtractExeFromRegistryCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end > 1) return command.Substring(1, end - 1);
            return null;
        }
        // Unquoted — take up to first whitespace.
        var ws = command.IndexOfAny([' ', '\t']);
        return ws < 0 ? command : command[..ws];
    }

    // Reset the one-shot PATH-resolution warning. Tests only.
    internal static void ResetPathWarningForTests() => Interlocked.Exchange(ref s_warnedPathResolution, 0);
}
