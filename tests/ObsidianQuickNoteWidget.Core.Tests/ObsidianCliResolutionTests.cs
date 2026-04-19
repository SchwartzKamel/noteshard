using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class ObsidianCliResolutionTests
{
    private sealed class FakeEnv : IObsidianCliEnvironment
    {
        public bool IsWindows { get; set; } = true;
        public Dictionary<string, string?> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileAttributes> Attrs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? RegistryCommand { get; set; }

        public string? GetEnvironmentVariable(string name) =>
            Vars.TryGetValue(name, out var v) ? v : null;
        public bool FileExists(string path) =>
            !string.IsNullOrWhiteSpace(path) && Files.Contains(path);
        public FileAttributes GetFileAttributes(string path) =>
            Attrs.TryGetValue(path, out var a) ? a : FileAttributes.Normal;
        public string? GetObsidianProtocolOpenCommand() => RegistryCommand;
    }

    private sealed class CapturingLog : ILog
    {
        public List<string> Warnings { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? ex = null) { }
    }

    public ObsidianCliResolutionTests()
    {
        // The PATH-fallback "once per process" warning uses a static flag.
        // Reset it so tests are order-independent.
        ObsidianCli.ResetPathWarningForTests();
    }

    [Fact]
    public void KnownInstallLocation_WinsOverPath()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["ProgramFiles"] = @"C:\Program Files",
                ["PATH"] = @"C:\tools",
            },
        };
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.com");
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.exe");
        env.Files.Add(@"C:\tools\obsidian.exe");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Equal(@"C:\Program Files\Obsidian\Obsidian.com", resolved);
    }

    [Fact]
    public void PerUserInstall_UsedWhenProgramFilesMissing()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["ProgramFiles"] = @"C:\Program Files",
                ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
                ["PATH"] = @"C:\tools",
            },
        };
        env.Files.Add(@"C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe");
        env.Files.Add(@"C:\tools\obsidian.exe");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe", resolved);
    }

    [Fact]
    public void CmdAndBatInPath_AreRejected_EvenIfPresent()
    {
        var env = new FakeEnv
        {
            Vars = { ["PATH"] = @"C:\evil;C:\evil2" },
        };
        env.Files.Add(@"C:\evil\obsidian.cmd");
        env.Files.Add(@"C:\evil2\obsidian.bat");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Null(resolved);
    }

    [Fact]
    public void PathScanAcceptsComAndExeOnly_AndWarnsOnce()
    {
        var env = new FakeEnv
        {
            Vars = { ["PATH"] = @"C:\tools" },
        };
        env.Files.Add(@"C:\tools\obsidian.exe");

        var log = new CapturingLog();
        var resolved = ObsidianCli.ResolveExecutable(env, log);

        Assert.Equal(@"C:\tools\obsidian.exe", resolved);
        Assert.Single(log.Warnings);
        Assert.Contains("via PATH", log.Warnings[0]);
        Assert.Contains("OBSIDIAN_CLI", log.Warnings[0]);

        // Second call in same process should not warn again.
        var log2 = new CapturingLog();
        var resolved2 = ObsidianCli.ResolveExecutable(env, log2);
        Assert.Equal(@"C:\tools\obsidian.exe", resolved2);
        Assert.Empty(log2.Warnings);
    }

    [Fact]
    public void ObsidianCliEnvVar_WinsOverAllElse()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["OBSIDIAN_CLI"] = @"D:\custom\my-obsidian.exe",
                ["ProgramFiles"] = @"C:\Program Files",
                ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
                ["PATH"] = @"C:\tools",
            },
            RegistryCommand = @"""C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe"" --protocol %1",
        };
        env.Files.Add(@"D:\custom\my-obsidian.exe");
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.com");
        env.Files.Add(@"C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe");
        env.Files.Add(@"C:\tools\obsidian.exe");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Equal(@"D:\custom\my-obsidian.exe", resolved);
    }

    [Fact]
    public void ObsidianCliEnvVar_IgnoredWhenFileDoesNotExist()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["OBSIDIAN_CLI"] = @"D:\does\not\exist.exe",
                ["ProgramFiles"] = @"C:\Program Files",
            },
        };
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.exe");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Equal(@"C:\Program Files\Obsidian\Obsidian.exe", resolved);
    }

    // F-12: UNC override rejected; fall through to the next resolver.
    [Theory]
    [InlineData(@"\\attacker\share\obsidian.exe")]
    [InlineData(@"\\?\C:\Obsidian\obsidian.exe")]
    [InlineData(@"\\.\pipe\evil")]
    [InlineData(@"//server/share/obsidian.exe")]
    public void ObsidianCliResolution_RejectsUncOverride(string badOverride)
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["OBSIDIAN_CLI"] = badOverride,
                ["ProgramFiles"] = @"C:\Program Files",
            },
        };
        env.Files.Add(badOverride);
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.exe");

        var log = new CapturingLog();
        var resolved = ObsidianCli.ResolveExecutable(env, log);

        Assert.Equal(@"C:\Program Files\Obsidian\Obsidian.exe", resolved);
        Assert.Contains(log.Warnings, w =>
            w.Contains("OBSIDIAN_CLI override rejected", StringComparison.Ordinal) &&
            w.Contains("UNC", StringComparison.OrdinalIgnoreCase));
    }

    // F-12: relative override rejected (resolver can't audit it safely).
    [Fact]
    public void ObsidianCliResolution_RejectsRelativeOverride()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["OBSIDIAN_CLI"] = @"obsidian.exe",
                ["ProgramFiles"] = @"C:\Program Files",
            },
        };
        env.Files.Add("obsidian.exe");
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.exe");

        var log = new CapturingLog();
        var resolved = ObsidianCli.ResolveExecutable(env, log);

        Assert.Equal(@"C:\Program Files\Obsidian\Obsidian.exe", resolved);
        Assert.Contains(log.Warnings, w =>
            w.Contains("OBSIDIAN_CLI override rejected", StringComparison.Ordinal) &&
            w.Contains("not fully qualified", StringComparison.OrdinalIgnoreCase));
    }

    // F-12: reparse-point (symlink / junction) override rejected.
    [Fact]
    public void ObsidianCliResolution_RejectsReparsePointOverride()
    {
        const string bad = @"D:\symlink\obsidian.exe";
        var env = new FakeEnv
        {
            Vars =
            {
                ["OBSIDIAN_CLI"] = bad,
                ["ProgramFiles"] = @"C:\Program Files",
            },
        };
        env.Files.Add(bad);
        env.Attrs[bad] = FileAttributes.ReparsePoint | FileAttributes.Archive;
        env.Files.Add(@"C:\Program Files\Obsidian\Obsidian.exe");

        var log = new CapturingLog();
        var resolved = ObsidianCli.ResolveExecutable(env, log);

        Assert.Equal(@"C:\Program Files\Obsidian\Obsidian.exe", resolved);
        Assert.Contains(log.Warnings, w =>
            w.Contains("OBSIDIAN_CLI override rejected", StringComparison.Ordinal) &&
            w.Contains("reparse-point", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegistryCommand_UsedWhenKnownInstallPathsMissing()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["ProgramFiles"] = @"C:\Program Files",
                ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
                ["PATH"] = string.Empty,
            },
            RegistryCommand =
                @"""C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe"" --protocol ""%1""",
        };
        env.Files.Add(@"C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe");

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\Obsidian\Obsidian.exe", resolved);
    }

    [Fact]
    public void MissingEverywhere_ReturnsNull()
    {
        var env = new FakeEnv
        {
            Vars =
            {
                ["ProgramFiles"] = @"C:\Program Files",
                ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
                ["PATH"] = @"C:\tools;C:\other",
            },
        };

        var resolved = ObsidianCli.ResolveExecutable(env, NullLog.Instance);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\"C:\\Obsidian\\Obsidian.exe\" --protocol %1", "C:\\Obsidian\\Obsidian.exe")]
    [InlineData("C:\\Obsidian\\Obsidian.exe --protocol %1", "C:\\Obsidian\\Obsidian.exe")]
    [InlineData("C:\\Obsidian\\Obsidian.exe", "C:\\Obsidian\\Obsidian.exe")]
    [InlineData("\"unterminated", null)]
    public void ExtractExeFromRegistryCommand_Parses(string? input, string? expected)
    {
        Assert.Equal(expected, ObsidianCli.ExtractExeFromRegistryCommand(input));
    }
}
