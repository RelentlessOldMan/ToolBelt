// ToolBelt test infrastructure — self-contained (BCL only). Not part of the shipping library.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ToolBelt.Tests.Framework
{
    /// <summary>
    /// Marks a test method or a <c>*Tests</c> class as parked. Optionally records why.
    /// Skipped tests are reported but neither run nor counted as failures.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SkipAttribute : Attribute
    {
        public SkipAttribute(string? reason = null) => Reason = reason;
        public string? Reason { get; }
    }

    /// <summary>Options parsed from the command line.</summary>
    public sealed class RunnerOptions
    {
        public string? Filter { get; set; }
        public bool Verbose { get; set; }
        public bool ListOnly { get; set; }
        public bool NoColor { get; set; }

        public static RunnerOptions Parse(string[] args)
        {
            var o = new RunnerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--filter" or "-f" when i + 1 < args.Length:
                        o.Filter = args[++i];
                        break;
                    case "--verbose" or "-v":
                        o.Verbose = true;
                        break;
                    case "--list" or "-l":
                        o.ListOnly = true;
                        break;
                    case "--no-color":
                        o.NoColor = true;
                        break;
                    default:
                        // A bare positional token is treated as the substring filter.
                        if (!args[i].StartsWith("-", StringComparison.Ordinal))
                            o.Filter = args[i];
                        break;
                }
            }
            return o;
        }
    }

    /// <summary>
    /// Discovers and runs tests by reflection. A test is any public, parameterless, void- or
    /// <see cref="Task"/>-returning instance method on a public non-abstract class whose name ends in
    /// "Tests". A fresh instance is constructed per method. Exit code 0 means every test passed.
    /// </summary>
    public static class TestRunner
    {
        public static int Run(Assembly assembly, string[] args)
        {
            var options = RunnerOptions.Parse(args);
            var tests = Discover(assembly, options.Filter).ToList();

            if (options.ListOnly)
            {
                foreach (var t in tests)
                    Console.WriteLine(t.DisplayName);
                Console.WriteLine($"\n{tests.Count} test(s).");
                return 0;
            }

            int passed = 0, failed = 0, skipped = 0;
            var failures = new List<(string Name, Exception Error)>();
            var sw = Stopwatch.StartNew();

            foreach (var test in tests)
            {
                if (test.Skip is not null)
                {
                    skipped++;
                    Write(options, ConsoleColor.Yellow, "SKIP");
                    Console.WriteLine($" {test.DisplayName}"
                        + (string.IsNullOrEmpty(test.Skip.Reason) ? "" : $" — {test.Skip.Reason}"));
                    continue;
                }

                try
                {
                    RunOne(test);
                    passed++;
                    if (options.Verbose)
                    {
                        Write(options, ConsoleColor.Green, "PASS");
                        Console.WriteLine($" {test.DisplayName}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add((test.DisplayName, ex));
                    Write(options, ConsoleColor.Red, "FAIL");
                    Console.WriteLine($" {test.DisplayName}");
                }
            }

            sw.Stop();

            if (failures.Count > 0)
            {
                Console.WriteLine();
                foreach (var (name, error) in failures)
                {
                    Write(options, ConsoleColor.Red, "FAIL");
                    Console.WriteLine($" {name}");
                    Console.WriteLine($"     {error.GetType().Name}: {error.Message}");
                    if (options.Verbose && error.StackTrace is not null)
                        Console.WriteLine("     " + error.StackTrace.Replace("\n", "\n     "));
                }
            }

            Console.WriteLine();
            var color = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Write(options, color, failed == 0 ? "ALL GREEN" : "FAILED");
            Console.WriteLine($"  {passed} passed, {failed} failed, {skipped} skipped "
                + $"in {sw.Elapsed.TotalSeconds:0.00}s");

            return failed == 0 ? 0 : 1;
        }

        private static void RunOne(TestCase test)
        {
            object? instance = test.Method.IsStatic ? null : Activator.CreateInstance(test.Type);
            try
            {
                object? result = test.Method.Invoke(instance, null);
                if (result is Task task)
                    task.GetAwaiter().GetResult();
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                // Unwrap the reflection layer so the real assertion failure surfaces.
                throw tie.InnerException;
            }
        }

        private static IEnumerable<TestCase> Discover(Assembly assembly, string? filter)
        {
            foreach (var type in assembly.GetTypes()
                         .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract
                                     && t.Name.EndsWith("Tests", StringComparison.Ordinal))
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var classSkip = type.GetCustomAttribute<SkipAttribute>();

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .Where(IsTestMethod)
                             .OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    var name = $"{type.FullName}.{method.Name}";
                    if (filter is not null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    yield return new TestCase(
                        type,
                        method,
                        name,
                        classSkip ?? method.GetCustomAttribute<SkipAttribute>());
                }
            }
        }

        private static bool IsTestMethod(MethodInfo m)
        {
            if (m.GetParameters().Length != 0 || m.IsGenericMethodDefinition || m.IsSpecialName)
                return false;
            return m.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(m.ReturnType);
        }

        private static void Write(RunnerOptions options, ConsoleColor color, string text)
        {
            if (options.NoColor)
            {
                Console.Write(text);
                return;
            }
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = previous;
        }

        private sealed class TestCase
        {
            public TestCase(Type type, MethodInfo method, string displayName, SkipAttribute? skip)
            {
                Type = type;
                Method = method;
                DisplayName = displayName;
                Skip = skip;
            }

            public Type Type { get; }
            public MethodInfo Method { get; }
            public string DisplayName { get; }
            public SkipAttribute? Skip { get; }
        }
    }
}
