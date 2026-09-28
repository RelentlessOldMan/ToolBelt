using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class CryptoRandomTests
    {
        public void BytesLengthAndVariation()
        {
            Check.Equal(32, CryptoRandom.Bytes(32).Length);
            Check.Equal(0, CryptoRandom.Bytes(0).Length);
            // Two draws should essentially never collide.
            Check.False(CryptoRandom.Bytes(32).SequenceEqual(CryptoRandom.Bytes(32)));
        }

        public void TokenIsUrlSafeAndCorrectLength()
        {
            for (int len = 1; len <= 64; len++)
            {
                string t = CryptoRandom.Token(len);
                Check.Equal(len, t.Length);
                foreach (char c in t)
                    Check.True(char.IsLetterOrDigit(c) || c == '-' || c == '_', $"non-url-safe char '{c}'");
            }
        }

        public void NumericCode()
        {
            string code = CryptoRandom.NumericCode(6);
            Check.Equal(6, code.Length);
            Check.True(code.All(char.IsDigit));
        }

        public void PasswordCoversRequestedClasses()
        {
            for (int i = 0; i < 200; i++)
            {
                string pw = CryptoRandom.Password(12, PasswordClasses.All);
                Check.Equal(12, pw.Length);
                Check.True(pw.Any(char.IsLower), "missing lowercase");
                Check.True(pw.Any(char.IsUpper), "missing uppercase");
                Check.True(pw.Any(char.IsDigit), "missing digit");
                Check.True(pw.Any(c => "!@#$%^&*()-_=+[]{}".IndexOf(c) >= 0), "missing symbol");
            }
        }

        public void PasswordSubsetOfClasses()
        {
            string pw = CryptoRandom.Password(10, PasswordClasses.Lower | PasswordClasses.Digit);
            Check.True(pw.All(c => char.IsLower(c) || char.IsDigit(c)));
        }

        public void UnbiasedNumericDistribution()
        {
            // Over many draws each digit 0-9 should appear with roughly equal frequency.
            var counts = new int[10];
            const int n = 50000;
            for (int i = 0; i < n; i++) counts[CryptoRandom.NumericCode(1)[0] - '0']++;
            double expected = n / 10.0;
            for (int d = 0; d < 10; d++)
                Check.True(Math.Abs(counts[d] - expected) < expected * 0.15, $"digit {d} skewed: {counts[d]}");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Bytes(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Token(0));
            Check.Throws<ArgumentException>(() => CryptoRandom.Password(8, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Password(2, PasswordClasses.All)); // too short for 4 classes
        }
    }
}
