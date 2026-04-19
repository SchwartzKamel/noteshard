using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.State;
using ObsidianQuickNoteWidget.Providers;
using Xunit;

namespace ObsidianQuickNoteWidget.Tests;

/// <summary>
/// v7: verifies that <c>openVault</c> and <c>openRecent</c> verbs go through
/// <see cref="IObsidianLauncher"/> (URI scheme) rather than the CLI — the CLI
/// requires Obsidian to already be running, the URI scheme does not.
/// </summary>
public class ObsidianWidgetProviderLaunchTests
{
    private sealed class RecordingLauncher : IObsidianLauncher
    {
        public int VaultCalls;
        public List<string> NoteCalls { get; } = new();
        public string? VaultName { get; set; } = "test-vault";

        public Task<bool> LaunchVaultAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref VaultCalls);
            return Task.FromResult(true);
        }

        public Task<bool> LaunchNoteAsync(string vaultRelativePath, CancellationToken ct = default)
        {
            NoteCalls.Add(vaultRelativePath);
            return Task.FromResult(true);
        }

        public string? ResolveVaultName() => VaultName;
    }

    private sealed class RecordingCli : IObsidianCli
    {
        public bool IsAvailable => true;
        public int OpenNoteCalls;
        public Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
            => Task.FromResult(new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero));
        public Task<string?> GetVaultRootAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
        {
            Interlocked.Increment(ref OpenNoteCalls);
            return Task.FromResult(true);
        }
        public Task<bool> AppendDailyAsync(string text, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class InMemoryStore : IStateStore
    {
        private readonly Dictionary<string, WidgetState> _cache = new();
        public WidgetState Get(string id) =>
            _cache.TryGetValue(id, out var s) ? s : new WidgetState { WidgetId = id };
        public void Save(WidgetState s) => _cache[s.WidgetId] = s;
        public void Delete(string id) => _cache.Remove(id);
    }

    private sealed class CapturingLog : ILog
    {
        public List<string> Warnings { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? ex = null) { }
    }

    [Fact]
    public async Task OpenVault_InvokesLauncher_NotCli()
    {
        var cli = new RecordingCli();
        var launcher = new RecordingLauncher();
        var store = new InMemoryStore();
        var provider = new ObsidianWidgetProvider(NullLog.Instance, store, cli, null, launcher);

        const string id = "w-vault";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.InvokeVerbForTest(id, "openVault", data: null);

        Assert.Equal(1, launcher.VaultCalls);
        Assert.Equal(0, cli.OpenNoteCalls);
    }

    private static readonly string[] ExpectedNoteCall = { "Inbox/Hello.md" };

    [Fact]
    public async Task OpenRecent_WithPath_InvokesLauncher_NotCli()
    {
        var cli = new RecordingCli();
        var launcher = new RecordingLauncher();
        var store = new InMemoryStore();
        var provider = new ObsidianWidgetProvider(NullLog.Instance, store, cli, null, launcher);

        const string id = "w-recent";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.InvokeVerbForTest(id, "openRecent", data: "{\"path\":\"Inbox/Hello.md\"}");

        Assert.Equal(ExpectedNoteCall, launcher.NoteCalls);
        Assert.Equal(0, cli.OpenNoteCalls);
    }

    [Theory]
    [InlineData("{\"path\":\"\"}")]
    [InlineData("{\"path\":\"   \"}")]
    [InlineData("{}")]
    public async Task OpenRecent_EmptyOrMissingPath_NoLauncherCall_WarnLogged(string data)
    {
        var cli = new RecordingCli();
        var launcher = new RecordingLauncher();
        var store = new InMemoryStore();
        var log = new CapturingLog();
        var provider = new ObsidianWidgetProvider(log, store, cli, null, launcher);

        const string id = "w-empty";
        provider.RegisterActiveForTest(id, WidgetIdentifiers.RecentNotesWidgetId);

        await provider.InvokeVerbForTest(id, "openRecent", data);

        Assert.Empty(launcher.NoteCalls);
        Assert.Equal(0, cli.OpenNoteCalls);
    }
}
