using System.Text;

namespace ObsidianQuickNoteWidget.Core.Logging;

/// <summary>
/// Rolling file log at %LocalAppData%\ObsidianQuickNoteWidget\log.txt. Thread-safe.
/// Local-only: no telemetry, no network. Logging never throws.
/// </summary>
public sealed class FileLog : ILog
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private const long MaxBytes = 1_000_000;

    public FileLog(string? path = null)
    {
        _path = path ?? DefaultPath();
        // F-08: create log directory with owner-only ACLs on Windows.
        // Logged-to-self warnings are silently dropped at this stage since the
        // log isn't up yet; any failure is bubbled through later writes.
        ObsidianQuickNoteWidget.Core.IO.DirectorySecurityHelper.CreateWithOwnerOnlyAcl(
            Path.GetDirectoryName(_path)!, log: null);
    }

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "ObsidianQuickNoteWidget", "log.txt");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {SanitizeForLogLine(ex.ToString())}");

    /// <summary>
    /// Neutralizes log-injection vectors (CWE-117): replaces CR, LF, and other
    /// C0 control characters (except TAB) with printable escape literals so
    /// attacker-influenced fields can't forge additional log lines.
    /// UTF-8 characters (including non-ASCII) are preserved verbatim.
    /// </summary>
    internal static string SanitizeForLogLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        StringBuilder? sb = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool needsEscape = c switch
            {
                '\r' => true,
                '\n' => true,
                '\t' => false,
                _ => c < 0x20 || c == 0x7f,
            };
            if (needsEscape)
            {
                sb ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
                sb.Append(c switch
                {
                    '\r' => "\\r",
                    '\n' => "\\n",
                    _ => $"\\u{(int)c:X4}",
                });
            }
            else
            {
                sb?.Append(c);
            }
        }
        return sb?.ToString() ?? value;
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {SanitizeForLogLine(message)}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                Roll();
                File.AppendAllText(_path, line);
            }
            catch
            {
            }
        }
    }

    private void Roll()
    {
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
            {
                var old = _path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_path, old);
            }
        }
        catch { /* ignore */ }
    }
}
