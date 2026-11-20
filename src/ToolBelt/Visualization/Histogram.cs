// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="Histogram.Render"/>.</summary>
    public sealed class HistogramOptions
    {
        /// <summary>Canvas background.</summary>
        public Rgba Background { get; set; } = Rgba.White;
        /// <summary>Bar fill color.</summary>
        public Rgba BarColor { get; set; } = new Rgba(70, 130, 180); // steel blue
        /// <summary>Frame color.</summary>
        public Rgba FrameColor { get; set; } = Rgba.Black;
        /// <summary>Pixels of margin between the frame and the image edge.</summary>
        public int Margin { get; set; } = 10;
        /// <summary>Whether to draw the plot frame.</summary>
        public bool DrawFrame { get; set; } = true;
        /// <summary>Explicit lower edge of the binning range (else the data minimum).</summary>
        public double? Min { get; set; }
        /// <summary>Explicit upper edge of the binning range (else the data maximum).</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// Bins one-dimensional data into equal-width buckets and renders the counts as a bar chart. The
    /// binning is exposed separately via <see cref="Bin"/> so the numbers can be used without drawing.
    /// </summary>
    public static class Histogram
    {
        /// <summary>
        /// Counts <paramref name="data"/> into <paramref name="bins"/> equal-width bins. Returns the bin
        /// edges (length <c>bins+1</c>) and counts (length <c>bins</c>). Values are placed in bin
        /// <c>i</c> when they fall in <c>[edge[i], edge[i+1])</c>; the maximum lands in the last bin. Interior edges are
        /// <c>lo + (hi − lo)·i/bins</c> worked exactly on the shortest decimals of the bounds and rounded once, so decimal
        /// edges (0.3, 93.35) are exactly the doubles that print that way.
        /// NaN values and values outside the range are ignored; infinities never set an automatic range. Explicit
        /// bounds must be finite with <paramref name="min"/> ≤ <paramref name="max"/>.
        /// </summary>
        public static (double[] Edges, int[] Counts) Bin(
            IReadOnlyList<double> data, int bins, double? min = null, double? max = null)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            // bins + 1 edges past Array.MaxLength threw OutOfMemoryException ("array dimensions exceeded").
            if (bins < 1 || bins >= 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(bins), bins, "Need between 1 and Array.MaxLength − 1 (0x7FFFFFC6) bins.");

            if (min.HasValue && (double.IsNaN(min.Value) || double.IsInfinity(min.Value)))
                throw new ArgumentOutOfRangeException(nameof(min), min, "Min must be finite.");
            if (max.HasValue && (double.IsNaN(max.Value) || double.IsInfinity(max.Value)))
                throw new ArgumentOutOfRangeException(nameof(max), max, "Max must be finite.");
            if (min > max) throw new ArgumentException("Min must not exceed Max.", nameof(min));

            double lo = min ?? double.PositiveInfinity, hi = max ?? double.NegativeInfinity;
            if (min is null || max is null)
            {
                foreach (double v in data)
                {
                    // ±∞ cannot set a range: one ∞ used to discard every finite value for [0, 1].
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (min is null && v < lo) lo = v;
                    if (max is null && v > hi) hi = v;
                }
            }
            if (double.IsInfinity(lo) || double.IsInfinity(hi)) // no finite data on an auto side
            {
                if (double.IsInfinity(lo) && double.IsInfinity(hi)) { lo = 0; hi = 1; }
                else if (double.IsInfinity(lo)) lo = hi - 1;
                else hi = lo + 1;
            }
            double keepLo = double.NegativeInfinity, keepHi = double.PositiveInfinity; // values counted, beyond [lo, hi]
            if (hi <= lo)
            {
                // One explicit bound at or past all the data stays, and the other side opens beyond it: centring moved
                // min = 5 to 4.5 and counted 4.7 and 4.8, both below it.
                if (min.HasValue && !max.HasValue) hi = Math.Min(double.MaxValue, lo + Math.Max(1, Math.Abs(lo) * 0.1));
                else if (max.HasValue && !min.HasValue) lo = Math.Max(-double.MaxValue, hi - Math.Max(1, Math.Abs(hi) * 0.1));
                else
                {
                    // Degenerate range. ±0.5 did nothing past 4.5e15 ({1e20, 1e20} gave five equal edges), and an explicit
                    // min = max = 5 counted 4.6 and 5.4: the bins open around the value but count only the value itself.
                    if (min.HasValue && max.HasValue) { keepLo = lo; keepHi = lo; }
                    double pad = Math.Max(0.5, Math.Abs(lo) * 1e-9);
                    hi = Math.Min(double.MaxValue, lo + pad);
                    lo = Math.Max(-double.MaxValue, lo - pad);
                }
            }

            var edges = new double[bins + 1];
            edges[0] = lo;
            // Each edge is lo + (hi − lo)·i/bins worked exactly on the decimals lo and hi print as, rounded once, so decimal
            // edges land where expected. In doubles (lo + span·i/bins) a quarter of them came out an ulp off: 70.9..115.8
            // in 12 bins gave 93.35000000000001, and -0.46 on the edge of [-0.5, -0.42] in 2 bins was counted in the bin
            // below. Exact, the edges are monotone and finite for any finite range (±double.MaxValue included).
            var (nLo, dLo) = DecimalRational(lo);
            var (nHi, dHi) = DecimalRational(hi);
            BigInteger wLo = nLo * dHi, wHi = nHi * dLo, den = dLo * dHi * bins;
            // Below 2^53 the numerator and denominator are exact doubles, and one IEEE division rounds correctly: the
            // BigInteger path took 300 ns an edge (3 s for ten million bins).
            BigInteger limit = BigInteger.One << 53;
            bool small = den < limit && BigInteger.Max(BigInteger.Abs(wLo), BigInteger.Abs(wHi)) * bins < limit;
            long sLo = small ? (long)wLo : 0, sHi = small ? (long)wHi : 0;
            double sDen = small ? (double)(long)den : 0;
            for (int i = 1; i < bins; i++)
            {
                double edge = small ? (sLo * (long)(bins - i) + sHi * (long)i) / sDen : RoundRational(wLo * (bins - i) + wHi * i, den);
                edges[i] = Math.Min(hi, Math.Max(edges[i - 1], edge));
            }
            edges[bins] = hi;

            // Scaled by bins first so ±1e308 ranges do not overflow; only a first guess at each value's bin.
            double width = hi / bins - lo / bins;
            var counts = new int[bins];
            foreach (double v in data)
            {
                if (double.IsNaN(v) || v < lo || v > hi || v < keepLo || v > keepHi) continue;
                // A NaN quotient (width 0) cast to int.MinValue and indexed below the array; start at 0 and settle.
                double q = Math.Floor(v / width - lo / width);
                int idx = double.IsNaN(q) ? 0 : (int)Math.Min(bins - 1, Math.Max(0, q));
                // Settle against the returned edges, so a value equal to edge[i] is always in bin i (the
                // quotient alone disagreed with the edges for values on them).
                // A guess far off (a subnormal or underflowed width) walked up to `bins` steps per value, n × bins in all, so
                // past a few steps the last bin whose lower edge is ≤ v is found by bisection.
                for (int step = 0; step < 4 && idx > 0 && v < edges[idx]; step++) idx--;
                for (int step = 0; step < 4 && idx < bins - 1 && v >= edges[idx + 1]; step++) idx++;
                if ((idx > 0 && v < edges[idx]) || (idx < bins - 1 && v >= edges[idx + 1]))
                {
                    int a = 0, b = bins - 1; // edges[a] ≤ v always holds (edges[0] = lo ≤ v)
                    while (a < b)
                    {
                        int mid = a + (b - a + 1) / 2;
                        if (edges[mid] <= v) a = mid; else b = mid - 1;
                    }
                    idx = a;
                }
                counts[idx]++;
            }
            return (edges, counts);
        }

        // x (finite) as numerator / denominator from its shortest round-trip decimal.
        private static (BigInteger Num, BigInteger Den) DecimalRational(double x)
        {
            if (x == 0) return (BigInteger.Zero, BigInteger.One);
            var (digits, q) = Shortest(Math.Abs(x));
            if (x < 0) digits = -digits;
            return q >= 0 ? (digits * BigInteger.Pow(10, q), BigInteger.One) : (digits, BigInteger.Pow(10, -q));
        }

        // num / den (den > 0) rounded once to the nearest double, ties to even.
        private static double RoundRational(BigInteger num, BigInteger den)
        {
            if (num.IsZero) return 0;
            bool negative = num.Sign < 0;
            num = BigInteger.Abs(num);
            int shift = 55 - (BitLength(num) - BitLength(den));      // a quotient of at least 54 bits
            BigInteger quotient = shift >= 0 ? BigInteger.DivRem(num << shift, den, out BigInteger rem)
                : BigInteger.DivRem(num, den << -shift, out rem);
            int bits = BitLength(quotient), exp = bits - 1 - shift;  // value in [2^exp, 2^(exp+1))
            int drop = bits - 53 + Math.Max(0, -1022 - exp);         // fewer bits kept for a subnormal
            BigInteger kept = quotient >> drop, rest = quotient - (kept << drop), half = BigInteger.One << (drop - 1);
            int cmp = rest.CompareTo(half);
            if (cmp > 0 || (cmp == 0 && (!rem.IsZero || !kept.IsEven))) kept++;
            double r = ScaleB((double)(long)kept, drop - shift);
            return negative ? -r : r;
        }

        private static int BitLength(BigInteger v)
        {
            byte[] bytes = v.ToByteArray(); // little-endian, positive
            int last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0) last--;
            int n = last * 8;
            for (int top = bytes[last]; top != 0; top >>= 1) n++;
            return n;
        }

        // x·2^k (exact when the result is representable; x ≤ 2^53, so no step underflows before the last).
        private static double ScaleB(double x, int k)
        {
            for (; k > 1000; k -= 1000) x *= Pow2(1000);
            for (; k < -1000; k += 1000) x *= Pow2(-1000);
            return k >= -1022 ? x * Pow2(k) : x * Pow2(-1000) * Pow2(k + 1000);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); // −1022 ≤ k ≤ 1023

        // The shortest decimal digits·10^exponent that reads back as x (positive and finite), found exactly with
        // BigInteger so every framework agrees (a private copy of Polynomial's).
        private static (BigInteger Digits, int Exponent) Shortest(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            int k = be - 1075;                                         // x = m·2^k
            bool tight = m == 1L << 52 && be > 1;                      // the gap below a power of two is half the one above
            int top = (int)Math.Floor(Math.Log10(x));
            for (int n = 1; n <= 18; n++)
            {
                int q = top - n + 1;                                   // the place of the last digit
                // Everything times A = 10^max(−q, 0)·2^max(2 − k, 0), so all terms are integers.
                BigInteger ten = BigInteger.Pow(10, Math.Max(-q, 0)), tenUp = BigInteger.Pow(10, Math.Max(q, 0));
                BigInteger xs = (BigInteger)m * BigInteger.Pow(2, Math.Max(k, 2)) * ten;
                BigInteger num = (BigInteger)m * BigInteger.Pow(2, Math.Max(k, 0)) * ten;
                BigInteger den = BigInteger.Pow(2, Math.Max(-k, 0)) * tenUp;
                BigInteger digits = BigInteger.DivRem(num, den, out BigInteger rem);
                int half = (2 * rem).CompareTo(den);
                if (half > 0 || (half == 0 && !digits.IsEven)) digits++;  // ties to even, as .NET Core rounds
                BigInteger lhs = digits * tenUp * BigInteger.Pow(2, Math.Max(2 - k, 0));
                BigInteger gap = lhs >= xs ? BigInteger.Pow(2, Math.Max(k - 1, 1)) * ten
                    : BigInteger.Pow(2, tight ? Math.Max(k - 2, 0) : Math.Max(k - 1, 1)) * ten;
                BigInteger diff = BigInteger.Abs(lhs - xs);
                if (diff < gap || (diff == gap && (m & 1) == 0)) return (digits, q);
            }
            throw new InvalidOperationException("Unreachable: 18 digits always read back.");
        }

        /// <summary>Bins <paramref name="data"/> and renders it as a bar chart.</summary>
        public static ImageBuffer Render(int width, int height, IReadOnlyList<double> data, int bins = 10, HistogramOptions? options = null)
        {
            options ??= new HistogramOptions();
            var image = new ImageBuffer(width, height, options.Background);

            int m = options.Margin;
            // A negative margin put the plot area outside the image (SetPixel threw "x", or int.MinValue overflowed).
            if (m < 0) throw new ArgumentOutOfRangeException(nameof(options), m, "Margin must not be negative.");
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.", nameof(options));
            // A bar needs a pixel column: 2e9 bins on a 100-pixel plot allocated 16 GB of edges to draw bars 0 wide.
            if (bins > right - left + 1) throw new ArgumentOutOfRangeException(nameof(bins), bins, $"More bins than the plot's {right - left + 1} pixel columns.");
            // Bin's own checks named its "min" and "max" parameters, which Render does not have.
            if (options.Min is double hmn && (double.IsNaN(hmn) || double.IsInfinity(hmn)) || options.Max is double hmx && (double.IsNaN(hmx) || double.IsInfinity(hmx)))
                throw new ArgumentOutOfRangeException(nameof(options), "Min and Max must be finite.");
            if (options.Min > options.Max) throw new ArgumentException("Min is above Max.", nameof(options));
            var (_, counts) = Bin(data, bins, options.Min, options.Max);

            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            int maxCount = 1;
            foreach (int c in counts) if (c > maxCount) maxCount = c;

            int plotWidth = right - left + 1, plotHeight = bottom - top + 1;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] <= 0) continue;
                int bx0 = left + (int)Math.Round((double)b / bins * plotWidth);
                int bx1 = left + (int)Math.Round((double)(b + 1) / bins * plotWidth);
                int bw = Math.Max(1, bx1 - bx0);
                int barH = (int)Math.Round((double)counts[b] / maxCount * plotHeight);
                if (barH > 0) image.DrawRectangle(bx0, bottom - barH + 1, bw, barH, options.BarColor, filled: true);
            }
            return image;
        }
    }
}
