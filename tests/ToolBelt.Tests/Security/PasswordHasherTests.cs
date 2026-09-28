using System;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class PasswordHasherTests
    {
        public void HashThenVerify()
        {
            string encoded = PasswordHasher.Hash("correct horse", iterations: 10_000);
            Check.True(PasswordHasher.Verify("correct horse", encoded));
            Check.False(PasswordHasher.Verify("wrong horse", encoded));
        }

        public void EncodedFormat()
        {
            string encoded = PasswordHasher.Hash("pw", iterations: 12345);
            string[] parts = encoded.Split('$');
            Check.Equal(4, parts.Length);
            Check.Equal("PBKDF2-SHA256", parts[0]);
            Check.Equal("12345", parts[1]);
        }

        public void SaltMakesEachHashUnique()
        {
            // Same password hashed twice yields different strings (random per-hash salt).
            Check.False(PasswordHasher.Hash("same", 10_000) == PasswordHasher.Hash("same", 10_000));
        }

        public void NeedsRehashDetectsWeakerParameters()
        {
            string weak = PasswordHasher.Hash("pw", iterations: 5_000);
            Check.True(PasswordHasher.NeedsRehash(weak, currentIterations: 10_000));
            Check.False(PasswordHasher.NeedsRehash(weak, currentIterations: 5_000));
            Check.False(PasswordHasher.NeedsRehash(weak, currentIterations: 1_000));
        }

        public void MalformedEncoded_Throws()
        {
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", "not-an-encoded-hash"));
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", "PBKDF2-SHA256$abc$c2FsdA==$aGFzaA=="));
            Check.Throws<FormatException>(() => PasswordHasher.NeedsRehash("garbage"));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => PasswordHasher.Hash(null!));
            Check.Throws<ArgumentNullException>(() => PasswordHasher.Verify(null!, "x"));
        }
    }
}
