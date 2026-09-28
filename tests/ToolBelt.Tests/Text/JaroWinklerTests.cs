using System;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class JaroWinklerTests
    {
        public void Identical_IsOne()
        {
            Check.Close(1.0, JaroWinkler.Jaro("abc", "abc"));
            Check.Close(1.0, JaroWinkler.Similarity("abc", "abc"));
            Check.Close(1.0, JaroWinkler.Jaro("", ""));
        }

        public void Disjoint_IsZero()
        {
            Check.Close(0.0, JaroWinkler.Jaro("abc", "xyz"));
            Check.Close(0.0, JaroWinkler.Similarity("abc", ""));
        }

        public void KnownJaroValues()
        {
            Check.Close(0.944444, JaroWinkler.Jaro("MARTHA", "MARHTA"), 1e-5);
            Check.Close(0.766667, JaroWinkler.Jaro("DIXON", "DICKSONX"), 1e-5);
            Check.Close(0.733333, JaroWinkler.Jaro("CRATE", "TRACE"), 1e-5);
        }

        public void KnownJaroWinklerValues()
        {
            Check.Close(0.961111, JaroWinkler.Similarity("MARTHA", "MARHTA"), 1e-5);
            Check.Close(0.813333, JaroWinkler.Similarity("DIXON", "DICKSONX"), 1e-5);
        }

        public void WinklerNeverBelowJaro()
        {
            var rng = new Random(7);
            const string palette = "abcde";
            for (int trial = 0; trial < 2000; trial++)
            {
                string a = Rand(rng, palette), b = Rand(rng, palette);
                double jaro = JaroWinkler.Jaro(a, b);
                double jw = JaroWinkler.Similarity(a, b);
                Check.True(jw >= jaro - 1e-12, $"trial {trial}: jw {jw} < jaro {jaro}");
                Check.True(jw >= -1e-12 && jw <= 1 + 1e-12, $"trial {trial}: out of range {jw}");
                Check.Close(jaro, JaroWinkler.Jaro(b, a), 1e-12, $"trial {trial}: symmetry");
            }
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => JaroWinkler.Jaro(null!, "x"));
            Check.Throws<ArgumentOutOfRangeException>(() => JaroWinkler.Similarity("a", "b", prefixScale: 0.5));
        }

        private static string Rand(Random rng, string palette)
        {
            int len = rng.Next(0, 8);
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = palette[rng.Next(palette.Length)];
            return new string(chars);
        }
    }
}
