// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using DiagnosticsProcess = System.Diagnostics.Process; // avoid clash with the ToolBelt.Process namespace

namespace ToolBelt.Runtime
{
    /// <summary>A captured point-in-time view of process memory and GC activity.</summary>
    public sealed class MemorySnapshot
    {
        internal MemorySnapshot(long workingSetBytes, long totalAllocatedBytes, int[] collectionCounts)
        {
            WorkingSetBytes = workingSetBytes;
            TotalAllocatedBytes = totalAllocatedBytes;
            CollectionCounts = collectionCounts;
        }

        public long WorkingSetBytes { get; }
        /// <summary>Total bytes allocated by the process so far (monotonic). Best-effort on the legacy target.</summary>
        public long TotalAllocatedBytes { get; }
        /// <summary>Number of collections per GC generation, indexed by generation.</summary>
        public int[] CollectionCounts { get; }

        /// <summary>The change from an earlier snapshot to this one.</summary>
        public MemorySnapshot Delta(MemorySnapshot earlier)
        {
            if (earlier is null) throw new ArgumentNullException(nameof(earlier));
            int n = Math.Max(CollectionCounts.Length, earlier.CollectionCounts.Length);
            var counts = new int[n];
            for (int i = 0; i < n; i++)
                counts[i] = At(CollectionCounts, i) - At(earlier.CollectionCounts, i);
            return new MemorySnapshot(
                WorkingSetBytes - earlier.WorkingSetBytes,
                TotalAllocatedBytes - earlier.TotalAllocatedBytes,
                counts);

            static int At(int[] a, int i) => i < a.Length ? a[i] : 0;
        }
    }

    /// <summary>
    /// Reads current memory pressure — working set, total allocated bytes, and GC collection counts by
    /// generation — plus a delta helper for measuring an operation's memory cost. What a benchmark or
    /// diagnostics dump needs for its allocation column.
    /// </summary>
    public static class MemoryPressure
    {
        /// <summary>The process working set (physical memory in use), in bytes.</summary>
        public static long WorkingSetBytes
        {
            get { using var p = DiagnosticsProcess.GetCurrentProcess(); return p.WorkingSet64; }
        }

        /// <summary>Total bytes allocated by the process so far.</summary>
        public static long TotalAllocatedBytes =>
#if NETSTANDARD2_0
            GC.GetTotalMemory(forceFullCollection: false); // approximate: current managed heap size
#else
            GC.GetTotalAllocatedBytes(precise: false);
#endif

        /// <summary>Collection counts indexed by generation (0..GC.MaxGeneration).</summary>
        public static int[] CollectionCounts()
        {
            var counts = new int[GC.MaxGeneration + 1];
            for (int gen = 0; gen <= GC.MaxGeneration; gen++) counts[gen] = GC.CollectionCount(gen);
            return counts;
        }

        /// <summary>Captures the current memory pressure.</summary>
        public static MemorySnapshot Snapshot()
            => new MemorySnapshot(WorkingSetBytes, TotalAllocatedBytes, CollectionCounts());
    }
}
