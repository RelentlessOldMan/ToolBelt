// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>
    /// Fixed-time equality comparison, so comparing secrets (MACs, tokens, password hashes) does not leak
    /// how many leading bytes matched via timing. Lives in exactly one audited place rather than being
    /// re-reasoned at each call site. Note: the <b>length</b> of the inputs is not treated as secret.
    /// </summary>
    public static class ConstantTime
    {
        /// <summary>True if the two byte spans are equal, in time independent of where they first differ.</summary>
        public static bool Equals(byte[] a, byte[] b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>True if the two strings are equal (compared as UTF-8 bytes) in fixed time.</summary>
        public static bool Equals(string a, string b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            return Equals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }
    }
}
