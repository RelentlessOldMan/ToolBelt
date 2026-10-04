using System;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class SingleInstanceTests
    {
        private static string UniqueName() => @"Local\ToolBelt.Windows.Tests." + Guid.NewGuid().ToString("N");

        public void FirstOwns_SecondDoesNot()
        {
            // A named mutex is owned per-thread and is reentrant, so the contended caller must run on a
            // different thread (mirroring the real cross-process single-instance scenario).
            string name = UniqueName();
            using var first = SingleInstance.TryAcquire(name);
            Check.True(first.IsOwned, "first acquires");

            bool secondOwned = true;
            var t = new Thread(() =>
            {
                using var second = SingleInstance.TryAcquire(name);
                secondOwned = second.IsOwned;
            });
            t.Start();
            t.Join();
            Check.False(secondOwned, "second (other thread) blocked while first holds");
        }

        public void ReleasedGate_CanBeReacquired()
        {
            string name = UniqueName();
            var first = SingleInstance.TryAcquire(name);
            Check.True(first.IsOwned);
            first.Dispose();
            Check.False(first.IsOwned, "ownership cleared on dispose");

            using var again = SingleInstance.TryAcquire(name);
            Check.True(again.IsOwned, "reacquire after release");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => SingleInstance.TryAcquire(null!));
            Check.Throws<ArgumentException>(() => SingleInstance.TryAcquire(""));
        }
    }
}
