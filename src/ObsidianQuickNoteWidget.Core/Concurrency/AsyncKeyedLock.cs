using System.Collections.Concurrent;

namespace ObsidianQuickNoteWidget.Core.Concurrency;

/// <summary>
/// Async mutual-exclusion keyed by <typeparamref name="TKey"/>. Callers with the
/// same key are serialized; callers with different keys run concurrently.
/// Entries are reference-counted so the underlying <see cref="SemaphoreSlim"/>
/// is disposed once the last in-flight holder releases it.
/// </summary>
public sealed class AsyncKeyedLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries;

    public AsyncKeyedLock() : this(EqualityComparer<TKey>.Default) { }

    public AsyncKeyedLock(IEqualityComparer<TKey> comparer)
    {
        _entries = new ConcurrentDictionary<TKey, Entry>(comparer);
    }

    /// <summary>Number of currently-tracked keys; primarily for tests.</summary>
    internal int TrackedKeyCount => _entries.Count;

    public async Task WithLockAsync(TKey key, Func<Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var entry = Acquire(key);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, acquiredSemaphore: false);
            throw;
        }

        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            Release(key, entry, acquiredSemaphore: true);
        }
    }

    public async Task<TResult> WithLockAsync<TResult>(TKey key, Func<Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var entry = Acquire(key);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, acquiredSemaphore: false);
            throw;
        }

        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            Release(key, entry, acquiredSemaphore: true);
        }
    }

    private Entry Acquire(TKey key)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                lock (existing.SyncRoot)
                {
                    if (!existing.Disposed)
                    {
                        existing.RefCount++;
                        return existing;
                    }
                }
                // Lost the race: another caller disposed this entry. Retry.
                continue;
            }

            var fresh = new Entry();
            fresh.RefCount = 1;
            if (_entries.TryAdd(key, fresh)) return fresh;
            fresh.Semaphore.Dispose();
        }
    }

    private void Release(TKey key, Entry entry, bool acquiredSemaphore)
    {
        bool dispose = false;
        lock (entry.SyncRoot)
        {
            if (acquiredSemaphore) entry.Semaphore.Release();
            if (--entry.RefCount == 0)
            {
                entry.Disposed = true;
                dispose = true;
            }
        }
        if (dispose)
        {
            _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        public readonly object SyncRoot = new();
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
        public bool Disposed;
    }
}
