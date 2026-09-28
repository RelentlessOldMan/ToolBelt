// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Reads and writes a value through a dotted member path with optional indexers, for example
    /// <c>order.lines[2].price</c> or <c>settings["theme"]</c>. On failure it throws an error identifying
    /// the exact path segment that could not be resolved, rather than a null-reference somewhere inside.
    /// The single-level by-name accessor is the trivial special case.
    /// </summary>
    public static class PropertyPath
    {
        public static object? Get(object root, string path)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            object? current = root;
            for (int i = 0; i < steps.Count; i++)
            {
                if (current is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                current = steps[i].Read(current, path);
            }
            return current;
        }

        public static bool TryGet(object root, string path, out object? value)
        {
            try { value = Get(root, path); return true; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                value = null;
                return false;
            }
        }

        public static void Set(object root, string path, object? value)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            object? target = root;
            for (int i = 0; i < steps.Count - 1; i++)
            {
                if (target is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                target = steps[i].Read(target, path);
            }
            if (target is null)
                throw new InvalidOperationException($"Path '{path}' hit a null before the final segment.");
            steps[steps.Count - 1].Write(target, value, path);
        }

        // ---- Path parsing ----

        private abstract class Step
        {
            public abstract object? Read(object target, string fullPath);
            public abstract void Write(object target, object? value, string fullPath);
        }

        private sealed class MemberStep : Step
        {
            private readonly string _name;
            public MemberStep(string name) => _name = name;
            public override string ToString() => _name;

            public override object? Read(object target, string fullPath)
            {
                PropertyInfo? p = target.GetType().GetProperty(_name, BindingFlags.Public | BindingFlags.Instance);
                if (p is null)
                    throw new ArgumentException($"Segment '{_name}' is not a public property of {target.GetType().Name} (path '{fullPath}').");
                return p.GetValue(target);
            }

            public override void Write(object target, object? value, string fullPath)
            {
                PropertyInfo? p = target.GetType().GetProperty(_name, BindingFlags.Public | BindingFlags.Instance);
                if (p is null || !p.CanWrite)
                    throw new ArgumentException($"Segment '{_name}' is not a writable property of {target.GetType().Name} (path '{fullPath}').");
                p.SetValue(target, value);
            }
        }

        private sealed class IndexStep : Step
        {
            private readonly string _token;
            public IndexStep(string token) => _token = token;
            public override string ToString() => $"[{_token}]";

            public override object? Read(object target, string fullPath)
            {
                if (target is IDictionary dict)
                    return dict[ConvertKey(dict, _token)];
                if (target is IList list)
                    return list[ParseIndex(fullPath)];
                throw new ArgumentException($"Segment '[{_token}]' requires a list or dictionary, got {target.GetType().Name} (path '{fullPath}').");
            }

            public override void Write(object target, object? value, string fullPath)
            {
                if (target is IDictionary dict) { dict[ConvertKey(dict, _token)] = value; return; }
                if (target is IList list) { list[ParseIndex(fullPath)] = value; return; }
                throw new ArgumentException($"Segment '[{_token}]' requires a list or dictionary, got {target.GetType().Name} (path '{fullPath}').");
            }

            private int ParseIndex(string fullPath)
            {
                if (!int.TryParse(_token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    throw new ArgumentException($"Index '[{_token}]' is not an integer (path '{fullPath}').");
                return index;
            }

            private static object ConvertKey(IDictionary dict, string token)
            {
                // Convert the textual key to the dictionary's key type where it is a common one.
                Type keyType = typeof(object);
                foreach (var t in dict.GetType().GetGenericArguments()) { keyType = t; break; }
                if (keyType == typeof(int)) return int.Parse(token, CultureInfo.InvariantCulture);
                if (keyType == typeof(long)) return long.Parse(token, CultureInfo.InvariantCulture);
                return token; // string or object key
            }
        }

        private static List<Step> Parse(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", nameof(path));

            var steps = new List<Step>();
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    i++;
                    continue;
                }
                if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) throw new ArgumentException($"Unclosed '[' in path '{path}'.");
                    string token = path.Substring(i + 1, close - i - 1).Trim().Trim('"', '\'');
                    steps.Add(new IndexStep(token));
                    i = close + 1;
                }
                else
                {
                    int j = i;
                    while (j < path.Length && path[j] != '.' && path[j] != '[') j++;
                    steps.Add(new MemberStep(path.Substring(i, j - i)));
                    i = j;
                }
            }
            if (steps.Count == 0) throw new ArgumentException("Path must not be empty.", nameof(path));
            return steps;
        }
    }
}
