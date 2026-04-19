using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.IO;

/// <summary>
/// Creates directories with owner-only (user-only) ACLs on Windows, closing
/// F-08 (inherited ACLs on <c>%LocalAppData%\ObsidianQuickNoteWidget\</c>).
/// On non-Windows platforms the tightening step is a no-op; the directory is
/// still created so callers have a single entry-point.
/// <para>
/// Idempotent: per-path tightening happens at most once per process so
/// repeated constructor calls (e.g. multiple <see cref="State.JsonStateStore"/>
/// instances) remain cheap.
/// </para>
/// <para>
/// Best-effort: all exceptions are swallowed (log-only) — the widget must
/// never crash over ACL-tightening failure.
/// </para>
/// </summary>
internal static class DirectorySecurityHelper
{
    private static readonly ConcurrentDictionary<string, byte> s_tightened =
        new(StringComparer.OrdinalIgnoreCase);

    public static void CreateWithOwnerOnlyAcl(string path, ILog? log = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            log?.Warn($"create dir failed: {FileLog.SanitizeForLogLine(ex.Message)}");
            return;
        }

        if (!OperatingSystem.IsWindows()) return;

        string normalized;
        try { normalized = Path.GetFullPath(path); }
        catch { return; }

        if (!s_tightened.TryAdd(normalized, 0)) return;

        TightenAclWindows(normalized, log);
    }

    [SupportedOSPlatform("windows")]
    private static void TightenAclWindows(string path, ILog? log)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var sec = di.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is not null)
            {
                var rule = new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);
                sec.AddAccessRule(rule);
            }

            di.SetAccessControl(sec);
        }
        catch (Exception ex)
        {
            log?.Warn($"tighten acl failed: {FileLog.SanitizeForLogLine(ex.Message)}");
        }
    }

    internal static void ResetForTests() => s_tightened.Clear();
}
