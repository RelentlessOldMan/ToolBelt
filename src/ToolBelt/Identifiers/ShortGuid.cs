// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Identifiers
{
    /// <summary>
    /// Encodes a <see cref="Guid"/> as a compact 22-character URL-safe string (base64url of the 16 raw
    /// bytes, no padding) and back. This is a lossless, reversible re-encoding of an existing Guid — for
    /// generating fresh compact ids see <c>NanoId</c> or <c>Ulid</c>.
    /// </summary>
    public static class ShortGuid
    {
        /// <summary>The fixed length of an encoded value.</summary>
        public const int Length = 22;

        public static string Encode(Guid guid)
        {
            string base64 = Convert.ToBase64String(guid.ToByteArray()); // 24 chars incl. trailing "=="
            return base64.Substring(0, Length).Replace('+', '-').Replace('/', '_');
        }

        public static Guid Decode(string shortGuid)
        {
            if (shortGuid is null) throw new ArgumentNullException(nameof(shortGuid));
            if (shortGuid.Length != Length)
                throw new FormatException($"A short GUID must be exactly {Length} characters.");

            string base64 = shortGuid.Replace('-', '+').Replace('_', '/') + "==";
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"'{shortGuid}' is not a valid short GUID.", ex);
            }
            if (bytes.Length != 16)
                throw new FormatException($"'{shortGuid}' does not decode to 16 bytes.");
            var guid = new Guid(bytes);
            // Only the exact text Encode produces is accepted: no '+' or '/', and no stray bits in the last character,
            // so each GUID has one short form and the strings can be compared or used as keys.
            if (!string.Equals(Encode(guid), shortGuid, StringComparison.Ordinal))
                throw new FormatException($"'{shortGuid}' is not a canonical short GUID.");
            return guid;
        }

        public static bool TryDecode(string shortGuid, out Guid guid)
        {
            try
            {
                guid = Decode(shortGuid);
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentNullException)
            {
                guid = Guid.Empty;
                return false;
            }
        }
    }
}
