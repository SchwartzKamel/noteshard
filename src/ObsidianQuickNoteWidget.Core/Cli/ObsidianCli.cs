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
    private static readonly string[] WindowsPathExtensions = [".exe", ".cmd", ".bat", ""];
    private static readonly string[] UnixPathExtensions = [""];
    private readonly string? _resolvedExe;
    private readonly ILog _log;

    public ObsidianCli(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _resolvedExe = ResolveExecutable();
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
        // The Obsidian CLI has no verb for "open the vault itself" — `vault` is
        // an info query (TSV output), not a UI-focus command. Surface empty-path
        // calls as an explicit no-op so callers can't accidentally trigger the
        // info query thinking it opens something.
        if (string.IsNullOrWhiteSpace(vaultRelativePath))
        {
            _log.Warn("OpenNoteAsync called with empty path; Obsidian CLI has no 'open vault' verb.");
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

    private static string? ResolveExecutable()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exts = OperatingSystem.IsWindows() ? WindowsPathExtensions : UnixPathExtensions;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, ExecutableName + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
