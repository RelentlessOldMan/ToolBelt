// ToolBelt drop-in — also copy Objects/TypeUtils.cs.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Copies matching public properties from a source object to a target of another type, converting
    /// compatible types along the way, with an ignore list and per-property custom projections. Replaces
    /// the walls of hand-written assignments at every boundary — which are tedious and quietly go stale when
    /// a member is added. Names match case-insensitively, an exact-case name first; unconvertible or missing members are skipped, as are target
    /// properties without a public setter, indexers, and a null bound for a non-nullable value-type property. A property
    /// overridden with only one accessor keeps the other it inherits; an interface source includes the properties of the
    /// interfaces it extends. A
    /// property of a by-ref-like type (Span), which reflection cannot read or write, is skipped; a getter, setter or
    /// selector that throws propagates its own exception. A
    /// value-type <typeparamref name="TTarget"/> works with <see cref="Map(TSource)"/> only: the in-place overload would
    /// fill a copy, so it throws.
    /// </summary>
    public sealed class ObjectMapper<TSource, TTarget> where TTarget : new()
    {
        // Kept by exact name: an entry names the target property of that exact name, or, when there is none, the ones
        // matching it ignoring case. With Name and NAME on the target, Ignore("NAME") ignored both.
        private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<TSource, object?>> _custom =
            new Dictionary<string, Func<TSource, object?>>(StringComparer.Ordinal);

        /// <summary>
        /// Excludes a target property from mapping: the one of exactly that name, or, when there is none, those of that name
        /// ignoring case.
        /// </summary>
        public ObjectMapper<TSource, TTarget> Ignore(string targetProperty)
        {
            _ignored.Add(targetProperty ?? throw new ArgumentNullException(nameof(targetProperty)));
            return this;
        }

        /// <summary>
        /// Supplies a custom value for a target property, overriding name matching: the one of exactly that name, or, when
        /// there is none, those of that name ignoring case (the ordinal-first such entry, if several match).
        /// </summary>
        public ObjectMapper<TSource, TTarget> ForMember(string targetProperty, Func<TSource, object?> selector)
        {
            if (targetProperty is null) throw new ArgumentNullException(nameof(targetProperty));
            _custom[targetProperty] = selector ?? throw new ArgumentNullException(nameof(selector));
            return this;
        }

        private bool Ignored(string name, HashSet<string> targetNames)
        {
            if (_ignored.Contains(name)) return true;
            foreach (string e in _ignored)
                if (!targetNames.Contains(e) && string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private Func<TSource, object?>? Custom(string name, HashSet<string> targetNames)
        {
            if (_custom.TryGetValue(name, out var exact)) return exact;
            string? best = null;
            foreach (string e in _custom.Keys)
                if (!targetNames.Contains(e) && string.Equals(e, name, StringComparison.OrdinalIgnoreCase)
                    && (best is null || string.CompareOrdinal(e, best) < 0)) best = e;
            return best is null ? null : _custom[best];
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
            var exact = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo p in typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0 && !Hidden(p, typeof(TSource)) && !IsByRefLike(p.PropertyType)
                    && PublicAccessor(p, setter: false) is MethodInfo g)
                    exact[p.Name] = g;
            // An interface's GetProperties lists only its own: the ones it inherits are on the interfaces it extends.
            if (typeof(TSource).IsInterface)
                foreach (Type i in typeof(TSource).GetInterfaces())
                    foreach (PropertyInfo p in i.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (p.GetIndexParameters().Length == 0 && !exact.ContainsKey(p.Name) && !IsByRefLike(p.PropertyType)
                            && PublicAccessor(p, setter: false) is MethodInfo g)
                            exact[p.Name] = g;
            // An exact-case name wins, then the ordinal-first of the others: with Name and NAME on the source, whichever
            // reflection listed last filled the target's Name.
            var sourceProps = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>(exact.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
                if (!sourceProps.ContainsKey(name)) sourceProps[name] = exact[name];

            PropertyInfo[] targetProps = typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo p in targetProps) targetNames.Add(p.Name);
            foreach (PropertyInfo targetProp in targetProps)
            {
                MethodInfo? setter = PublicAccessor(targetProp, setter: true);
                if (setter is null || targetProp.GetIndexParameters().Length != 0
                    || Ignored(targetProp.Name, targetNames) || Hidden(targetProp, typeof(TTarget)) || IsByRefLike(targetProp.PropertyType)) continue;

                object? value;
                if (Custom(targetProp.Name, targetNames) is Func<TSource, object?> selector)
                {
                    value = selector(source);
                }
                else if (exact.TryGetValue(targetProp.Name, out MethodInfo? getter) || sourceProps.TryGetValue(targetProp.Name, out getter))
                {
                    value = Call(getter, source, null);
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
                Call(setter, target, new[] { value });
            }
        }

        // Calls an accessor, rethrowing what it threw rather than the TargetInvocationException reflection wraps it in.
        private static object? Call(MethodInfo accessor, object? target, object?[]? args)
        {
            try { return accessor.Invoke(target, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static bool IsByRefLike(Type type)
        {
            if (type.IsByRef || type.IsPointer) return true;
            if (!type.IsValueType) return false;
            // .NET Framework does not mark its own by-ref-like types with the attribute.
            if (type == typeof(TypedReference) || type.FullName == "System.ArgIterator" || type.FullName == "System.RuntimeArgumentHandle") return true;
            foreach (object a in type.GetCustomAttributes(false))
                if (a.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
            return false;
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
