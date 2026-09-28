using System;
using System.Collections.Generic;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Identifiers
{
    public sealed class NanoIdTests
    {
        public void DefaultSizeAndAlphabet()
        {
            var gen = new NanoId(new Random(1));
            string id = gen.New();
            Check.Equal(21, id.Length);
            foreach (char c in id)
                Check.True(NanoId.UrlSafeAlphabet.IndexOf(c) >= 0, $"'{c}' not URL-safe");
        }

        public void CustomSize()
        {
            var gen = new NanoId(new Random(1), defaultSize: 8);
            Check.Equal(8, gen.New().Length);
            Check.Equal(30, gen.New(30).Length);
        }

        public void CustomAlphabet()
        {
            var gen = new NanoId(new Random(1), alphabet: "AB");
            string id = gen.New(50);
            foreach (char c in id)
                Check.True(c == 'A' || c == 'B', $"unexpected char '{c}'");
        }

        public void DeterministicWithSeed()
        {
            var a = new NanoId(new Random(42));
            var b = new NanoId(new Random(42));
            Check.Equal(a.New(), b.New()); // same seed -> same id
        }

        public void GeneratesDistinctIds()
        {
            var gen = new NanoId(new Random(7));
            var seen = new HashSet<string>();
            for (int i = 0; i < 10000; i++)
                Check.True(seen.Add(gen.New()), "collision in 10k default-size ids");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new NanoId(alphabet: "X"));           // too short
            Check.Throws<ArgumentOutOfRangeException>(() => new NanoId(defaultSize: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new NanoId(new Random(1)).New(0));
        }
    }
}
