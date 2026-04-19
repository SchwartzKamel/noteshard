using ObsidianQuickNoteWidget.Core.Cli;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests.Cli;

public class ObsidianCommandInvokerTests
{
    private static readonly string[] RunArgsNextTab = ["command", "id=workspace:next-tab"];
    private static readonly string[] CommandsOnly = ["commands"];
    private static readonly string[] CommandsWithFilter = ["commands", "filter=workspace:"];
    private static readonly string[] WorkspaceClosePrefix =
        ["workspace:close", "workspace:close-others", "workspace:close-window"];
    private static readonly string[] UnorderedList = ["zeta:one", "alpha:two", "beta:three"];

    private sealed class FakeCli : IObsidianCli
    {
        public bool Available { get; set; } = true;
        public bool IsAvailable => Available;

        public List<IReadOnlyList<string>> Invocations { get; } = new();
        public Func<IReadOnlyList<string>, CliResult> Responder { get; set; }
            = _ => new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero);

        public Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Invocations.Add(args.ToArray());
            return Task.FromResult(Responder(args));
        }

        public Task<string?> GetVaultRootAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> AppendDailyAsync(string text, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    [Fact]
    public async Task RunCommand_Success_StdoutExecuted_ReturnsSuccess()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, "Executed: workspace:next-tab\n", string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var r = await sut.RunCommandAsync("workspace:next-tab");

        Assert.True(r.Success);
        Assert.Null(r.ErrorMessage);
        Assert.Equal("Executed: workspace:next-tab", r.StdoutTrimmed);
        Assert.Equal(RunArgsNextTab, cli.Invocations.Single());
    }

    [Fact]
    public async Task RunCommand_EmptyStdout_StillSuccess()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var r = await sut.RunCommandAsync("plugin:silent-command");

        Assert.True(r.Success);
        Assert.Null(r.ErrorMessage);
        Assert.Equal(string.Empty, r.StdoutTrimmed);
    }

    [Fact]
    public async Task RunCommand_ErrorPrefix_ReturnsFailureWithMessage()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0,
                "Error: Command \"does:not-exist\" not found. Use \"commands\" to list available command IDs.\n",
                string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var r = await sut.RunCommandAsync("does:not-exist");

        Assert.False(r.Success);
        Assert.NotNull(r.ErrorMessage);
        Assert.Contains("does:not-exist", r.ErrorMessage);
        Assert.StartsWith("Error:", r.StdoutTrimmed);
    }

    [Fact]
    public async Task RunCommand_Timeout_ReturnsFailure()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(-1, string.Empty, "obsidian CLI timed out", TimeSpan.FromSeconds(10)),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var r = await sut.RunCommandAsync("workspace:next-tab");

        Assert.False(r.Success);
        Assert.NotNull(r.ErrorMessage);
        Assert.Contains("timed out", r.ErrorMessage);
    }

    [Fact]
    public async Task RunCommand_CliUnavailable_ReturnsFailureWithoutInvoking()
    {
        var cli = new FakeCli { Available = false };
        var sut = new ObsidianCommandInvoker(cli);

        var r = await sut.RunCommandAsync("workspace:next-tab");

        Assert.False(r.Success);
        Assert.Equal("Obsidian CLI not available", r.ErrorMessage);
        Assert.Empty(cli.Invocations);
    }

    [Fact]
    public async Task RunCommand_TrimsCommandId_BeforeInvocation()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, "Executed: workspace:next-tab", string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        await sut.RunCommandAsync("  workspace:next-tab  ");

        Assert.Equal(RunArgsNextTab, cli.Invocations.Single());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task RunCommand_EmptyOrWhitespace_Throws(string id)
    {
        var sut = new ObsidianCommandInvoker(new FakeCli());
        await Assert.ThrowsAsync<ArgumentException>(() => sut.RunCommandAsync(id));
    }

    [Fact]
    public async Task RunCommand_Null_Throws()
    {
        var sut = new ObsidianCommandInvoker(new FakeCli());
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunCommandAsync(null!));
    }

    [Theory]
    [InlineData("ws:bad\x00id")]
    [InlineData("ws:bad\nid")]
    [InlineData("ws:bad\rid")]
    [InlineData("ws:bad\x7fid")]
    [InlineData("ws:bad\x1bid")]
    public async Task RunCommand_ControlChars_Throws(string id)
    {
        var sut = new ObsidianCommandInvoker(new FakeCli());
        await Assert.ThrowsAsync<ArgumentException>(() => sut.RunCommandAsync(id));
    }

    [Fact]
    public async Task RunCommand_Overlong_Throws()
    {
        var sut = new ObsidianCommandInvoker(new FakeCli());
        var tooLong = new string('a', 257);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.RunCommandAsync(tooLong));
    }

    [Fact]
    public async Task RunCommand_At256Chars_Accepted()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, "Executed", string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);
        var id = new string('a', 256);

        var r = await sut.RunCommandAsync(id);

        Assert.True(r.Success);
    }

    [Fact]
    public async Task ListCommands_NoPrefix_InvokesBareCommandsVerb()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0,
                "workspace:close\nworkspace:close-others\nworkspace:close-window\n",
                string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var list = await sut.ListCommandsAsync();

        Assert.Equal(WorkspaceClosePrefix, list);
        Assert.Equal(CommandsOnly, cli.Invocations.Single());
    }

    [Fact]
    public async Task ListCommands_WithPrefix_PassesFilterArg()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, "workspace:close\n", string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        await sut.ListCommandsAsync("workspace:");

        Assert.Equal(CommandsWithFilter, cli.Invocations.Single());
    }

    [Fact]
    public async Task ListCommands_IgnoresBlankLines_PreservesOrder()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0,
                "\nzeta:one\n\n  \nalpha:two\n   beta:three   \n\n",
                string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var list = await sut.ListCommandsAsync();

        Assert.Equal(UnorderedList, list);
    }

    [Fact]
    public async Task ListCommands_CliUnavailable_ReturnsEmpty()
    {
        var cli = new FakeCli { Available = false };
        var sut = new ObsidianCommandInvoker(cli);

        var list = await sut.ListCommandsAsync();

        Assert.Empty(list);
        Assert.Empty(cli.Invocations);
    }

    [Fact]
    public async Task ListCommands_ErrorPrefixOnStdout_ReturnsEmpty()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(0, "Error: something broke\n", string.Empty, TimeSpan.Zero),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var list = await sut.ListCommandsAsync();

        Assert.Empty(list);
    }

    [Fact]
    public async Task ListCommands_Timeout_ReturnsEmpty()
    {
        var cli = new FakeCli
        {
            Responder = _ => new CliResult(-1, string.Empty, "obsidian CLI timed out", TimeSpan.FromSeconds(10)),
        };
        var sut = new ObsidianCommandInvoker(cli);

        var list = await sut.ListCommandsAsync();

        Assert.Empty(list);
    }

    [Fact]
    public void Ctor_NullCli_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ObsidianCommandInvoker(null!));
    }
}
