using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class MultiDictionaryTests
    {
        public void AddAndLookup_PreservesOrderAndDuplicates()
        {
            var m = new MultiDictionary<string, int>();
            m.Add("a", 1);
            m.Add("a", 2);
            m.Add("a", 1); // duplicate allowed
            Check.True(m["a"].SequenceEqual(new[] { 1, 2, 1 }));
            Check.Equal(3, m.Total);   // 3 values
            Check.Equal(1, m.Count);   // 1 distinct key
        }

        public void MissingKey_ReturnsEmpty()
        {
            var m = new MultiDictionary<string, int>();
            Check.Equal(0, m["nope"].Count);
        }

        public void Remove_DeletesFirstOccurrence_AndDropsEmptyKey()
        {
            var m = new MultiDictionary<string, int>();
            m.Add("a", 1);
            m.Add("a", 1);
            Check.True(m.Remove("a", 1));
            Check.True(m["a"].SequenceEqual(new[] { 1 }));
            Check.True(m.Remove("a", 1));
            Check.False(m.ContainsKey("a")); // key gone when last value removed
            Check.False(m.Remove("a", 1));   // nothing left
        }

        public void RemoveAll_DropsKeyAndAdjustsCount()
        {
            var m = new MultiDictionary<string, int>();
            m.Add("a", 1); m.Add("a", 2); m.Add("b", 3);
            Check.True(m.RemoveAll("a"));
            Check.Equal(1, m.Total);   // only b -> 3 remains
            Check.Equal(1, m.Count);   // 1 distinct key
            Check.False(m.RemoveAll("a"));
        }

        public void Snapshot_IsIndependentOfInternalState()
        {
            var m = new MultiDictionary<string, int>();
            m.Add("a", 1);
            var snap = m["a"];
            m.Add("a", 2);
            Check.Equal(1, snap.Count); // earlier snapshot unaffected
        }

        public void NullKey_Throws()
        {
            var m = new MultiDictionary<string, int>();
            Check.Throws<ArgumentNullException>(() => m.Add(null!, 1));
        }

        // Differential: mirror random Add/Remove/RemoveAll against an insertion-ordered list of pairs and
        // compare total count, key set, and each key's value sequence after every operation.
        public void Differential_MatchesPairListModel()
        {
            var rng = new Random(24601);
            for (int trial = 0; trial < 300; trial++)
            {
                var m = new MultiDictionary<int, int>();
                var model = new List<KeyValuePair<int, int>>();

                for (int op = 0; op < 200; op++)
                {
                    int key = rng.Next(0, 5);
                    switch (rng.Next(4))
                    {
                        case 0:
                        case 1:
                            int val = rng.Next(0, 4);
                            m.Add(key, val);
                            model.Add(new KeyValuePair<int, int>(key, val));
                            break;
                        case 2:
                            int rv = rng.Next(0, 4);
                            bool a = m.Remove(key, rv);
                            int idx = model.FindIndex(p => p.Key == key && p.Value == rv);
                            bool e = idx >= 0;
                            Check.Equal(e, a, $"trial {trial} op {op}: remove({key},{rv})");
                            if (e) model.RemoveAt(idx);
                            break;
                        default:
                            bool ra = m.RemoveAll(key);
                            bool re = model.Any(p => p.Key == key);
                            Check.Equal(re, ra, $"trial {trial} op {op}: removeAll({key})");
                            model.RemoveAll(p => p.Key == key);
                            break;
                    }

                    Check.Equal(model.Count, m.Total, $"trial {trial} op {op}: total count");
                    var modelKeys = model.Select(p => p.Key).Distinct().OrderBy(k => k).ToArray();
                    var actualKeys = m.Keys.OrderBy(k => k).ToArray();
                    Check.True(modelKeys.SequenceEqual(actualKeys), $"trial {trial} op {op}: key set");

                    foreach (int k in modelKeys)
                    {
                        var expectedValues = model.Where(p => p.Key == k).Select(p => p.Value).ToArray();
                        Check.True(expectedValues.SequenceEqual(m[k]),
                            $"trial {trial} op {op}: values for key {k}");
                    }
                }
            }
        }
    }
}
