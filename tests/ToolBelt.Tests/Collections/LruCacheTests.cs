using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class LruCacheTests
    {
        public void EvictsLeastRecentlyUsed()
        {
            var cache = new LruCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Set(3, "c"); // evicts key 1 (LRU)

            Check.False(cache.ContainsKey(1));
            Check.True(cache.ContainsKey(2));
            Check.True(cache.ContainsKey(3));
        }

        public void GetRefreshesRecency()
        {
            var cache = new LruCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.TryGet(1, out _); // 1 becomes MRU, so 2 is now LRU
            cache.Set(3, "c");      // evicts 2

            Check.True(cache.ContainsKey(1));
            Check.False(cache.ContainsKey(2));
        }

        public void PeekDoesNotAffectRecency()
        {
            var cache = new LruCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Peek(1, out _); // must NOT refresh 1
            cache.Set(3, "c");    // 1 is still LRU → evicted

            Check.False(cache.ContainsKey(1));
            Check.True(cache.ContainsKey(2));
        }

        public void UpdateExistingKey_RefreshesAndKeepsCount()
        {
            var cache = new LruCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Set(1, "A"); // update value + refresh recency
            cache.Set(3, "c"); // evicts 2

            Check.Equal(2, cache.Count);
            cache.Peek(1, out var v);
            Check.Equal("A", v);
            Check.False(cache.ContainsKey(2));
        }

        public void NullKey_Throws_ForReferenceKeys()
        {
            var cache = new LruCache<string, int>(2);
            Check.Throws<ArgumentNullException>(() => cache.Set(null!, 1));
        }

        public void InvalidCapacity_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
        }

        // Differential: mirror a random op sequence against a deliberately naive O(n) reference model and
        // assert both agree on membership AND recency order after every single operation.
        public void Differential_MatchesNaiveModel()
        {
            var rng = new Random(31337);
            for (int trial = 0; trial < 300; trial++)
            {
                int capacity = rng.Next(1, 8);
                var actual = new LruCache<int, int>(capacity);
                var model = new NaiveLru(capacity);
                int keyUniverse = capacity + 3; // force collisions and evictions

                for (int op = 0; op < 200; op++)
                {
                    int key = rng.Next(0, keyUniverse);
                    if (rng.Next(2) == 0)
                    {
                        int value = rng.Next();
                        actual.Set(key, value);
                        model.Set(key, value);
                    }
                    else
                    {
                        bool a = actual.TryGet(key, out int av);
                        bool m = model.TryGet(key, out int mv);
                        Check.Equal(m, a, $"trial {trial} op {op}: TryGet hit for key {key}");
                        if (m) Check.Equal(mv, av, $"trial {trial} op {op}: value for key {key}");
                    }

                    Check.Equal(model.Count, actual.Count, $"trial {trial} op {op}: count");
                    Check.True(model.KeysMruFirst().SequenceEqual(actual.Keys),
                        $"trial {trial} op {op}: recency order [{string.Join(",", actual.Keys)}] " +
                        $"vs model [{string.Join(",", model.KeysMruFirst())}]");
                }
            }
        }

        // Obviously-correct reference: a list holding keys MRU-first, rebuilt with plain list ops.
        private sealed class NaiveLru
        {
            private readonly int _capacity;
            private readonly List<int> _mruFirst = new List<int>();
            private readonly Dictionary<int, int> _values = new Dictionary<int, int>();

            public NaiveLru(int capacity) => _capacity = capacity;

            public int Count => _mruFirst.Count;

            public void Set(int key, int value)
            {
                _values[key] = value;
                _mruFirst.Remove(key);
                _mruFirst.Insert(0, key);
                if (_mruFirst.Count > _capacity)
                {
                    int lru = _mruFirst[_mruFirst.Count - 1];
                    _mruFirst.RemoveAt(_mruFirst.Count - 1);
                    _values.Remove(lru);
                }
            }

            public bool TryGet(int key, out int value)
            {
                if (_values.TryGetValue(key, out value))
                {
                    _mruFirst.Remove(key);
                    _mruFirst.Insert(0, key);
                    return true;
                }
                return false;
            }

            public IEnumerable<int> KeysMruFirst() => _mruFirst;
        }
    }
}
