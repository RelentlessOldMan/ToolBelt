// ToolBelt drop-in — also copy Objects/TypeUtils.cs.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

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
        /// <summary>
        /// Creates an instance of <paramref name="type"/> with the public constructor that fits <paramref name="args"/> most
        /// closely: among those that accept them, the one whose parameter types match the arguments' types most exactly (a
        /// string argument prefers a string parameter to an object one). A struct with no arguments is default-initialized.
        /// An exception thrown by the constructor propagates as itself.
        /// </summary>
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

            // By-ref, pointer and by-ref-like types (Span) cannot be boxed: invoking their constructors threw.
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters
                || type.IsByRef || type.IsPointer || type == typeof(void) || IsByRefLike(type))
            {
                instance = null;
                return false;
            }

            ConstructorInfo? best = null;
            int bestScore = int.MaxValue;
            foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length != args.Length || !Matches(ps, args)) continue;
                int score = Score(ps, args);
                if (score < bestScore) { best = ctor; bestScore = score; }
            }

            if (best is null)
            {
                // Not Nullable<T>: its default boxes to null, which Create returned as an instance.
                if (type.IsValueType && args.Length == 0 && Nullable.GetUnderlyingType(type) is null)
                {
                    instance = Activator.CreateInstance(type);   // a struct's implicit parameterless constructor
                    return true;
                }
                instance = null;
                return false;
            }
            try
            {
                instance = best.Invoke(args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            return true;
        }

        // Lower is closer: for each argument, the inheritance distance from its type to the parameter type; an interface
        // or Nullable parameter counts as far, and an object parameter (which accepts anything) as farther still, as in
        // C# overload resolution. A null argument adds nothing.
        private static bool IsByRefLike(Type type)
        {
            if (!type.IsValueType) return false;
            foreach (object a in type.GetCustomAttributes(false))
                if (a.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
            return false;
        }

        private static int Score(ParameterInfo[] parameters, object?[] args)
        {
            int score = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] is null) continue;
                Type pt = parameters[i].ParameterType;
                int distance = 0;
                Type? t = args[i]!.GetType();
                while (t != null && t != pt) { t = t.BaseType; distance++; }
                if (pt == typeof(object) && distance > 0) score += 2000;
                else score += t == pt ? distance : 1000;   // reached only through an interface (or boxing to Nullable)
            }
            return score;
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
