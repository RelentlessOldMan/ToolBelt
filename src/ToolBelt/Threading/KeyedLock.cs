// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Provides an independent async mutual-exclusion lock per key, so operations on different keys run
    /// concurrently while operations on the same key serialize. Per-key semaphores are reference-counted
    /// and removed once no one holds or awaits them, so the memory footprint tracks live keys rather than
    /// all keys ever seen. Acquire with <c>using (await keyed.LockAsync(key)) { ... }</c>. Not reentrant.
    /// </summary>
    public sealed class KeyedLock<TKey>
        where TKey : notnull
    {
        private sealed class Entry
        {
            public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            public int RefCount;
        }

        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();
        private readonly object _gate = new object();

        /// <summary>Number of keys currently held or awaited. Intended for diagnostics/tests.</summary>
        public int ActiveKeyCount
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>Waits for exclusive access to <paramref name="key"/>; dispose the handle to release.</summary>
        public async Task<IDisposable> LockAsync(TKey key, CancellationToken cancellationToken = default)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));

            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry();
                    _entries[key] = entry;
                }
                entry.RefCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Never acquired — undo our reservation so the entry can be reclaimed.
                Unreserve(key, entry, acquired: false);
                throw;
            }

            return new Releaser(this, key, entry);
        }

        private void Release(TKey key, Entry entry) => Unreserve(key, entry, acquired: true);

        private void Unreserve(TKey key, Entry entry, bool acquired)
        {
            if (acquired)
                entry.Semaphore.Release();

            lock (_gate)
            {
                if (--entry.RefCount == 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly KeyedLock<TKey> _owner;
            private readonly TKey _key;
            private readonly Entry _entry;
            private bool _released;

            public Releaser(KeyedLock<TKey> owner, TKey key, Entry entry)
            {
                _owner = owner;
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                if (_released)
                    return;
                _released = true;
                _owner.Release(_key, _entry);
            }
        }
    }
}
