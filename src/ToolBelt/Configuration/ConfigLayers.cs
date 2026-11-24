// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Merges configuration sources in priority order (built-in defaults, then a file, then environment,
    /// then command-line overrides, for example) into one flat key space, and records which source supplied
    /// each final value. Sources added later win. That provenance report is the part everyone wishes they
    /// had while debugging a deployment. Keys are compared case-insensitively, by one rule on every runtime: ASCII and the
    /// letters of Latin, Greek, Cyrillic, Armenian and fullwidth Latin that have a simple upper/lower pair match their pair;
    /// every other character matches only itself. (The runtime's ordinal ignore-case differs: .NET matches µ with Μ, ς
    /// with Σ and Dž with DŽ, .NET Framework does not, so one source loaded on one and was refused on the other.) The caller supplies each
    /// source explicitly — nothing is read from the environment implicitly. A source holding one key in two casings with
    /// different values is refused (<see cref="ArgumentException"/>). Each source is copied when added, so later
    /// changes to the dictionary are not seen; a lookup costs O(layers). A null value is unset, as the binder reads it: a
    /// lower source's value for the key shows through (a key that every source holding it sets to null resolves to null).
    /// </summary>
    public sealed class ConfigLayers
    {
        private readonly List<(string Source, IReadOnlyDictionary<string, string?> Values)> _layers =
            new List<(string, IReadOnlyDictionary<string, string?>)>();

        /// <summary>Adds a source. Later sources override earlier ones for the same key.</summary>
        public ConfigLayers Add(string sourceName, IReadOnlyDictionary<string, string?> values)
        {
            if (sourceName is null) throw new ArgumentNullException(nameof(sourceName));
            if (values is null) throw new ArgumentNullException(nameof(values));
            // A case-insensitive copy. A key in two casings kept whichever was enumerated last, silently, so which value
            // won depended on the dictionary's order; with different values it is refused (the same value twice is fine).
            var copy = new Dictionary<string, string?>(KeyComparer.Instance);
            var spelled = new Dictionary<string, string>(KeyComparer.Instance);
            foreach (var kv in values)
            {
                if (kv.Key is null) throw new ArgumentException("A source holds a null key.", nameof(values));
                if (spelled.TryGetValue(kv.Key, out string? first) && copy[kv.Key] != kv.Value)
                    throw new ArgumentException($"Source '{sourceName}' holds '{first}' and '{kv.Key}', the same key in different casing, with different values.", nameof(values));
                spelled[kv.Key] = kv.Key;
                copy[kv.Key] = kv.Value;
            }
            _layers.Add((sourceName, copy));
            return this;
        }

        /// <summary>The merged key/value space, highest-priority value per key.</summary>
        public IReadOnlyDictionary<string, string?> Resolve()
        {
            var result = new Dictionary<string, string?>(KeyComparer.Instance);
            // A null above a value hid it, and the binder then took the property's default, from no source at all.
            foreach (var (_, values) in _layers)
                foreach (var kv in values)
                    if (kv.Value != null || !result.ContainsKey(kv.Key)) result[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>For each key, the name of the source that supplied its winning value.</summary>
        public IReadOnlyDictionary<string, string> Provenance()
        {
            var result = new Dictionary<string, string>(KeyComparer.Instance);
            foreach (var (source, values) in _layers)
                foreach (var kv in values)
                    if (kv.Value != null || !result.ContainsKey(kv.Key)) result[kv.Key] = source;
            return result;
        }

        /// <summary>The winning value for <paramref name="key"/> (case-insensitive); false if no source sets it (a key
        /// held only as null is unset: it returned true with a null value).</summary>
        public bool TryGet(string key, out string? value) => Find(key, out value) >= 0 && value != null;

        /// <summary>The source that supplied the winning value for <paramref name="key"/>, or null if no source holds it
        /// (a key held only as null names the lowest source holding it, as <see cref="Provenance"/> does).</summary>
        public string? SourceOf(string key)
        {
            int layer = Find(key, out _);
            return layer >= 0 ? _layers[layer].Source : null;
        }

        // The last layer holding the key, without building Resolve() for every lookup.
        private int Find(string key, out string? value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            // A null is unset: a lower layer's value wins. Held only as null, the key resolves to null from the lowest
            // layer holding it, as Resolve and Provenance do.
            int nullAt = -1;
            for (int i = _layers.Count - 1; i >= 0; i--)
                if (_layers[i].Values.TryGetValue(key, out value))
                {
                    if (value != null) return i;
                    nullAt = i;
                }
            value = null;
            return nullAt;
        }

        // Case-insensitive by a fixed rule. A character folds to its uppercase only inside the ranges below, and only when
        // that uppercase lowers back to it; there .NET Framework 4.8 and .NET 8 agree on every pair. Outside them (IPA,
        // Georgian, Cherokee, newer additions) and for one-way mappings (µ, ς, ſ, Dž, the Kelvin sign) the runtimes'
        // tables differ, so a character matches only itself.
        private sealed class KeyComparer : IEqualityComparer<string>
        {
            public static readonly KeyComparer Instance = new KeyComparer();

            private static char Fold(char c)
            {
                if (c < 0x80) return c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
                bool inScope = c <= 0x023E || (c >= 0x0241 && c <= 0x024F) || (c >= 0x0370 && c <= 0x03F2)
                    || (c >= 0x03F4 && c <= 0x0523) || (c >= 0x0531 && c <= 0x0587) || (c >= 0xFF21 && c <= 0xFF5A);
                if (!inScope) return c;
                char u = char.ToUpperInvariant(c);
                return u != c && char.ToLowerInvariant(u) == c ? u : c;
            }

            public bool Equals(string? x, string? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null || x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i] && Fold(x[i]) != Fold(y[i])) return false;
                return true;
            }

            public int GetHashCode(string s)
            {
                if (s is null) throw new ArgumentNullException(nameof(s));
                unchecked
                {
                    int h = (int)2166136261;
                    foreach (char c in s) h = (h ^ Fold(c)) * 16777619;
                    return h;
                }
            }
        }
    }
}
