using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Concurrency;

/// <summary>
/// Helpers for running fire-and-forget work without swallowing exceptions. The
/// returned <see cref="Task"/> is always non-faulting: callers can safely
/// discard it, but tests can <c>await</c> it to observe completion.
/// </summary>
public static class AsyncSafe
{
    /// <summary>
    /// Runs <paramref name="work"/>, logging and surfacing any exception via
    /// <paramref name="onError"/>. Never throws.
    /// </summary>
    public static async Task RunAsync(
        Func<Task> work,
        ILog log,
        string context,
        Func<Exception, Task>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try { log.Error($"{context} failed", ex); } catch { /* logger must never re-throw */ }
            if (onError is not null)
            {
                try { await onError(ex).ConfigureAwait(false); } catch (Exception inner) { try { log.Error($"{context} onError handler failed", inner); } catch { } }
            }
        }
    }
}
