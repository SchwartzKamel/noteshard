using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ObsidianQuickNoteWidget.Core.IO;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class DirectorySecurityHelperTests
{
    // F-08: new directories under %LocalAppData%\ObsidianQuickNoteWidget\ must
    // have inheritance disabled and only grant FullControl to the current user
    // (plus whatever the creator-owner / SYSTEM already owns via the directory
    // creation path — we only add, never strip). On non-Windows this is a no-op
    // and the test is skipped.
    [Fact]
    public void DirectorySecurityHelper_AppliesOwnerOnlyAcl_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        DirectorySecurityHelper.ResetForTests();

        var dir = Path.Combine(Path.GetTempPath(),
            "oqnw-acl-" + Guid.NewGuid().ToString("N"));
        try
        {
            DirectorySecurityHelper.CreateWithOwnerOnlyAcl(dir);
            Assert.True(Directory.Exists(dir));

            AssertOwnerOnlyAcl(dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    // F-08: calling twice is a no-op — second call should not throw and the
    // directory must still end up with the owner-only ACL.
    [Fact]
    public void DirectorySecurityHelper_Idempotent_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        DirectorySecurityHelper.ResetForTests();

        var dir = Path.Combine(Path.GetTempPath(),
            "oqnw-acl-" + Guid.NewGuid().ToString("N"));
        try
        {
            DirectorySecurityHelper.CreateWithOwnerOnlyAcl(dir);
            DirectorySecurityHelper.CreateWithOwnerOnlyAcl(dir);
            AssertOwnerOnlyAcl(dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOwnerOnlyAcl(string dir)
    {
        var di = new DirectoryInfo(dir);
        var sec = di.GetAccessControl();
        Assert.True(sec.AreAccessRulesProtected,
            "inheritance must be disabled so parent ACLs don't apply");

        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User!;
        var rules = sec.GetAccessRules(includeExplicit: true, includeInherited: false,
            targetType: typeof(SecurityIdentifier));
        bool userHasFullControl = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference.Value.Equals(userSid.Value, StringComparison.Ordinal) &&
                rule.AccessControlType == AccessControlType.Allow &&
                rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
            {
                userHasFullControl = true;
                break;
            }
        }
        Assert.True(userHasFullControl, "current user must have explicit Allow FullControl");
    }
}
