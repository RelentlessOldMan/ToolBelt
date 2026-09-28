using System;
using ToolBelt.Runtime;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Runtime
{
    public sealed class MemoryPressureTests
    {
        public void WorkingSetAndCountsArePlausible()
        {
            Check.True(MemoryPressure.WorkingSetBytes > 0, "working set should be positive");
            var counts = MemoryPressure.CollectionCounts();
            Check.Equal(GC.MaxGeneration + 1, counts.Length);
            foreach (int c in counts) Check.True(c >= 0);
        }

        public void SnapshotDeltaReflectsAllocation()
        {
            var before = MemoryPressure.Snapshot();
            // Allocate ~8 MB and keep it alive so it counts.
            var buffers = new byte[16][];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = new byte[512 * 1024];
            GC.KeepAlive(buffers);
            var after = MemoryPressure.Snapshot();

            var delta = after.Delta(before);
            Check.Equal(GC.MaxGeneration + 1, delta.CollectionCounts.Length);
#if !NETSTANDARD2_0
            Check.True(delta.TotalAllocatedBytes > 0, $"expected allocation to register, delta was {delta.TotalAllocatedBytes}");
#endif
        }

        public void NullDelta_Throws()
        {
            Check.Throws<ArgumentNullException>(() => MemoryPressure.Snapshot().Delta(null!));
        }
    }
}
