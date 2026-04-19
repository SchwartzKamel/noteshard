using System.Diagnostics;
using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.Cli;

public class ObsidianLauncherTests
{
    private sealed class Harness
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ProcessStartInfo> Spawned { get; } = new();
        public string? VaultEnv { get; set; }
        public string ConfigPath { get; set; } = @"C:\fake\obsidian.json";
        public Exception? SpawnThrows { get; set; }

        public ObsidianLauncher Build(ILog? log = null) => new(
            log,
            p => Files.ContainsKey(p),
            p => Files[p],
            psi =>
            {
                Spawned.Add(psi);
                if (SpawnThrows is not null) throw SpawnThrows;
            },
            () => VaultEnv,
            () => ConfigPath);
    }

    private const string VaultConfigSingle =
        @"{""vaults"":{""abc"":{""path"":""C:\\Users\\u\\OneDrive\\Obsidian\\lafiamafia"",""ts"":1776495319440,""open"":true}},""cli"":true}";

    [Fact]
    public void ResolveVaultName_SingleOpenVault_ReturnsLeafName()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] = VaultConfigSingle;

        Assert.Equal("lafiamafia", h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_MultipleVaults_OpenTrueWins()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] =
            @"{""vaults"":{
                ""a"":{""path"":""C:\\vaults\\alpha"",""ts"":1000,""open"":false},
                ""b"":{""path"":""C:\\vaults\\bravo"",""ts"":500,""open"":true},
                ""c"":{""path"":""C:\\vaults\\charlie"",""ts"":2000,""open"":false}
            }}";

        Assert.Equal("bravo", h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_NoOpenFlag_NewestTsWins()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] =
            @"{""vaults"":{
                ""a"":{""path"":""C:\\vaults\\alpha"",""ts"":1000},
                ""b"":{""path"":""C:\\vaults\\bravo"",""ts"":3000},
                ""c"":{""path"":""C:\\vaults\\charlie"",""ts"":2000}
            }}";

        Assert.Equal("bravo", h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_EmptyVaultsObject_ReturnsNull()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] = @"{""vaults"":{}}";
        Assert.Null(h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_MissingConfig_ReturnsNull()
    {
        var h = new Harness(); // no files registered
        Assert.Null(h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_MalformedJson_ReturnsNull()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] = "{ not json";
        Assert.Null(h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_MissingVaultsKey_ReturnsNull()
    {
        var h = new Harness();
        h.Files[h.ConfigPath] = @"{""cli"":true}";
        Assert.Null(h.Build().ResolveVaultName());
    }

    [Fact]
    public void ResolveVaultName_EnvOverride_WinsOverConfig()
    {
        var h = new Harness { VaultEnv = "envVault" };
        h.Files[h.ConfigPath] = VaultConfigSingle;

        Assert.Equal("envVault", h.Build().ResolveVaultName());
    }

    [Fact]
    public async Task LaunchVaultAsync_BuildsEncodedUri_AndReturnsTrue()
    {
        var h = new Harness { VaultEnv = "my vault & notes" };
        var result = await h.Build().LaunchVaultAsync();

        Assert.True(result);
        var psi = Assert.Single(h.Spawned);
        Assert.True(psi.UseShellExecute);
        Assert.Equal("obsidian://open?vault=my%20vault%20%26%20notes", psi.FileName);
    }

    [Fact]
    public async Task LaunchVaultAsync_NoVault_ReturnsFalse_NoSpawn()
    {
        var h = new Harness(); // no config, no env
        var result = await h.Build().LaunchVaultAsync();

        Assert.False(result);
        Assert.Empty(h.Spawned);
    }

    [Fact]
    public async Task LaunchNoteAsync_BuildsEncodedUri()
    {
        var h = new Harness { VaultEnv = "vault" };
        var result = await h.Build().LaunchNoteAsync("Inbox/My Note.md");

        Assert.True(result);
        var psi = Assert.Single(h.Spawned);
        Assert.Equal("obsidian://open?vault=vault&file=Inbox%2FMy%20Note.md", psi.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LaunchNoteAsync_EmptyPath_ReturnsFalse_NoSpawn(string path)
    {
        var h = new Harness { VaultEnv = "v" };
        Assert.False(await h.Build().LaunchNoteAsync(path));
        Assert.Empty(h.Spawned);
    }

    [Theory]
    [InlineData("..\\escape.md")]
    [InlineData("../escape.md")]
    [InlineData("sub/../escape.md")]
    [InlineData("C:\\absolute.md")]
    [InlineData("/rooted.md")]
    [InlineData("has\nnewline.md")]
    [InlineData("has\rcr.md")]
    [InlineData("has\x00null.md")]
    public async Task LaunchNoteAsync_UnsafePath_Rejected(string path)
    {
        var h = new Harness { VaultEnv = "v" };
        Assert.False(await h.Build().LaunchNoteAsync(path));
        Assert.Empty(h.Spawned);
    }

    [Fact]
    public async Task LaunchVaultAsync_SpawnThrowsWin32_ReturnsFalse_DoesNotCrash()
    {
        var h = new Harness
        {
            VaultEnv = "v",
            SpawnThrows = new System.ComponentModel.Win32Exception("no handler registered"),
        };

        Assert.False(await h.Build().LaunchVaultAsync());
    }

    [Fact]
    public void IsSafeRelativePath_HappyPaths()
    {
        Assert.True(ObsidianLauncher.IsSafeRelativePath("Note.md"));
        Assert.True(ObsidianLauncher.IsSafeRelativePath("Inbox/Note.md"));
        Assert.True(ObsidianLauncher.IsSafeRelativePath("a/b/c/deep.md"));
        Assert.True(ObsidianLauncher.IsSafeRelativePath("folder with spaces/x.md"));
    }
}
