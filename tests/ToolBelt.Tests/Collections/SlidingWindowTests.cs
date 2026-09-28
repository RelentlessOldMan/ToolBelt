using System;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class SlidingWindowTests
    {
        public void BasicWindows()
        {
            var windows = SlidingWindow.Over(new[] { 1, 2, 3, 4 }, 2).ToList();
            Check.Equal(3, windows.Count);
            Check.True(windows[0].SequenceEqual(new[] { 1, 2 }));
            Check.True(windows[1].SequenceEqual(new[] { 2, 3 }));
            Check.True(windows[2].SequenceEqual(new[] { 3, 4 }));
        }

        public void WindowEqualToLength()
        {
            var windows = SlidingWindow.Over(new[] { 1, 2, 3 }, 3).ToList();
            Check.Equal(1, windows.Count);
            Check.True(windows[0].SequenceEqual(new[] { 1, 2, 3 }));
        }

        public void WindowLargerThanSource_Empty()
        {
            Check.Equal(0, SlidingWindow.Over(new[] { 1, 2 }, 3).Count());
            Check.Equal(0, SlidingWindow.Over(Array.Empty<int>(), 1).Count());
        }

        public void WindowOfOne()
        {
            var windows = SlidingWindow.Over(new[] { 1, 2, 3 }, 1).ToList();
            Check.Equal(3, windows.Count);
            Check.True(windows.All(w => w.Length == 1));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => SlidingWindow.Over<int>(null!, 1).ToList());
            Check.Throws<ArgumentOutOfRangeException>(() => SlidingWindow.Over(new[] { 1 }, 0).ToList());
        }

        // Property: window count == max(0, n - size + 1), and window i is source[i..i+size).
        public void Property_CountAndContents()
        {
            var rng = new Random(27);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(0, 30);
                int size = rng.Next(1, 10);
                var source = Enumerable.Range(0, n).ToArray();
                var windows = SlidingWindow.Over(source, size).ToList();

                Check.Equal(Math.Max(0, n - size + 1), windows.Count, $"trial {trial}: count");
                for (int i = 0; i < windows.Count; i++)
                    Check.True(windows[i].SequenceEqual(source.Skip(i).Take(size)), $"trial {trial}: window {i}");
            }
        }
    }
}
