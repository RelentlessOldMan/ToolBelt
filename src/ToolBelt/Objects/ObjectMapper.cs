// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Copies matching public properties from a source object to a target of another type, converting
    /// compatible types along the way, with an ignore list and per-property custom projections. Replaces
    /// the walls of hand-written assignments at every boundary — which are tedious and quietly go stale when
    /// a member is added. Names match case-insensitively; unconvertible or missing members are skipped.
    /// </summary>
    public sealed class ObjectMapper<TSource, TTarget> where TTarget : new()
    {
        private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<TSource, object?>> _custom =
            new Dictionary<string, Func<TSource, object?>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Excludes a target property from mapping.</summary>
        public ObjectMapper<TSource, TTarget> Ignore(string targetProperty)
        {
            _ignored.Add(targetProperty ?? throw new ArgumentNullException(nameof(targetProperty)));
            return this;
        }

        /// <summary>Supplies a custom value for a target property, overriding name matching.</summary>
        public ObjectMapper<TSource, TTarget> ForMember(string targetProperty, Func<TSource, object?> selector)
        {
            if (targetProperty is null) throw new ArgumentNullException(nameof(targetProperty));
            _custom[targetProperty] = selector ?? throw new ArgumentNullException(nameof(selector));
            return this;
        }

        /// <summary>Maps into a new target instance.</summary>
        public TTarget Map(TSource source)
        {
            var target = new TTarget();
            Map(source, target);
            return target;
        }

        /// <summary>Maps into an existing target instance.</summary>
        public void Map(TSource source, TTarget target)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (target is null) throw new ArgumentNullException(nameof(target));

            var sourceProps = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo p in typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                    sourceProps[p.Name] = p;

            foreach (PropertyInfo targetProp in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!targetProp.CanWrite || _ignored.Contains(targetProp.Name)) continue;

                object? value;
                if (_custom.TryGetValue(targetProp.Name, out var selector))
                {
                    value = selector(source);
                }
                else if (sourceProps.TryGetValue(targetProp.Name, out PropertyInfo? sp))
                {
                    value = sp.GetValue(source);
                }
                else
                {
                    continue; // no source for this target property
                }

                if (value != null && !targetProp.PropertyType.IsInstanceOfType(value))
                {
                    if (!TypeUtils.TryConvert(value, targetProp.PropertyType, out value))
                        continue; // unconvertible; leave target default
                }
                targetProp.SetValue(target, value);
            }
        }
    }
}
