using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using ToolBelt.Grids;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 136 (objects, functional, grids, graphs, intervals, eighth pass): each failed on the code before its fix.
    public sealed class ObjectsReview136Tests
    {
        // ---- TypeUtils: default(T) for a struct with a parameterless constructor ----

        private struct Answer
        {
            public int X;
            public Answer() { X = 42; }
        }

        private struct Exploding
        {
            public int X;
            public Exploding() { X = 1; throw new InvalidOperationException("ctor"); }
        }

        private sealed class TextSource { public string P { get; set; } = "x"; }
        private sealed class ExplodingTarget { public Exploding P { get; set; } public int Q { get; set; } }
        private sealed class QSource { public string P { get; set; } = "x"; public int Q { get; set; } = 7; }

        // Activator ran the struct's explicit parameterless constructor: the "default" had X = 42, and one that threw
        // escaped TryConvert, ConvertOrDefault and ObjectMapper.Map as TargetInvocationException.
        public void TypeUtils_StructDefault_DoesNotRunItsConstructor()
        {
            Check.Equal(0, ((Answer)TypeUtils.DefaultValue(typeof(Answer))!).X);
            Check.False(TypeUtils.TryConvert("x", typeof(Answer), out object? r));
            Check.Equal(0, ((Answer)r!).X);
            Check.Equal(0, ((Exploding)TypeUtils.DefaultValue(typeof(Exploding))!).X);
            Check.False(TypeUtils.TryConvert("x", typeof(Exploding), out r));
            Check.Equal(0, ((Exploding)r!).X);
            Check.Equal("fb", TypeUtils.ConvertOrDefault("x", typeof(Exploding), "fb"));
            var mapped = new ObjectMapper<QSource, ExplodingTarget>().Map(new QSource());
            Check.Equal(0, mapped.P.X);
            Check.Equal(7, mapped.Q);
            // Guards: ordinary defaults.
            Check.Equal(0, TypeUtils.DefaultValue(typeof(int)));
            Check.Equal(DayOfWeek.Sunday, TypeUtils.DefaultValue(typeof(DayOfWeek)));
            Check.Equal(default(Guid), TypeUtils.DefaultValue(typeof(Guid)));
            Check.Null(TypeUtils.DefaultValue(typeof(int?)));
            Check.Null(TypeUtils.DefaultValue(typeof(string)));
        }

        // ---- TypeUtils: a DateTime's own text converts back to it ----

        // ChangeType parsed "...Z" as local time (Kind Local, shifted by the machine's zone).
        public void TypeUtils_DateTimeText_RoundTripsItsKind()
        {
            foreach (var dt in new[]
            {
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567),
                new DateTime(2020, 7, 2, 3, 4, 5, DateTimeKind.Local),
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
                DateTime.MaxValue, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
            })
            {
                Check.True(TypeUtils.TryConvert(dt, typeof(string), out object? s));
                Check.True(TypeUtils.TryConvert(s, typeof(DateTime), out object? back), (string)s!);
                Check.Equal(dt.Ticks, ((DateTime)back!).Ticks, (string)s!);
                Check.Equal(dt.Kind, ((DateTime)back).Kind, (string)s!);
                Check.True(TypeUtils.TryConvert(s, typeof(DateTime?), out back));
                Check.Equal(dt, (DateTime)back!);
            }
            Check.Equal(DateTimeKind.Utc, ((DateTime)TypeUtils.ConvertOrDefault("2020-01-02T03:04:05Z", typeof(DateTime))!).Kind);
            Check.Equal(3, ((DateTime)TypeUtils.ConvertOrDefault("2020-01-02T03:04:05Z", typeof(DateTime))!).Hour);
            Check.False(TypeUtils.TryConvert("not a date", typeof(DateTime), out _));
        }

        // ---- Enums whose names differ only by case ----

        private enum Cased { A = 1, a = 2 }

        private sealed class CasedSource { public string P { get; set; } = "a"; }
        private sealed class CasedTarget { public Cased P { get; set; } }

        // Enum.Parse ignoring case took the first name that matched: "a" became A.
        public void TypeUtils_EnumName_PrefersTheExactCase()
        {
            Check.True(TypeUtils.TryConvert(Cased.a, typeof(string), out object? s));
            Check.True(TypeUtils.TryConvert(s, typeof(Cased), out object? back));
            Check.Equal(Cased.a, back);
            Check.Equal(Cased.A, TypeUtils.ConvertOrDefault("A", typeof(Cased)));
            Check.Equal(Cased.a, new ObjectMapper<CasedSource, CasedTarget>().Map(new CasedSource()).P);
            // Guard: a name in another case still parses.
            Check.Equal(DayOfWeek.Monday, TypeUtils.ConvertOrDefault("monday", typeof(DayOfWeek)));
            Check.Equal(DayOfWeek.Monday, TypeUtils.ConvertOrDefault("Monday", typeof(DayOfWeek?)));
        }

        // The same in PropertyPath's keys, which read and wrote the wrong entry.
        public void PropertyPath_EnumKey_PrefersTheExactCase()
        {
            var d = new Dictionary<Cased, int> { [Cased.A] = 10, [Cased.a] = 20 };
            Check.Equal(20, PropertyPath.Get(d, "[a]"));
            Check.Equal(10, PropertyPath.Get(d, "[A]"));
            PropertyPath.Set(d, "[a]", 99);
            Check.Equal(10, d[Cased.A]);
            Check.Equal(99, d[Cased.a]);
            var days = new Dictionary<DayOfWeek, int> { [DayOfWeek.Friday] = 5 };
            Check.Equal(5, PropertyPath.Get(days, "[friday]"));
        }

        // ---- Keys that print as paths ----

        private static Dictionary<string, int> Keyed(int bump) => new Dictionary<string, int>
        {
            ["a]b"] = 1 + bump, [" x"] = 2 + bump, ["x"] = 5, ["'y'"] = 3 + bump, ["y"] = 7, ["\"q\""] = 4 + bump, ["q"] = 8,
            ["x "] = 9 + bump, ["a.b"] = 10 + bump, ["it's"] = 11 + bump, ["\"]"] = 12 + bump,
        };

        // A key was printed raw in "[...]": "[a]b]" did not parse, "[ x]" read key "x", "['y']" key "y".
        public void FlattenAndDiff_KeyPaths_ReadBackTheirOwnKey()
        {
            var before = Keyed(0);
            var after = Keyed(100);
            var flat = FlattenObject.Flatten(before);
            Check.Equal(before.Count, flat.Count);
            foreach (var kv in flat)
            {
                Check.True(PropertyPath.TryGet(before, kv.Key, out object? v), kv.Key);
                Check.Equal(kv.Value, v, kv.Key);
            }
            Check.True(flat.ContainsKey("[x]"));
            Check.True(flat.ContainsKey("[it's]"));
            Check.True(flat.ContainsKey("[\"a]b\"]"));
            var diffs = PropertyDiff.Compare(before, after);
            Check.Equal(8, diffs.Count);
            foreach (var d in diffs)
            {
                Check.True(PropertyPath.TryGet(after, d.Path, out object? v), d.Path);
                Check.Equal(d.NewValue, v, d.Path);
            }
            // Unflatten reads Flatten's quoted keys back.
            var tree = UnflattenObject.Unflatten(flat);
            Check.Equal(before.Count, tree.Count);
            foreach (var kv in before)
                Check.Equal(kv.Value, tree[kv.Key], kv.Key);
        }

        // ---- Cell ----

        // ToString used the current culture: sv-SE printed (−1, 2) with U+2212.
        public void Cell_ToString_IsInvariant()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "sv-SE", "fa-IR", "ar-SA", "en-US" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(name);
                    Check.Equal("(-1, 2)", new Cell(-1, 2).ToString(), name);
                    Check.Equal("(-2147483648, 0)", new Cell(int.MinValue, 0).ToString(), name);
                }
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---- AttributeCache ----

        private sealed class MarkAttribute : Attribute { }

        private sealed class PlainMember : MemberInfo
        {
            private readonly object[] _attributes;
            public PlainMember(object[] attributes) => _attributes = attributes;
            public override Type? DeclaringType => null;
            public override MemberTypes MemberType => MemberTypes.Custom;
            public override string Name => "Plain";
            public override Type? ReflectedType => null;
            public override object[] GetCustomAttributes(bool inherit) => _attributes;
            public override object[] GetCustomAttributes(Type attributeType, bool inherit) => _attributes.Where(attributeType.IsInstanceOfType).ToArray();
            public override bool IsDefined(Type attributeType, bool inherit) => _attributes.Any(attributeType.IsInstanceOfType);
        }

        // A custom member returning a plain object[] made Attribute.GetCustomAttributes throw InvalidCastException.
        public void AttributeCache_PlainObjectArray_IsRead()
        {
            Check.False(AttributeCache.Has<MarkAttribute>(new PlainMember(new object[0])));
            Check.Equal(0, AttributeCache.GetAll<MarkAttribute>(new PlainMember(new object[0])).Count);
            var marked = new PlainMember(new object[] { new MarkAttribute() });
            Check.True(AttributeCache.Has<MarkAttribute>(marked));
            Check.Equal(1, AttributeCache.GetAll<MarkAttribute>(marked).Count);
            Check.NotNull(AttributeCache.Get<MarkAttribute>(marked));
        }

        // ---- ExpressionAccessor ----

        private sealed class Holder { public int I; public string? S { get; set; } public int? N { get; set; } }

        // A value or instance of the wrong type threw InvalidCastException; reflection throws ArgumentException.
        public void ExpressionAccessor_WrongTypes_ThrowArgumentException()
        {
            var set = ExpressionAccessor.Setter(typeof(Holder), "I");
            Check.Equal("value", Check.Throws<ArgumentException>(() => set(new Holder(), 5L)).ParamName);
            Check.Equal("value", Check.Throws<ArgumentException>(() => ExpressionAccessor.Setter(typeof(Holder), "S")(new Holder(), 3)).ParamName);
            Check.Equal("value", Check.Throws<ArgumentException>(() => ExpressionAccessor.Setter(typeof(Holder), "N")(new Holder(), "3")).ParamName);
            Check.Equal("instance", Check.Throws<ArgumentException>(() => set(new object(), 5)).ParamName);
            Check.Equal("instance", Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(Holder), "I")(new object())).ParamName);
            // Guards: the right types still work.
            var h = new Holder();
            set(h, 5);
            ExpressionAccessor.Setter(typeof(Holder), "N")(h, 6);
            Check.Equal(5, h.I);
            Check.Equal(6, h.N);
            Check.Equal(5, ExpressionAccessor.Getter(typeof(Holder), "I")(h));
        }
    }
}
