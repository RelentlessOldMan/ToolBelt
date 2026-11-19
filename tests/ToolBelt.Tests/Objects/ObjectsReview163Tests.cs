using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 163 (objects, functional, grids, graphs, intervals, tenth pass): each failed on the code before its fix.
    public sealed class ObjectsReview163Tests
    {
        private const int Trials = 400;

        public sealed class H<K> where K : notnull
        {
            public Dictionary<K, int> D { get; set; } = new Dictionary<K, int>();
        }

        // Every path Flatten prints reads back its own value through PropertyPath, and Set through it writes that entry.
        private static void RoundTrip<K>(IEnumerable<K> keys) where K : notnull
        {
            var h = new H<K>();
            int n = 0;
            foreach (K k in keys)
                if (!h.D.ContainsKey(k)) h.D[k] = ++n;
            IDictionary<string, object?> flat = FlattenObject.Flatten(h);
            Check.Equal(h.D.Count, flat.Count, "one path per key");
            foreach (KeyValuePair<string, object?> kv in flat)
            {
                Check.True(PropertyPath.TryGet(h, kv.Key, out object? v), "TryGet " + kv.Key);
                Check.Equal(kv.Value, v, kv.Key);
            }
            var copy = new H<K> { D = new Dictionary<K, int>(h.D) };
            foreach (KeyValuePair<string, object?> kv in flat) PropertyPath.Set(copy, kv.Key, (int)kv.Value! + 1000000);
            Check.Equal(h.D.Count, copy.D.Count, "Set added no key");
            foreach (KeyValuePair<K, int> kv in h.D) Check.Equal(kv.Value + 1000000, copy.D[kv.Key], "Set " + kv.Key);
        }

        private static IEnumerable<T> Make<T>(int seed, Func<Random, T> next)
        {
            var rng = new Random(seed);
            for (int i = 0; i < Trials; i++) yield return next(rng);
        }

        private static double AnyDouble(Random rng)
        {
            switch (rng.Next(6))
            {
                case 0: return rng.Next(-1000, 1000);
                case 1: return -0.0;
                case 2: return double.NaN;
                case 3: return rng.Next(2) == 0 ? double.PositiveInfinity : double.NegativeInfinity;
                default:
                    var b = new byte[8];
                    rng.NextBytes(b);
                    double d = BitConverter.ToDouble(b, 0);
                    return double.IsNaN(d) ? 1.5 : d;
            }
        }

        private static string AnyText(Random rng)
        {
            const string alphabet = "abcXYZ019 .-_'";
            var c = new char[rng.Next(0, 6)];
            for (int i = 0; i < c.Length; i++) c[i] = alphabet[rng.Next(alphabet.Length)];
            return new string(c);
        }

        private static BigInteger AnyBig(Random rng)
        {
            var b = new byte[rng.Next(1, 40)];
            rng.NextBytes(b);
            return new BigInteger(b);
        }

        // ---- C1: a key type that is not IConvertible printed a path PropertyPath could not read ----

        public void PropertyPath_BigIntegerKeysReadBack() => RoundTrip(Make(1, AnyBig));

        public void PropertyPath_ValueTupleKeysReadBack()
        {
            RoundTrip(Make(2, r => (r.Next(-50, 50), AnyDouble(r))));
            RoundTrip(Make(3, r => (AnyText(r), r.Next(), AnyBig(r))));
            RoundTrip(Make(4, r => ((r.Next(9), r.Next(9)), (long)r.Next())));
            RoundTrip(Make(5, r => (r.Next(3), r.Next(3), r.Next(3), r.Next(3), r.Next(3), r.Next(3), r.Next(3), r.Next(3), r.Next(3))));
        }

        public void PropertyPath_TupleKeysReadBack() => RoundTrip(Make(6, r => Tuple.Create(AnyText(r), r.Next(100))));

#if NET8_0_OR_GREATER
        public void PropertyPath_Net8KeyTypesReadBack()
        {
            RoundTrip(Make(7, r => DateOnly.FromDayNumber(r.Next(DateOnly.MaxValue.DayNumber + 1))));
            RoundTrip(Make(8, r => new TimeOnly((long)(r.NextDouble() * TimeOnly.MaxValue.Ticks))));
            RoundTrip(Make(9, r =>
            {
                Half h = BitConverter.Int16BitsToHalf((short)r.Next(short.MinValue, short.MaxValue + 1));
                return Half.IsNaN(h) ? (Half)1 : h;
            }));
            RoundTrip(Make(10, r => new Int128((ulong)r.NextInt64(), (ulong)r.NextInt64())));
            RoundTrip(Make(11, r => new UInt128((ulong)r.NextInt64(), (ulong)r.NextInt64())));
            RoundTrip(Make(12, r => (nint)r.NextInt64(long.MinValue, long.MaxValue)));
            RoundTrip(Make(13, r => (nuint)(ulong)r.NextInt64()));
            RoundTrip(new[] { Int128.MinValue, Int128.MaxValue, Int128.Zero });
            RoundTrip(new[] { Half.MinValue, Half.MaxValue, Half.Epsilon, Half.NegativeInfinity, Half.PositiveInfinity });
        }

        // ---- C2: a TimeOnly key printed without its seconds, so two keys spelled one path ----

        public void Flatten_TimeOnlyKeysKeepTheirSeconds()
        {
            var h = new H<TimeOnly>();
            h.D[new TimeOnly(3, 4, 5)] = 1;
            h.D[new TimeOnly(3, 4, 6)] = 2;
            h.D[new TimeOnly(3, 4, 6, 7)] = 3;
            IDictionary<string, object?> flat = FlattenObject.Flatten(h);
            Check.Equal(3, flat.Count);
            var other = new H<TimeOnly> { D = new Dictionary<TimeOnly, int>(h.D) };
            other.D[new TimeOnly(3, 4, 6)] = 9;
            var diffs = PropertyDiff.Compare(h, other);
            Check.Equal(1, diffs.Count);
            Check.True(PropertyPath.TryGet(other, diffs[0].Path, out object? v), diffs[0].Path);
            Check.Equal((object)9, v, diffs[0].Path);
        }
#endif

        // ---- R1: a key that needed quotes and held both quote characters was left bare, naming another key ----

        private static string QuoteText(Random rng)
        {
            const string alphabet = "a\"' ].b[";
            var c = new char[rng.Next(0, 7)];
            for (int i = 0; i < c.Length; i++) c[i] = alphabet[rng.Next(alphabet.Length)];
            return new string(c);
        }

        public void PropertyPath_KeysHoldingBothQuotesReadBack()
        {
            RoundTrip(Make(14, QuoteText));
            RoundTrip(new[] { "\"it's\"", "it's", "'a\"", "\"", "'", "\"\"", "''", " \"'", "']\"", "a\"\"b'" });
        }

        public void Unflatten_KeysHoldingBothQuotesRebuild()
        {
            var h = new H<string>();
            int n = 0;
            foreach (string k in Make(15, QuoteText))
                if (!h.D.ContainsKey(k)) h.D[k] = ++n;
            h.D["\"it's\""] = -1;
            h.D["it's"] = -2;
            Dictionary<string, object?> back = UnflattenObject.Unflatten(FlattenObject.Flatten(h));
            var d = (IDictionary<string, object?>)back["D"]!;
            Check.Equal(h.D.Count, d.Count, "one entry per key");
            foreach (KeyValuePair<string, int> kv in h.D)
            {
                Check.True(d.TryGetValue(kv.Key, out object? v), "missing [" + kv.Key + "]");
                Check.Equal((object)kv.Value, v, kv.Key);
            }
        }

        public void PropertyDiff_KeysHoldingBothQuotesNameTheirOwnEntry()
        {
            var a = new H<string>();
            a.D["\"it's\""] = 1;
            a.D["it's"] = 99;
            var b = new H<string> { D = new Dictionary<string, int>(a.D) };
            b.D["\"it's\""] = 2;
            var diffs = PropertyDiff.Compare(a, b);
            Check.Equal(1, diffs.Count);
            Check.True(PropertyPath.TryGet(b, diffs[0].Path, out object? v), diffs[0].Path);
            Check.Equal((object)2, v, diffs[0].Path);
        }

        // ---- R2: TryConvert from double to float accepted a value the float does not hold ----

        // Accepted: a double the float holds, or one the float's shortest text names (0.1, 1e30, as decimal 0.1m converts).
        public void TypeUtils_DoubleToFloatRefusesWhatAFloatDoesNotHold()
        {
#if NETCOREAPP
            // .NET Core's float "R" is the shortest text and its double.Parse is correctly rounded: the reference.
            var rng = new Random(16);
            for (int i = 0; i < 40000; i++)
            {
                double d;
                switch (i % 5)
                {
                    case 0: d = rng.NextDouble(); break;
                    case 1: d = (float)(rng.NextDouble() * 1e6); break;
                    case 2: d = rng.Next(-(1 << 26), 1 << 26); break;
                    case 3: d = double.Parse(((float)(rng.NextDouble() * Math.Pow(10, rng.Next(-40, 38)))).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); break;
                    default: d = Math.Round(rng.NextDouble(), rng.Next(1, 10)) * Math.Pow(10, rng.Next(-30, 30)); break;
                }
                float f = (float)d;
                bool holds = !float.IsInfinity(f) && (f != 0 || d == 0)
                    && ((double)f == d || double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) == d);
                bool ok = TypeUtils.TryConvert(d, typeof(float), out object? r);
                Check.Equal(holds, ok, d.ToString("R", CultureInfo.InvariantCulture));
                if (ok) Check.Equal((object)f, r);
            }
#endif
            Check.False(TypeUtils.TryConvert(0.123456789012, typeof(float), out _));
            Check.False(TypeUtils.TryConvert(16777217.0, typeof(float), out _));
            Check.False(TypeUtils.TryConvert(0.1 + 0.2, typeof(float), out _));
            Check.False(TypeUtils.TryConvert(1.00000001, typeof(float), out _));
            Check.True(TypeUtils.TryConvert(1.0000001, typeof(float), out _));
            Check.True(TypeUtils.TryConvert(0.1, typeof(float), out object? tenth) && (float)tenth! == 0.1f);
            Check.True(TypeUtils.TryConvert(0.12345678, typeof(float), out object? eight) && (float)eight! == 0.12345678f);
            Check.True(TypeUtils.TryConvert(1.1754944e-38, typeof(float), out _));
            Check.True(TypeUtils.TryConvert(1e-45, typeof(float), out object? sub) && (float)sub! == float.Epsilon);
            Check.True(TypeUtils.TryConvert(3.4028235e38, typeof(float), out object? max) && (float)max! == float.MaxValue);
            Check.True(TypeUtils.TryConvert(-0.3, typeof(float), out object? neg) && (float)neg! == -0.3f);
            Check.True(TypeUtils.TryConvert(double.NaN, typeof(float), out object? nan) && float.IsNaN((float)nan!));
            Check.True(TypeUtils.TryConvert(double.NegativeInfinity, typeof(float), out object? inf) && float.IsNegativeInfinity((float)inf!));
            Check.True(TypeUtils.TryConvert(-0.0, typeof(float), out object? z) && float.IsNegativeInfinity(1 / (float)z!));
        }

        // ---- R4: a struct without its own Equals compared by bits on .NET Framework ----

        public struct PrivD
        {
            private readonly double _x;
            public PrivD(double x) { _x = x; }
        }

        public struct FmtD : IFormattable
        {
            public double X;
            public string ToString(string? format, IFormatProvider? provider) => X.ToString(format, provider);
        }

        public sealed class Box
        {
            public FmtD F { get; set; }
        }

        private static double Nan(long payload) => BitConverter.Int64BitsToDouble(0x7FF8000000000000L | payload);

        public void DeepEquals_StructsWithoutEqualsCompareAsOnDotNetCore()
        {
            Check.True(DeepEquals.Equals(new PrivD(0.0), new PrivD(-0.0)), "PrivD 0 / -0");
            Check.True(DeepEquals.Equals(new PrivD(Nan(1)), new PrivD(Nan(2))), "PrivD NaNs");
            Check.False(DeepEquals.Equals(new PrivD(1.0), new PrivD(2.0)), "PrivD 1 / 2");
            Check.True(DeepEquals.Equals(new FmtD { X = 0.0 }, new FmtD { X = -0.0 }), "FmtD 0 / -0");
            Check.True(DeepEquals.Equals(new FmtD { X = Nan(3) }, new FmtD { X = Nan(4) }), "FmtD NaNs");
            Check.False(DeepEquals.Equals(new FmtD { X = 1.0 }, new FmtD { X = 2.0 }), "FmtD 1 / 2");
            Check.Equal(0, PropertyDiff.Compare(new Box { F = new FmtD { X = 0.0 } }, new Box { F = new FmtD { X = -0.0 } }).Count);
            Check.Equal(0, PropertyDiff.Compare(new PrivD(Nan(1)), new PrivD(Nan(5))).Count);
            Check.Equal(1, PropertyDiff.Compare(new Box { F = new FmtD { X = 1.0 } }, new Box { F = new FmtD { X = 2.0 } }).Count);
        }

        // ---- R5: "-NaN" was NaN on .NET Core and refused on .NET Framework ----

        public void TypeUtils_SignedNaNIsNaNEverywhere()
        {
            foreach (string s in new[] { "-NaN", "+NaN", " -NaN ", "NaN" })
            {
                Check.True(TypeUtils.TryConvert(s, typeof(double), out object? d) && double.IsNaN((double)d!), s + " double");
                Check.True(TypeUtils.TryConvert(s, typeof(float), out object? f) && float.IsNaN((float)f!), s + " float");
                Check.False(TypeUtils.TryConvert(s, typeof(decimal), out _), s + " decimal");
            }
        }

        // A tuple key printed its items with the current culture: (1.5, 2) was "(1,5, 2)" in German.
        public void Flatten_TupleKeysPrintInvariant()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var h = new H<(double, int)>();
                h.D[(1.5, 2)] = 1;
                IDictionary<string, object?> flat = FlattenObject.Flatten(h);
                Check.True(flat.ContainsKey("D[(1.5, 2)]"), string.Join(" | ", flat.Keys));
                Check.True(PropertyPath.TryGet(h, "D[(1.5, 2)]", out object? v));
                Check.Equal((object)1, v);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }
    }
}
