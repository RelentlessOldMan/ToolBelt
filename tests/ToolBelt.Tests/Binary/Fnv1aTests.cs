using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Fnv1aTests
    {
        public void KnownVectors32()
        {
            Check.Equal(0x811C9DC5u, Fnv1a.Hash32(""));       // offset basis
            Check.Equal(0xE40C292Cu, Fnv1a.Hash32("a"));
            Check.Equal(0xBF9CF968u, Fnv1a.Hash32("foobar"));
        }

        public void KnownVectors64()
        {
            Check.Equal(0xCBF29CE484222325uL, Fnv1a.Hash64(""));
            Check.Equal(0x85944171F73967E8uL, Fnv1a.Hash64("foobar"));
        }

        public void StringEqualsUtf8Bytes()
        {
            var rng = new Random(99);
            for (int trial = 0; trial < 500; trial++)
            {
                int len = rng.Next(0, 30);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append((char)('a' + rng.Next(0, 26)));
                string s = sb.ToString();

                Check.Equal(Fnv1a.Hash32(Encoding.UTF8.GetBytes(s)), Fnv1a.Hash32(s), $"trial {trial}: 32");
                Check.Equal(Fnv1a.Hash64(Encoding.UTF8.GetBytes(s)), Fnv1a.Hash64(s), $"trial {trial}: 64");
            }
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Fnv1a.Hash32((byte[])null!));
            Check.Throws<ArgumentNullException>(() => Fnv1a.Hash64((string)null!));
        }
    }
}
