using System;
using System.Linq;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class MedianFilterTests
    {
        public void RemovesSingleSpike()
        {
            // A lone spike in an otherwise flat signal is removed by a width-3 median.
            var input = new double[] { 1, 1, 1, 100, 1, 1, 1 };
            var output = MedianFilter.Apply(input, 3);
            Check.True(output.All(v => v == 1), "spike should be removed");
        }

        public void PreservesEdges()
        {
            // A step edge is preserved (unlike a mean filter which would blur it).
            var input = new double[] { 0, 0, 0, 10, 10, 10 };
            var output = MedianFilter.Apply(input, 3);
            Check.True(output.SequenceEqual(new double[] { 0, 0, 0, 10, 10, 10 }));
        }

        public void WindowOneIsIdentity()
        {
            var input = new double[] { 3, 1, 4, 1, 5 };
            Check.True(MedianFilter.Apply(input, 1).SequenceEqual(input));
        }

        public void ConstantUnchanged()
        {
            var input = new double[] { 7, 7, 7, 7 };
            Check.True(MedianFilter.Apply(input, 3).All(v => v == 7));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => MedianFilter.Apply(new double[] { 1 }, 0));
            Check.Throws<ArgumentException>(() => MedianFilter.Apply(new double[] { 1 }, 2)); // even
            Check.Throws<ArgumentNullException>(() => MedianFilter.Apply(null!, 3));
        }
    }
}
