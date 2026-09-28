// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Constructs types by matching the available arguments to the best-fitting public constructor, and
    /// safely scans an assembly to instantiate implementations of a base type with a per-type error callback
    /// so one bad type does not abort the scan. This is the smallest honest step toward simple plugin
    /// loading — it is explicitly NOT a dependency-injection container.
    /// </summary>
    public static class ActivatorUtils
    {
        /// <summary>Creates an instance of <paramref name="type"/>, choosing a constructor that fits <paramref name="args"/>.</summary>
        public static object Create(Type type, params object?[] args)
        {
            if (!TryCreate(type, args, out object? instance))
                throw new MissingMethodException($"No public constructor of {type.Name} matches the {args?.Length ?? 0} supplied argument(s).");
            return instance!;
        }

        public static bool TryCreate(Type type, object?[] args, out object? instance)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            args ??= Array.Empty<object?>();

            if (type.IsAbstract || type.IsInterface)
            {
                instance = null;
                return false;
            }

            ConstructorInfo? best = null;
            foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length != args.Length) continue;
                if (Matches(ps, args)) { best = ctor; break; }
            }

            if (best is null)
            {
                instance = null;
                return false;
            }
            instance = best.Invoke(args);
            return true;
        }

        /// <summary>
        /// Instantiates every concrete <typeparamref name="TBase"/> in the assembly that has a parameterless
        /// constructor, reporting per-type failures to <paramref name="onError"/> and continuing.
        /// </summary>
        public static IReadOnlyList<TBase> ScanAndCreate<TBase>(Assembly assembly, Action<Type, Exception>? onError = null)
            where TBase : class
        {
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            var result = new List<TBase>();
            foreach (Type type in TypeUtils.GetDerivedTypes(typeof(TBase), assembly))
            {
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                try
                {
                    if (Activator.CreateInstance(type) is TBase created)
                        result.Add(created);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(type, ex);
                }
            }
            return result;
        }

        private static bool Matches(ParameterInfo[] parameters, object?[] args)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                Type pt = parameters[i].ParameterType;
                if (args[i] is null)
                {
                    // null fits reference types and Nullable<T>, not other value types.
                    if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null) return false;
                }
                else if (!pt.IsInstanceOfType(args[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
