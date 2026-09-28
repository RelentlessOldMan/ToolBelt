using System;
using System.Text;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class HmacTests
    {
        // RFC 4231 Test Case 2.
        public void HmacSha256KnownVector()
        {
            byte[] key = Encoding.ASCII.GetBytes("Jefe");
            byte[] mac = Hmac.Sha256(key, "what do ya want for nothing?");
            Check.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
                Hashing.ToHex(mac));
        }

        public void VerifyAcceptsCorrectAndRejectsTampered()
        {
            byte[] key = Encoding.ASCII.GetBytes("secret-key");
            byte[] data = Encoding.ASCII.GetBytes("message");
            byte[] mac = Hmac.Sha256(key, data);

            Check.True(Hmac.Verify(key, data, mac));

            var tampered = (byte[])mac.Clone();
            tampered[0] ^= 0xFF;
            Check.False(Hmac.Verify(key, data, tampered));
            Check.False(Hmac.Verify(key, Encoding.ASCII.GetBytes("message!"), mac)); // changed data
        }

        public void DifferentKeysProduceDifferentMacs()
        {
            byte[] data = Encoding.ASCII.GetBytes("same message");
            Check.False(Hashing.ToHex(Hmac.Sha256(new byte[] { 1 }, data))
                == Hashing.ToHex(Hmac.Sha256(new byte[] { 2 }, data)));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Hmac.Sha256(null!, new byte[0]));
            Check.Throws<ArgumentNullException>(() => Hmac.Sha256(new byte[1], (byte[])null!));
        }
    }
}
