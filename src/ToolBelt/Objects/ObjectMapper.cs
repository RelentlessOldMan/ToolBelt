// ToolBelt drop-in — also copy Objects/TypeUtils.cs.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Copies matching public properties from a source object to a target of another type, converting
    /// compatible types along the way, with an ignore list and per-property custom projections. Replaces
    /// the walls of hand-written assignments at every boundary — which are tedious and quietly go stale when
    /// a member is added. Names match case-insensitively; unconvertible or missing members are skipped, as are target
    /// properties without a public setter, indexers, and a null bound for a non-nullable value-type property. A property
    /// overridden with only one accessor keeps the other it inherits; an interface source includes the properties of the
    /// interfaces it extends. A
    /// value-type <typeparamref name="TTarget"/> works with <see cref="Map(TSource)"/> only: the in-place overload would
    /// fill a copy, so it throws.
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
            if (source is null) throw new ArgumentNullException(nameof(source));
            object target = new TTarget();   // boxed once, so a struct target is filled in place
            MapInto(source, target);
            return (TTarget)target;
        }

        /// <summary>Maps into an existing target instance.</summary>
        public void Map(TSource source, TTarget target)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (typeof(TTarget).IsValueType)
                throw new InvalidOperationException($"{typeof(TTarget).Name} is a value type; mapping into it in place would change a copy. Use Map(source).");
            MapInto(source, target);
        }

        // True if a more derived property of the same name hides p (`new`): reflection lists both, and the base one,
        // listed last, overwrote the derived one.
        private static bool Hidden(PropertyInfo p, Type type)
        {
            foreach (PropertyInfo other in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (other.Name == p.Name && other.DeclaringType != p.DeclaringType && other.GetIndexParameters().Length == 0
                    && other.DeclaringType!.IsSubclassOf(p.DeclaringType!)) return true;
            return false;
        }

        private void MapInto(TSource source, object target)
        {
            var sourceProps = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo p in typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0 && !Hidden(p, typeof(TSource)) && PublicAccessor(p, setter: false) is MethodInfo g)
                    sourceProps[p.Name] = g;
            // An interface's GetProperties lists only its own: the ones it inherits are on the interfaces it extends.
            if (typeof(TSource).IsInterface)
                foreach (Type i in typeof(TSource).GetInterfaces())
                    foreach (PropertyInfo p in i.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (p.GetIndexParameters().Length == 0 && !sourceProps.ContainsKey(p.Name) && PublicAccessor(p, setter: false) is MethodInfo g)
                            sourceProps[p.Name] = g;

            foreach (PropertyInfo targetProp in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? setter = PublicAccessor(targetProp, setter: true);
                if (setter is null || targetProp.GetIndexParameters().Length != 0
                    || _ignored.Contains(targetProp.Name) || Hidden(targetProp, typeof(TTarget))) continue;

                object? value;
                if (_custom.TryGetValue(targetProp.Name, out var selector))
                {
                    value = selector(source);
                }
                else if (sourceProps.TryGetValue(targetProp.Name, out MethodInfo? getter))
                {
                    value = getter.Invoke(source, null);
                }
                else
                {
                    continue; // no source for this target property
                }

                if (value is null)
                {
                    if (targetProp.PropertyType.IsValueType && Nullable.GetUnderlyingType(targetProp.PropertyType) is null)
                        continue; // null does not fit; leave the target's value
                }
                else if (!targetProp.PropertyType.IsInstanceOfType(value))
                {
                    if (!TypeUtils.TryConvert(value, targetProp.PropertyType, out value))
                        continue; // unconvertible; leave target default
                }
                setter.Invoke(target, new[] { value });
            }
        }

        // The public get (or set) accessor of p. An override that declares only the other accessor inherits this one from
        // the property it overrides, which reflection does not report on the override; a non-public accessor counts as none.
        private static MethodInfo? PublicAccessor(PropertyInfo p, bool setter)
        {
            for (PropertyInfo? q = p; q != null; q = OverriddenProperty(q))
            {
                MethodInfo? m = setter ? q.GetSetMethod(true) : q.GetGetMethod(true);
                if (m != null) return m.IsPublic ? m : null;
            }
            return null;
        }

        private static PropertyInfo? OverriddenProperty(PropertyInfo p)
        {
            MethodInfo? accessor = p.GetGetMethod(true) ?? p.GetSetMethod(true);
            if (accessor is null || accessor.GetBaseDefinition().DeclaringType == accessor.DeclaringType) return null;
            for (Type? t = p.DeclaringType!.BaseType; t != null; t = t.BaseType)
                foreach (PropertyInfo q in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (q.Name == p.Name && q.GetIndexParameters().Length == 0) return q;
            return null;
        }
    }
}
