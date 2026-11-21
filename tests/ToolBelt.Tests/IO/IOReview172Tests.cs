using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Round 172 review regressions (IO, tenth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class IOReview172Tests
    {
        // ---------- ByteSize ----------

        // The text's value times the unit, rounded half away from zero, straight from its digits.
        private static BigInteger ExactBytes(string digits, string fraction, BigInteger unit)
        {
            BigInteger n = BigInteger.Parse("0" + digits + fraction, CultureInfo.InvariantCulture) * unit;
            BigInteger d = BigInteger.Pow(10, fraction.Length);
            return (2 * n + d) / (2 * d);
        }

        // Through decimal, which keeps 28 or 29 digits, "0.49999999999999999999999999999999" became 0.5 and then 1 byte.
        public void ByteSize_Parse_RoundsOnceFromTheExactValue()
        {
            Check.Equal(0L, ByteSize.Parse("0.49999999999999999999999999999999"));
            Check.Equal(1L, ByteSize.Parse("1.4999999999999999999999999999999999 B"));
            Check.Equal(-1L, ByteSize.Parse("-0.5"));
            Check.Equal(long.MinValue, ByteSize.Parse("-9223372036854775808"));
            Check.False(ByteSize.TryParse("9223372036854775807.5", out _), "past long.MaxValue");
            var rng = new Random(172);
            string[] units = { "", "B", "KB", "KiB", "MB", "MiB", "GB", "GiB", "TB", "TiB", "PB", "PiB", "EB", "EiB" };
            for (int trial = 0; trial < 3000; trial++)
            {
                int u = rng.Next(units.Length);
                int power = u <= 1 ? 0 : (u - 2) / 2 + 1;
                BigInteger unit = BigInteger.Pow(u % 2 == 0 && u > 1 ? 1000 : 1024, power);
                if (u <= 1) unit = 1;
                string whole = rng.Next(4) == 0 ? "0" : rng.Next(1, 1000).ToString(CultureInfo.InvariantCulture);
                // Near a midpoint now and then: digits then a run of 9s or 0s past decimal's precision.
                var f = new StringBuilder();
                int len = rng.Next(0, 40);
                for (int i = 0; i < len; i++) f.Append((char)('0' + rng.Next(10)));
                if (rng.Next(2) == 0) f.Append(rng.Next(2) == 0 ? "5" : "4").Append(new string(rng.Next(2) == 0 ? '9' : '0', rng.Next(20, 40))).Append(rng.Next(2) == 0 ? "1" : "");
                string fraction = f.ToString();
                bool negative = rng.Next(4) == 0;
                string text = (negative ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "") + " " + units[u];
                BigInteger want = ExactBytes(whole, fraction, unit);
                bool ok = ByteSize.TryParse(text, out long got);
                if (want > long.MaxValue) { Check.False(ok, text); continue; }
                Check.True(ok, text);
                Check.Equal((long)(negative ? -want : want), got, text);
            }
        }

        // The exact quotient, rounded half away from zero, as text, by decimal arithmetic (exact for these divisors and
        // decimals: the quotient is held to 28 digits, and the trials stay clear of the 28th).
        private static string ReferenceFormat(long bytes, bool binary, int decimals)
        {
            decimal magnitude = Math.Abs((decimal)bytes), b = binary ? 1024m : 1000m;
            string[] units = binary ? new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" } : new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int unit = 0;
            decimal div = 1;
            while (unit < 6 && magnitude >= div * b) { div *= b; unit++; }
            string sign = bytes < 0 ? "-" : "";
            if (unit == 0) return sign + magnitude.ToString(CultureInfo.InvariantCulture) + " B";
            decimal v = Math.Round(magnitude / div, decimals, MidpointRounding.AwayFromZero);
            if (unit < 6 && v >= b) { div *= b; unit++; v = Math.Round(magnitude / div, decimals, MidpointRounding.AwayFromZero); }
            return sign + v.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + units[unit];
        }

        // In double, dividing by 1000 per unit put exact midpoints on either side, differently by runtime: 1.125 EB was
        // "1.12 EB" on .NET 8 and "1.13 EB" on .NET Framework; long.MaxValue with 15 decimals printed …773 (.NET 8) or …770
        // for 9.223372036854776 EB.
        public void ByteSize_Format_RoundsTheExactQuotient()
        {
            Check.Equal("1.13 EB", ByteSize.Format(1125000000000000000, binary: false));
            Check.Equal("2.68 TB", ByteSize.Format(2675000000000, binary: false));
            Check.Equal("9.223372036854776 EB", ByteSize.Format(long.MaxValue, binary: false, decimals: 15));
            Check.Equal("-8.00 EiB", ByteSize.Format(long.MinValue));
            Check.Equal("1.00 MiB", ByteSize.Format(1048575));
            // A binary quotient is a finite decimal, printed in full.
            Check.Equal("1.0009765625 KiB", ByteSize.Format(1025, binary: true, decimals: 10));
            var rng = new Random(1172);
            for (int trial = 0; trial < 5000; trial++)
            {
                bool binary = rng.Next(2) == 0;
                int decimals = rng.Next(0, 9);
                long bytes;
                if (rng.Next(3) == 0)
                {
                    // An exact midpoint: k + ½ at the last decimal, in a random unit.
                    int unit = rng.Next(1, 7);
                    decimal div = 1;
                    for (int i = 0; i < unit; i++) div *= binary ? 1024m : 1000m;
                    decimal step = 1;
                    for (int i = 0; i < decimals; i++) step /= 10;
                    decimal value = (rng.Next(1, 1000) * step + step / 2) * div;
                    if (value != Math.Floor(value) || value > long.MaxValue) continue;
                    bytes = (long)value;
                }
                else bytes = (long)(rng.NextDouble() * Math.Pow(2, rng.Next(0, 63)));
                if (rng.Next(4) == 0) bytes = -bytes;
                string want = ReferenceFormat(bytes, binary, decimals);
                Check.Equal(want, ByteSize.Format(bytes, binary, decimals), $"{bytes}, binary {binary}, {decimals} decimals");
            }
        }

        // ---------- CsvDialect ----------

        private static bool IsNegative(double v) => BitConverter.DoubleToInt64Bits(v) < 0;

        // .NET Framework's TryParse rejects a number past double's range, so ParseNumber threw FormatException there for
        // "1e400" where .NET Core gave infinity, and Detect did not count such a field as numeric.
        public void CsvDialect_NumbersPastTheDoubleRange_AreInfinityOrZeroOnEveryRuntime()
        {
            var rfc = CsvDialect.Rfc4180;
            Check.Equal(double.PositiveInfinity, rfc.ParseNumber("1e400"));
            Check.Equal(double.NegativeInfinity, rfc.ParseNumber("-1e400"));
            Check.Equal(double.PositiveInfinity, rfc.ParseNumber(" 1.7976931348623159e308 "));
            Check.Equal(double.PositiveInfinity, rfc.ParseNumber("1" + new string('0', 400)));
            Check.Equal(double.PositiveInfinity, rfc.ParseNumber("0.0001e313"));
            Check.Equal(double.PositiveInfinity, CsvDialect.ExcelEuropean.ParseNumber("-1,5e400") * -1);
            double tiny = rfc.ParseNumber("1e-400");
            Check.True(tiny == 0 && !IsNegative(tiny), "1e-400");
            Check.True(IsNegative(rfc.ParseNumber("-1e-400")), "-1e-400");
            Check.Equal(0.0, rfc.ParseNumber("0e99999999999"));
            Check.Equal(1.7976931348623157e308, rfc.ParseNumber("1.7976931348623157e308"));
            Check.Equal(4.9406564584124654e-324, rfc.ParseNumber("4.9406564584124654e-324"));
            foreach (string bad in new[] { "e400", "1e", "1e+", "1ee400", ".e5", "1e400x", "--1e400", "1.2.3e400" })
                Check.False(rfc.TryParseNumber(bad, out _), bad);
            Check.Equal(',', CsvDialect.Detect(new[] { "a;b", "1,5e400;x" }).DecimalSeparator);
            var rng = new Random(2172);
            for (int trial = 0; trial < 2000; trial++)
            {
                string mantissa = rng.Next(1, 10) + "." + rng.Next(0, 1000000).ToString("D6", CultureInfo.InvariantCulture);
                int exponent = rng.Next(300, 2000) * (rng.Next(2) == 0 ? 1 : -1);
                bool negative = rng.Next(2) == 0;
                string text = (negative ? "-" : "") + mantissa + "e" + exponent.ToString(CultureInfo.InvariantCulture);
                Check.True(rfc.TryParseNumber(text, out double got), text);
                if (exponent >= 309) Check.Equal(negative ? double.NegativeInfinity : double.PositiveInfinity, got, text);
                else if (exponent <= -325) Check.True(got == 0 && IsNegative(got) == negative, text);
                else
                {
                    // Graded against the exact value: .NET Framework's Parse is not correctly rounded (round 195).
                    var digits = System.Numerics.BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture);
                    Check.True(IOReview195Tests.IsNearest(negative ? -got : got, digits, exponent - 6, 52, -1074), text);
                }
            }
        }

#if NET8_0_OR_GREATER
        // ---------- TarUtils ----------

        // TarReader returns a PAX global header as an entry of its own, but the metadata guard counted it with the next
        // run, so a "g" then a GNU long name was refused as "a mix of GNU and PAX metadata headers".
        public void Tar_PaxGlobalHeaderThenAGnuLongName_IsRead()
        {
            using var tmp = new TempDirectory();
            string tar = Path.Combine(tmp.Path, "global.tar");
            byte[] global = Record("comment", "hi"), longName = Encoding.ASCII.GetBytes("long-name.txt\0");
            File.WriteAllBytes(tar, Cat(Header("pax_global_header", 'g', global.Length), Pad(global),
                Header("././@LongLink", 'L', longName.Length), Pad(longName),
                Header("short", '0', 5), Pad(Encoding.ASCII.GetBytes("hello")), new byte[1024]));
            Check.Equal("long-name.txt", string.Join(",", TarUtils.ListEntries(tar)));
            string dest = Path.Combine(tmp.Path, "out");
            TarUtils.ExtractToDirectory(tar, dest);
            Check.Equal("hello", File.ReadAllText(Path.Combine(dest, "long-name.txt")));
            // A GNU long name, then a PAX x, before one entry is still refused.
            File.WriteAllBytes(tar, Cat(Header("././@LongLink", 'L', longName.Length), Pad(longName),
                Header("pax", 'x', global.Length), Pad(global), Header("short", '0', 5), Pad(Encoding.ASCII.GetBytes("hello")), new byte[1024]));
            Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
        }

        private static byte[] Header(string name, char type, long size)
        {
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
            Encoding.ASCII.GetBytes("00000000000\0").CopyTo(h, 136);
            h[156] = (byte)type;
            Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(h, 257);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            int sum = h.Sum(b => b);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            return h;
        }

        private static byte[] Pad(byte[] d)
        {
            var r = new byte[(d.Length + 511) / 512 * 512];
            d.CopyTo(r, 0);
            return r;
        }

        // A PAX record, "<length> <key>=<value>\n", whose length counts its own digits.
        private static byte[] Record(string key, string value)
        {
            string body = " " + key + "=" + value + "\n";
            int length = body.Length + 1;
            while (length != body.Length + length.ToString(CultureInfo.InvariantCulture).Length) length++;
            return Encoding.ASCII.GetBytes(length + body);
        }

        private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
#endif
    }
}
