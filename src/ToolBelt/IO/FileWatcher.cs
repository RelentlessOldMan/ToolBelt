// ToolBelt drop-in — also copy IO/DirectoryUtils.cs (tolerant, link-safe tree walk for snapshots).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ToolBelt.IO
{
    /// <summary>The net effect a <see cref="FileChange"/> describes.</summary>
    public enum FileChangeKind
    {
        Created,
        Changed,
        Deleted,
        Renamed,
    }

    /// <summary>One coalesced file change. <see cref="OldFullPath"/> is set for <see cref="FileChangeKind.Renamed"/>.</summary>
    public sealed class FileChange
    {
        public FileChange(FileChangeKind kind, string fullPath, string? oldFullPath = null)
        {
            Kind = kind;
            FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
            if (kind == FileChangeKind.Renamed && oldFullPath is null) throw new ArgumentException("A rename needs the old path.", nameof(oldFullPath));
            OldFullPath = kind == FileChangeKind.Renamed ? oldFullPath : null;
        }

        public FileChangeKind Kind { get; }
        public string FullPath { get; }
        public string? OldFullPath { get; }

        public override string ToString() => Kind == FileChangeKind.Renamed ? $"Renamed {OldFullPath} -> {FullPath}" : $"{Kind} {FullPath}";
    }

    /// <summary>
    /// Merges a stream of raw file-system events into the net change per file — the logic a debounced watcher needs,
    /// separated so it can be tested deterministically with an explicit clock. Per path: created then changed → Created;
    /// created then deleted → nothing; deleted then created → Changed (replaced); changed then deleted → Deleted; a rename
    /// chain a→b→c → Renamed a→c (Created c if a was new; back to a → Changed a); renamed then deleted → Deleted of the
    /// original name. <see cref="TakeQuiet"/> releases changes that have been quiet for the debounce interval. Not
    /// thread-safe: callers lock.
    /// </summary>
    public sealed class FileChangeCoalescer
    {
        private readonly Dictionary<string, Entry> _pending;
        private readonly StringComparer _comparer;
        private long _sequence;

        /// <param name="ignoreCase">Treat paths case-insensitively (default: true on Windows).</param>
        public FileChangeCoalescer(bool? ignoreCase = null)
        {
            _comparer = (ignoreCase ?? Path.DirectorySeparatorChar == '\\') ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _pending = new Dictionary<string, Entry>(_comparer);
        }

        /// <summary>The number of files with pending changes.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Records a raw event observed at <paramref name="now"/>.</summary>
        public void Add(FileChange change, DateTime now)
        {
            if (change is null) throw new ArgumentNullException(nameof(change));
            switch (change.Kind)
            {
                case FileChangeKind.Created: OnCreated(change.FullPath, now); break;
                case FileChangeKind.Changed: OnChanged(change.FullPath, now); break;
                case FileChangeKind.Deleted: OnDeleted(change.FullPath, now); break;
                case FileChangeKind.Renamed: OnRenamed(change.OldFullPath!, change.FullPath, now); break;
            }
        }

        /// <summary>
        /// Removes and returns the changes with no activity for at least <paramref name="quiet"/>, in first-seen order. A
        /// change for which <paramref name="isReady"/> returns false stays pending (its quiet period restarts) — used to
        /// hold back files that are still being written.
        /// </summary>
        public IReadOnlyList<FileChange> TakeQuiet(DateTime now, TimeSpan quiet, Func<FileChange, bool>? isReady = null)
        {
            var due = new List<KeyValuePair<string, Entry>>();
            foreach (var kv in _pending)
                if (now - kv.Value.LastActivity >= quiet) due.Add(kv);
            due.Sort((x, y) => x.Value.FirstSeen != y.Value.FirstSeen ? x.Value.FirstSeen.CompareTo(y.Value.FirstSeen) : x.Value.Order.CompareTo(y.Value.Order));
            var result = new List<FileChange>(due.Count);
            foreach (var kv in due)
            {
                FileChange change = kv.Value.ToChange(kv.Key);
                if (isReady != null && !isReady(change)) { kv.Value.LastActivity = now; continue; }
                _pending.Remove(kv.Key);
                result.Add(change);
            }
            return result;
        }

        /// <summary>Removes and returns everything pending, in first-seen order.</summary>
        public IReadOnlyList<FileChange> TakeAll() => TakeQuiet(DateTime.MaxValue, TimeSpan.Zero);

        // ---------- the merge rules ----------

        private void OnCreated(string path, DateTime now)
        {
            if (_pending.TryGetValue(path, out Entry? e))
            {
                if (e.Kind == FileChangeKind.Deleted) e.Kind = FileChangeKind.Changed; // deleted and recreated: replaced
                e.LastActivity = now;
            }
            else Put(path, new Entry(FileChangeKind.Created, null, now, ++_sequence));
        }

        private void OnChanged(string path, DateTime now)
        {
            if (_pending.TryGetValue(path, out Entry? e))
            {
                if (e.Kind == FileChangeKind.Deleted) e.Kind = FileChangeKind.Changed;
                e.LastActivity = now;
            }
            else Put(path, new Entry(FileChangeKind.Changed, null, now, ++_sequence));
        }

        private void OnDeleted(string path, DateTime now)
        {
            if (!_pending.TryGetValue(path, out Entry? e))
            {
                Put(path, new Entry(FileChangeKind.Deleted, null, now, ++_sequence));
                return;
            }
            switch (e.Kind)
            {
                case FileChangeKind.Created:
                    _pending.Remove(path); // came and went: nothing to report
                    break;
                case FileChangeKind.Renamed:
                    // The original file (known by its old name) is gone; report that name as deleted.
                    _pending.Remove(path);
                    string old = e.OldPath!;
                    if (_pending.TryGetValue(old, out Entry? atOld) && atOld.Kind == FileChangeKind.Created)
                    { atOld.Kind = FileChangeKind.Changed; atOld.LastActivity = now; } // a new file took the old name
                    else Put(old, new Entry(FileChangeKind.Deleted, null, now, e.Order) { FirstSeen = e.FirstSeen });
                    break;
                default:
                    e.Kind = FileChangeKind.Deleted;
                    e.LastActivity = now;
                    break;
            }
        }

        private void OnRenamed(string oldPath, string newPath, DateTime now)
        {
            Entry result;
            if (_pending.TryGetValue(oldPath, out Entry? prior))
            {
                _pending.Remove(oldPath);
                switch (prior.Kind)
                {
                    case FileChangeKind.Created:
                        result = new Entry(FileChangeKind.Created, null, now, prior.Order) { FirstSeen = prior.FirstSeen };
                        break;
                    case FileChangeKind.Renamed when _comparer.Equals(prior.OldPath, newPath):
                        result = new Entry(FileChangeKind.Changed, null, now, prior.Order) { FirstSeen = prior.FirstSeen }; // renamed back
                        break;
                    case FileChangeKind.Renamed:
                        result = new Entry(FileChangeKind.Renamed, prior.OldPath, now, prior.Order) { FirstSeen = prior.FirstSeen };
                        break;
                    default:
                        result = new Entry(FileChangeKind.Renamed, oldPath, now, prior.Order) { FirstSeen = prior.FirstSeen };
                        break;
                }
            }
            else result = new Entry(FileChangeKind.Renamed, oldPath, now, ++_sequence);

            if (_pending.TryGetValue(newPath, out Entry? atNew))
            {
                // Something already pending at the destination is superseded by the file renamed onto it.
                if (atNew.Kind == FileChangeKind.Deleted && result.Kind == FileChangeKind.Created) result.Kind = FileChangeKind.Changed;
                if (atNew.FirstSeen < result.FirstSeen) result.FirstSeen = atNew.FirstSeen;
                _pending.Remove(newPath);
            }
            Put(newPath, result);
        }

        private void Put(string path, Entry e) => _pending[path] = e;

        private sealed class Entry
        {
            public Entry(FileChangeKind kind, string? oldPath, DateTime now, long order)
            {
                Kind = kind;
                OldPath = oldPath;
                FirstSeen = now;
                LastActivity = now;
                Order = order;
            }

            public FileChangeKind Kind;
            public string? OldPath;
            public DateTime FirstSeen;
            public DateTime LastActivity;
            public readonly long Order;

            public FileChange ToChange(string path) => new FileChange(Kind, path, OldPath);
        }
    }

    /// <summary>A point-in-time listing of files (size and last-write time) under a directory, and the diff between two.</summary>
    public sealed class DirectorySnapshot
    {
        private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _files;

        private DirectorySnapshot(Dictionary<string, (long, DateTime)> files) => _files = files;

        public int Count => _files.Count;

        /// <summary>
        /// Lists the files under <paramref name="directory"/> matching <paramref name="pattern"/>, optionally recursively.
        /// Unreadable parts are skipped; links are not followed.
        /// </summary>
        public static DirectorySnapshot Take(string directory, string pattern = "*", bool recursive = true)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var files = new Dictionary<string, (long, DateTime)>(comparer);
            IEnumerable<string> paths;
            if (recursive) paths = DirectoryUtils.EnumerateFiles(directory, pattern);
            else
            {
                try { paths = Directory.GetFiles(directory, pattern); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { paths = Array.Empty<string>(); }
            }
            foreach (string p in paths)
            {
                try
                {
                    var fi = new FileInfo(p);
                    files[fi.FullName] = (fi.Length, fi.LastWriteTimeUtc);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
            return new DirectorySnapshot(files);
        }

        /// <summary>Created, Changed (size or last-write time differ) and Deleted files going from <paramref name="before"/> to <paramref name="after"/>.</summary>
        public static IReadOnlyList<FileChange> Diff(DirectorySnapshot before, DirectorySnapshot after)
        {
            if (before is null) throw new ArgumentNullException(nameof(before));
            if (after is null) throw new ArgumentNullException(nameof(after));
            var changes = new List<FileChange>();
            foreach (var kv in after._files)
            {
                if (!before._files.TryGetValue(kv.Key, out var old)) changes.Add(new FileChange(FileChangeKind.Created, kv.Key));
                else if (old != kv.Value) changes.Add(new FileChange(FileChangeKind.Changed, kv.Key));
            }
            foreach (string path in before._files.Keys)
                if (!after._files.ContainsKey(path)) changes.Add(new FileChange(FileChangeKind.Deleted, path));
            return changes;
        }

        internal bool ContainsUnder(string directory)
        {
            string prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var cmp = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return _files.Keys.Any(k => k.StartsWith(prefix, cmp));
        }

        internal void Record(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists) _files[fi.FullName] = (fi.Length, fi.LastWriteTimeUtc);
                else _files.Remove(fi.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        internal void Forget(string path) => _files.Remove(Path.GetFullPath(path));
    }

    /// <summary>Options for <see cref="FileWatcher"/>.</summary>
    public sealed class FileWatcherOptions
    {
        /// <summary>File-name pattern, e.g. "*.csv".</summary>
        public string Filter { get; set; } = "*";
        public bool IncludeSubdirectories { get; set; } = true;

        /// <summary>A file's changes are reported once it has been quiet this long (default 250 ms).</summary>
        public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// When set, a created or changed file is held back until its size and last-write time have stayed the same for
        /// this long — so a large file still being copied in is reported once, when complete.
        /// </summary>
        public TimeSpan? StableFor { get; set; }

        /// <summary>The OS watcher's buffer (bytes, 4–64 KB). On overflow the watcher rescans instead of losing events.</summary>
        public int InternalBufferSize { get; set; } = 64 * 1024;
    }

    /// <summary>A batch of coalesced changes.</summary>
    public sealed class FileChangesEventArgs : EventArgs
    {
        public FileChangesEventArgs(IReadOnlyList<FileChange> changes) => Changes = changes;
        public IReadOnlyList<FileChange> Changes { get; }
    }

    /// <summary>
    /// A debounced, self-healing wrapper over <see cref="FileSystemWatcher"/>. The raw watcher raises several events per
    /// save, reports files mid-write, and silently drops events when its buffer overflows; this one raises
    /// <see cref="Changed"/> with one coalesced <see cref="FileChange"/> per file once that file has been quiet for
    /// <see cref="FileWatcherOptions.Debounce"/> (and, optionally, stable for <see cref="FileWatcherOptions.StableFor"/>).
    /// <para>
    /// It keeps a snapshot of the watched files and rescans against it when the buffer overflows, when it is restarted
    /// after <see cref="Stop"/> (so changes made while stopped are still reported), and when a whole directory is created,
    /// deleted or moved (the OS reports only the directory, not the files inside). Files only — directories themselves
    /// are not reported. <see cref="Changed"/> and <see cref="Error"/> are raised on a thread-pool thread, never
    /// concurrently with themselves; marshal to a UI thread yourself. An exception thrown by a handler is passed to
    /// <see cref="Error"/> and watching continues.
    /// </para>
    /// </summary>
    public sealed class FileWatcher : IDisposable
    {
        private readonly FileWatcherOptions _options;
        private readonly FileSystemWatcher _watcher;
        private readonly FileChangeCoalescer _coalescer = new FileChangeCoalescer();
        private readonly Dictionary<string, (long Length, DateTime LastWrite, DateTime Since)> _probes =
            new Dictionary<string, (long, DateTime, DateTime)>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly object _gate = new object();
        private readonly object _raiseGate = new object(); // serialises Changed between the timer and Flush()
        private readonly Timer _timer;
        private readonly TimeSpan _tick;
        private DirectorySnapshot? _snapshot;
        private int _flushing;
        private volatile bool _rescanRequested;
        private bool _disposed;

        public FileWatcher(string directory, FileWatcherOptions? options = null)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            string full = Path.GetFullPath(directory);
            if (!System.IO.Directory.Exists(full)) throw new DirectoryNotFoundException($"Directory '{directory}' does not exist.");
            _options = options ?? new FileWatcherOptions();
            if (_options.Debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "Debounce must be non-negative.");
            if (_options.StableFor is TimeSpan s && s <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "StableFor must be positive.");
            Directory = full;

            _watcher = new FileSystemWatcher(full, _options.Filter)
            {
                IncludeSubdirectories = _options.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = Math.Max(4096, Math.Min(64 * 1024, _options.InternalBufferSize)),
            };
            _watcher.Created += (_, e) => OnRaw(FileChangeKind.Created, e.FullPath, null);
            _watcher.Changed += (_, e) => OnRaw(FileChangeKind.Changed, e.FullPath, null);
            _watcher.Deleted += (_, e) => OnRaw(FileChangeKind.Deleted, e.FullPath, null);
            _watcher.Renamed += (_, e) => OnRaw(FileChangeKind.Renamed, e.FullPath, e.OldFullPath);
            _watcher.Error += (_, e) =>
            {
                if (e.GetException() is InternalBufferOverflowException) _rescanRequested = true; // recover rather than lose events
                else Error?.Invoke(this, e);
            };

            long ms = (long)Math.Max(10, Math.Min(250, Math.Min(_options.Debounce.TotalMilliseconds,
                (_options.StableFor ?? _options.Debounce).TotalMilliseconds) / 2));
            _tick = TimeSpan.FromMilliseconds(ms);
            _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>The watched directory (full path).</summary>
        public string Directory { get; }

        public bool IsRunning { get; private set; }

        /// <summary>Raised with each batch of coalesced changes.</summary>
        public event EventHandler<FileChangesEventArgs>? Changed;

        /// <summary>Raised for watcher errors other than buffer overflow (which triggers a rescan) and for handler exceptions.</summary>
        public event EventHandler<ErrorEventArgs>? Error;

        /// <summary>Starts watching. On a restart, changes made while stopped are found by a rescan and reported.</summary>
        public void Start()
        {
            ThrowIfDisposed();
            if (IsRunning) return;
            bool restart;
            lock (_gate) restart = _snapshot != null;
            if (!restart)
            {
                var snap = Snapshot();
                lock (_gate) _snapshot = snap;
            }
            _watcher.EnableRaisingEvents = true;
            IsRunning = true;
            if (restart) _rescanRequested = true;
            _timer.Change(_tick, _tick);
        }

        /// <summary>Stops watching. Pending changes are kept and reported after <see cref="Start"/> or <see cref="Flush"/>.</summary>
        public void Stop()
        {
            ThrowIfDisposed();
            if (!IsRunning) return;
            _watcher.EnableRaisingEvents = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            IsRunning = false;
        }

        /// <summary>
        /// Compares the directory with the last snapshot and queues the differences (reported after the debounce like any
        /// other change). Runs automatically after a buffer overflow or a restart; call it to resynchronise on demand.
        /// Returns the number of differences found.
        /// </summary>
        public int Rescan()
        {
            ThrowIfDisposed();
            DirectorySnapshot fresh = Snapshot();
            lock (_gate)
            {
                IReadOnlyList<FileChange> diff = _snapshot is null ? Array.Empty<FileChange>() : DirectorySnapshot.Diff(_snapshot, fresh);
                DateTime now = DateTime.UtcNow;
                foreach (FileChange c in diff) _coalescer.Add(c, now);
                _snapshot = fresh;
                return diff.Count;
            }
        }

        /// <summary>Reports everything pending now, ignoring the debounce and stability wait. Returns what was reported.</summary>
        public IReadOnlyList<FileChange> Flush()
        {
            ThrowIfDisposed();
            IReadOnlyList<FileChange> batch;
            lock (_gate) batch = Take(_coalescer.TakeAll());
            Raise(batch);
            return batch;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            using (var done = new ManualResetEvent(false))
            {
                if (_timer.Dispose(done)) done.WaitOne(TimeSpan.FromSeconds(5)); // let an in-flight tick finish
            }
            IsRunning = false;
        }

        // ---------- internals ----------

        private void OnRaw(FileChangeKind kind, string path, string? oldPath)
        {
            try
            {
                // Directory-level events: the files inside were not reported individually, so resynchronise.
                if (kind != FileChangeKind.Deleted && System.IO.Directory.Exists(path)) { if (kind != FileChangeKind.Changed) _rescanRequested = true; return; }
                lock (_gate)
                {
                    if (kind == FileChangeKind.Deleted && _snapshot != null && _snapshot.ContainsUnder(path)) { _rescanRequested = true; return; }
                    if (kind == FileChangeKind.Renamed && oldPath != null && _snapshot != null && _snapshot.ContainsUnder(oldPath)) { _rescanRequested = true; return; }
                    _coalescer.Add(new FileChange(kind, path, oldPath), DateTime.UtcNow);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex));
            }
        }

        private void OnTick()
        {
            if (Interlocked.Exchange(ref _flushing, 1) == 1) return; // never overlap ticks
            try
            {
                if (_rescanRequested && !_disposed)
                {
                    _rescanRequested = false;
                    Rescan();
                }
                IReadOnlyList<FileChange> batch;
                lock (_gate)
                {
                    DateTime now = DateTime.UtcNow;
                    batch = Take(_coalescer.TakeQuiet(now, _options.Debounce, _options.StableFor is null ? null : c => IsStable(c, now)));
                }
                Raise(batch);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                Error?.Invoke(this, new ErrorEventArgs(ex));
            }
            finally { Volatile.Write(ref _flushing, 0); }
        }

        // Holds a created/changed file back until its size and last-write time have been unchanged for StableFor.
        private bool IsStable(FileChange change, DateTime now)
        {
            FileInfo fi;
            try { fi = new FileInfo(change.FullPath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { _probes.Remove(change.FullPath); return true; }
            if (change.Kind == FileChangeKind.Deleted || !fi.Exists) { _probes.Remove(change.FullPath); return true; }
            var current = (fi.Length, fi.LastWriteTimeUtc);
            if (_probes.TryGetValue(change.FullPath, out var probe) && probe.Length == current.Length && probe.LastWrite == current.LastWriteTimeUtc)
            {
                if (now - probe.Since < _options.StableFor!.Value) return false;
                _probes.Remove(change.FullPath);
                return true;
            }
            _probes[change.FullPath] = (current.Length, current.LastWriteTimeUtc, now);
            return false;
        }

        // Keeps the snapshot current with what is being reported, so a later rescan does not repeat it.
        private IReadOnlyList<FileChange> Take(IReadOnlyList<FileChange> batch)
        {
            if (_snapshot != null)
                foreach (FileChange c in batch)
                {
                    if (c.OldFullPath != null) _snapshot.Forget(c.OldFullPath);
                    _snapshot.Record(c.FullPath);
                }
            return batch;
        }

        private void Raise(IReadOnlyList<FileChange> batch)
        {
            if (batch.Count == 0) return;
            lock (_raiseGate) // reentrant, so a handler that calls Flush() does not deadlock
            {
                try { Changed?.Invoke(this, new FileChangesEventArgs(batch)); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { Error?.Invoke(this, new ErrorEventArgs(ex)); }
            }
        }

        private DirectorySnapshot Snapshot() => DirectorySnapshot.Take(Directory, _options.Filter, _options.IncludeSubdirectories);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileWatcher));
        }
    }
}
