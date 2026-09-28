// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Merges configuration sources in priority order (built-in defaults, then a file, then environment,
    /// then command-line overrides, for example) into one flat key space, and records which source supplied
    /// each final value. Sources added later win. That provenance report is the part everyone wishes they
    /// had while debugging a deployment. Keys are compared case-insensitively. The caller supplies each
    /// source explicitly — nothing is read from the environment implicitly.
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
            _layers.Add((sourceName, values));
            return this;
        }

        /// <summary>The merged key/value space, highest-priority value per key.</summary>
        public IReadOnlyDictionary<string, string?> Resolve()
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, values) in _layers)
                foreach (var kv in values)
                    result[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>For each key, the name of the source that supplied its winning value.</summary>
        public IReadOnlyDictionary<string, string> Provenance()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, values) in _layers)
                foreach (var kv in values)
                    result[kv.Key] = source;
            return result;
        }

        public bool TryGet(string key, out string? value)
            => Resolve().TryGetValue(key, out value); // case-insensitive, highest-priority value

        /// <summary>The source that supplied the winning value for <paramref name="key"/>, or null if unset.</summary>
        public string? SourceOf(string key)
            => Provenance().TryGetValue(key, out string? source) ? source : null;
    }
}
