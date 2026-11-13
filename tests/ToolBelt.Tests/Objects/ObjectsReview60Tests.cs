using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Regressions found in round 60 while probing BUGLIST 59-1: each failed before its fix.
    public sealed class ObjectsReview60Tests
    {
        public sealed class LazyNode
        {
            public LazyNode(int v)
            {
                V = v;
                Kids = Enumerable.Range(0, 1).Select(i => new LazyNode(v + 1));
            }

            public int V { get; }
            public IEnumerable<LazyNode> Kids { get; }
        }

        private static void Ends(Action action, string what)
        {
            Exception? error = null;
            var task = Task.Run(() => { try { action(); } catch (Exception e) { error = e; } });
            Check.True(task.Wait(TimeSpan.FromSeconds(20)), what + " did not finish");
            Check.True(error is null, what + " threw " + error);
        }

        public void Walkers_AStoredLazySequenceOfNewObjectsEnds()
        {
            // The sequence is stored once, so its property read is stable, but each pass makes new nodes.
            Ends(() => Check.True(DeepEquals.Equals(new LazyNode(1), new LazyNode(1))), "DeepEquals");
            Ends(() => Check.False(DeepEquals.Equals(new LazyNode(1), new LazyNode(2))), "DeepEquals unequal");
            Ends(() => Check.Equal(0, PropertyDiff.Compare(new LazyNode(1), new LazyNode(1)).Count), "PropertyDiff");
            Ends(() => Check.Equal(2, FlattenObject.Flatten(new LazyNode(1))["Kids[0].V"]), "Flatten");
        }

        public void Walkers_ALazySequenceOfStoredObjectsIsWalkedInFull()
        {
            // Both passes give the same objects: stored data, followed to any depth.
            var stored = Enumerable.Range(0, 30).Select(i => new List<int> { i }).ToArray();
            Func<IEnumerable<List<int>>> lazy = () => stored.Where(_ => true);
            var flat = FlattenObject.Flatten(new { Items = lazy() });
            Check.Equal(29, flat["Items[29][0]"]);
            Check.True(DeepEquals.Equals(new { Items = lazy() }, new { Items = lazy() }));
        }

        public void Unflatten_ALeafRootSaysWhyItCannotBeRebuilt()
        {
            var flat = FlattenObject.Flatten(5);
            Check.Equal(5, flat[""]);
            var ex = Check.Throws<FormatException>(() => UnflattenObject.Unflatten(flat));
            Check.True(ex.Message.Contains("root"), ex.Message);
        }

        public void FriendlyName_DeepGenericNestingThrowsInsteadOfOverflowing()
        {
            // About 2,400 levels overflowed a 1 MB stack, which no catch can stop: the process ended. (.NET Framework itself
            // overflows building a type about 8,000 levels deep.)
            Type deep = typeof(int);
            for (int i = 0; i < 5000; i++) deep = typeof(List<>).MakeGenericType(deep);
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { TypeUtils.FriendlyName(deep); } catch (Exception e) { error = e; }
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            Check.True(error is InsufficientExecutionStackException, error?.GetType().Name ?? "no exception");

            Type shallow = typeof(int);
            for (int i = 0; i < 200; i++) shallow = typeof(List<>).MakeGenericType(shallow);
            string name = TypeUtils.FriendlyName(shallow);
            Check.True(name.StartsWith("List<List<", StringComparison.Ordinal) && name.EndsWith("int" + new string('>', 200), StringComparison.Ordinal), name.Substring(0, 20));
        }
    }
}
