using Microsoft.Windows.Widgets.Providers;
using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.State;
using ObsidianQuickNoteWidget.Providers;

namespace ObsidianQuickNoteWidget.Tests.Bdd;

/// <summary>
/// Records every attempt the provider makes to push a card update to the
/// Widget Host. Scenarios assert on <see cref="Count"/> and
/// <see cref="CountFor"/> to verify background paths stay silent.
/// </summary>
internal sealed class RecordingUpdateSink : IWidgetUpdateSink
{
    private readonly List<WidgetUpdateRequestOptions> _submissions = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _submissions.Count; } }

    public int CountFor(string widgetId)
    {
        lock (_lock)
        {
            int n = 0;
            foreach (var s in _submissions)
                if (string.Equals(s.WidgetId, widgetId, StringComparison.Ordinal)) n++;
            return n;
        }
    }

    public IReadOnlyList<WidgetUpdateRequestOptions> Submissions
    {
        get { lock (_lock) return _submissions.ToArray(); }
    }

    public void Submit(WidgetUpdateRequestOptions options)
    {
        lock (_lock) _submissions.Add(options);
    }
}

internal sealed class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, WidgetState> _cache = new();

    public WidgetState Get(string widgetId)
        => _cache.TryGetValue(widgetId, out var s) ? s : new WidgetState { WidgetId = widgetId };

    public void Save(WidgetState state) => _cache[state.WidgetId] = state;
    public void Delete(string widgetId) => _cache.Remove(widgetId);
}

internal sealed class RecordingCli : IObsidianCli
{
    public bool Available { get; set; } = true;
    public bool IsAvailable => Available;

    public int ListFoldersCalls;
    public int ListFilesCalls;
    public int ListRecentsCalls;
    public int CreateNoteCalls;
    public int OpenNoteCalls;

    public IReadOnlyList<string> FoldersReply { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FilesReply { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecentsReply { get; set; } = Array.Empty<string>();

    public Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
        => Task.FromResult(new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero));

    public Task<string?> GetVaultRootAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref ListFoldersCalls);
        return Task.FromResult(FoldersReply);
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref ListFilesCalls);
        return Task.FromResult(FilesReply);
    }

    public Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ListRecentsCalls);
        return Task.FromResult(RecentsReply);
    }

    public Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
    {
        Interlocked.Increment(ref CreateNoteCalls);
        return Task.FromResult<string?>(null);
    }

    public Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
    {
        Interlocked.Increment(ref OpenNoteCalls);
        return Task.FromResult(true);
    }

    public Task<bool> AppendDailyAsync(string text, CancellationToken ct = default)
        => Task.FromResult(false);
}

internal sealed class RecordingLauncher : IObsidianLauncher
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

internal sealed class CapturingLog : ILog
{
    public List<string> Infos { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<Exception> Errors { get; } = new();

    public void Info(string message) => Infos.Add(message);
    public void Warn(string message) => Warnings.Add(message);
    public void Error(string message, Exception? ex = null)
    {
        if (ex != null) Errors.Add(ex);
    }
}
