// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A set that remembers insertion order: enumeration yields items in the order they were first
    /// added, while membership, add, and remove stay O(1). Re-adding an existing item is a no-op that
    /// preserves its original position. Not thread-safe.
    /// </summary>
    public sealed class OrderedSet<T> : IEnumerable<T>
        where T : notnull
    {
        private readonly Dictionary<T, LinkedListNode<T>> _map = new Dictionary<T, LinkedListNode<T>>();
        private readonly LinkedList<T> _order = new LinkedList<T>();

        public int Count => _map.Count;

        /// <summary>Adds an item. Returns false if it was already present.</summary>
        public bool Add(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (_map.ContainsKey(item))
                return false;
            _map[item] = _order.AddLast(item);
            return true;
        }

        public bool Contains(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            return _map.ContainsKey(item);
        }

        public bool Remove(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (_map.TryGetValue(item, out var node))
            {
                _order.Remove(node);
                _map.Remove(item);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        public T[] ToArray()
        {
            var result = new T[_map.Count];
            int i = 0;
            foreach (var item in _order)
                result[i++] = item;
            return result;
        }

        public IEnumerator<T> GetEnumerator() => _order.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
