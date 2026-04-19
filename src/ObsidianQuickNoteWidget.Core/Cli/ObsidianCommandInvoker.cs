using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Cli;

/// <summary>
/// Wraps the Obsidian CLI <c>command</c> and <c>commands</c> verbs.
///
/// Live-CLI contract (probed against Obsidian 1.12.7 at
/// <c>C:\Program Files\Obsidian\Obsidian.com</c>, matches v1/v2 cli-probe):
/// <list type="bullet">
///   <item>Process exit code is <b>always 0</b> for both verbs, even on error.</item>
///   <item>Success (<c>command id=workspace:next-tab</c>) stdout:
///         <c>Executed: &lt;command-id&gt;</c> (one line, possibly followed by
///         command-specific output, or empty for some plugin commands).</item>
///   <item>Error (unknown id, e.g. <c>command id=this:does-not-exist</c>) stdout:
///         <c>Error: Command "&lt;id&gt;" not found. Use "commands" to list …</c>
///         — prefix <c>Error:</c> is the authoritative failure signal.</item>
///   <item><c>commands [filter=&lt;prefix&gt;]</c> prints one command id per
///         stdout line (e.g. <c>workspace:close</c>, <c>workspace:close-others</c>,
///         <c>workspace:close-window</c>).</item>
/// </list>
/// Input is validated before spawning the process: command ids are trimmed,
/// rejected if empty, longer than <see cref="MaxCommandIdLength"/>, or
/// containing C0/DEL control characters (CWE-77/CWE-88 defense-in-depth —
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> already
/// guards against argv injection, F-01).
/// </summary>
public sealed class ObsidianCommandInvoker : IObsidianCommandInvoker
{
    internal const int MaxCommandIdLength = 256;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IObsidianCli _cli;
    private readonly ILog _log;

    public ObsidianCommandInvoker(IObsidianCli cli, ILog? log = null)
    {
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _log = log ?? NullLog.Instance;
    }

    public async Task<CommandRunResult> RunCommandAsync(string commandId, CancellationToken ct = default)
    {
        var id = ValidateCommandId(commandId, nameof(commandId));

        if (!_cli.IsAvailable)
        {
            return new CommandRunResult(false, "Obsidian CLI not available", string.Empty);
        }

        var r = await _cli.RunAsync(["command", "id=" + id], timeout: Timeout, ct: ct).ConfigureAwait(false);
        var stdoutTrimmed = (r.StdOut ?? string.Empty).Trim();

        if (!r.Succeeded)
        {
            // ExitCode == -1 is the sentinel our IObsidianCli uses for
            // start-failure and timeout (see ObsidianCli.RunAsync).
            var msg = string.IsNullOrWhiteSpace(r.StdErr) ? "obsidian command failed" : r.StdErr.Trim();
            _log.Warn($"command id={FileLog.SanitizeForLogLine(id)} failed: {FileLog.SanitizeForLogLine(msg)}");
            return new CommandRunResult(false, msg, stdoutTrimmed);
        }

        if (TryExtractCliError(stdoutTrimmed, out var cliError))
        {
            _log.Warn($"command id={FileLog.SanitizeForLogLine(id)} reported error: {FileLog.SanitizeForLogLine(cliError)}");
            return new CommandRunResult(false, cliError, stdoutTrimmed);
        }

        return new CommandRunResult(true, null, stdoutTrimmed);
    }

    public async Task<IReadOnlyList<string>> ListCommandsAsync(string? prefix = null, CancellationToken ct = default)
    {
        if (!_cli.IsAvailable) return Array.Empty<string>();

        var args = new List<string> { "commands" };
        if (!string.IsNullOrEmpty(prefix))
        {
            var cleanPrefix = ValidateCommandId(prefix, nameof(prefix));
            args.Add("filter=" + cleanPrefix);
        }

        var r = await _cli.RunAsync(args, timeout: Timeout, ct: ct).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            _log.Warn($"obsidian commands failed: {FileLog.SanitizeForLogLine(r.StdErr?.Trim() ?? string.Empty)}");
            return Array.Empty<string>();
        }

        if (TryExtractCliError(r.StdOut ?? string.Empty, out var cliError))
        {
            _log.Warn($"obsidian commands reported error: {FileLog.SanitizeForLogLine(cliError)}");
            return Array.Empty<string>();
        }

        return ParseCommandList(r.StdOut);
    }

    internal static IReadOnlyList<string> ParseCommandList(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return Array.Empty<string>();

        var list = new List<string>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            list.Add(line);
        }
        return list;
    }

    internal static string ValidateCommandId(string value, string paramName)
    {
        if (value is null) throw new ArgumentNullException(paramName);

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Command id must not be empty or whitespace.", paramName);
        if (trimmed.Length > MaxCommandIdLength)
            throw new ArgumentException(
                $"Command id length {trimmed.Length} exceeds maximum {MaxCommandIdLength}.", paramName);

        foreach (var c in trimmed)
        {
            if (c < 0x20 || c == 0x7f)
                throw new ArgumentException("Command id must not contain control characters.", paramName);
        }

        return trimmed;
    }

    private static bool TryExtractCliError(string stdout, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrEmpty(stdout)) return false;

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Error:", StringComparison.Ordinal))
            {
                message = line.Substring("Error:".Length).Trim();
                if (message.Length == 0) message = line;
                return true;
            }
        }
        return false;
    }
}
