using ObsidianQuickNoteWidget.Core.Concurrency;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class PerWidgetGateTests
{
    [Fact]
    public async Task SameKey_Serializes()
    {
        var gate = new AsyncKeyedLock<string>();
        int concurrent = 0;
        int maxObserved = 0;

        async Task Work()
        {
            var now = Interlocked.Increment(ref concurrent);
            maxObserved = Math.Max(maxObserved, now);
            await Task.Delay(40).ConfigureAwait(false);
            Interlocked.Decrement(ref concurrent);
        }

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => gate.WithLockAsync("a", Work)))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxObserved);
    }

    private static readonly string[] DistinctKeys = { "a", "b", "c", "d" };

    [Fact]
    public async Task DifferentKeys_RunConcurrently()
    {
        var gate = new AsyncKeyedLock<string>();
        using var barrier = new Barrier(participantCount: 4);

        async Task Work()
        {
            // Ensure all four have acquired their per-key gate before releasing.
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            await Task.Yield();
        }

        var tasks = DistinctKeys
            .Select(k => Task.Run(() => gate.WithLockAsync(k, Work)))
            .ToArray();

        var all = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(all, Task.Delay(3000));
        Assert.Same(all, completed);
    }

    [Fact]
    public async Task EntryIsRemoved_WhenLastHolderReleases()
    {
        var gate = new AsyncKeyedLock<string>();

        await gate.WithLockAsync("a", () => Task.CompletedTask);
        await gate.WithLockAsync("b", () => Task.CompletedTask);

        Assert.Equal(0, gate.TrackedKeyCount);
    }

    [Fact]
    public async Task EntryLingers_WhileHolderActive()
    {
        var gate = new AsyncKeyedLock<string>();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var holder = Task.Run(() => gate.WithLockAsync("a", async () =>
        {
            started.SetResult();
            await release.Task.ConfigureAwait(false);
        }));

        await started.Task;
        Assert.Equal(1, gate.TrackedKeyCount);

        release.SetResult();
        await holder;

        Assert.Equal(0, gate.TrackedKeyCount);
    }

    [Fact]
    public async Task GenericOverload_ReturnsValue()
    {
        var gate = new AsyncKeyedLock<string>();
        var result = await gate.WithLockAsync("a", () => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Exception_InWorkReleasesLock()
    {
        var gate = new AsyncKeyedLock<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.WithLockAsync("a", () => throw new InvalidOperationException("boom")));

        // Subsequent acquisition must succeed.
        var reentered = false;
        await gate.WithLockAsync("a", () => { reentered = true; return Task.CompletedTask; });
        Assert.True(reentered);
        Assert.Equal(0, gate.TrackedKeyCount);
    }

    [Fact]
    public async Task Cancellation_DoesNotLeakRefCount()
    {
        var gate = new AsyncKeyedLock<string>();
        var holding = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var holder = Task.Run(() => gate.WithLockAsync("a", async () =>
        {
            holding.SetResult();
            await release.Task.ConfigureAwait(false);
        }));
        await holding.Task;

        using var cts = new CancellationTokenSource();
        var waiter = gate.WithLockAsync("a", () => Task.CompletedTask, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        release.SetResult();
        await holder;
        Assert.Equal(0, gate.TrackedKeyCount);
    }
}
