using System;
using System.IO;
using System.Text;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class HashingTests
    {
        // Published NIST/FIPS test vectors.
        public void Sha256KnownVectors()
        {
            Check.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hashing.ToHex(Hashing.Sha256("abc")));
            Check.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Hashing.ToHex(Hashing.Sha256("")));
        }

        public void Sha512KnownVector()
        {
            Check.Equal(
                "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a" +
                "2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
                Hashing.ToHex(Hashing.Sha512("abc")));
        }

        public void StreamMatchesBytes()
        {
            var data = Encoding.UTF8.GetBytes("the quick brown fox");
            using var ms = new MemoryStream(data);
            Check.Equal(Hashing.ToHex(Hashing.Sha256(data)), Hashing.ToHex(Hashing.Sha256(ms)));
        }

        public void HexAndBase64LengthsForSha256()
        {
            byte[] h = Hashing.Sha256("x");
            Check.Equal(32, h.Length);
            Check.Equal(64, Hashing.ToHex(h).Length);
            Check.True(h.AsSpan().SequenceEqual(Convert.FromBase64String(Hashing.ToBase64(h))));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Hashing.Sha256((byte[])null!));
            Check.Throws<ArgumentNullException>(() => Hashing.Sha256((string)null!));
            Check.Throws<ArgumentNullException>(() => Hashing.ToHex(null!));
        }
    }
}
