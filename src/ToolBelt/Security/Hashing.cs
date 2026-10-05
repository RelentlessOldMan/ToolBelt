// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>
    /// Cryptographic hashing over bytes, strings (UTF-8) and streams, returning raw bytes with hex/base64
    /// formatters. Uses the framework's audited SHA-2 primitives. <see cref="Md5"/> is included only for
    /// legacy non-cryptographic checksums and is clearly marked as such.
    /// </summary>
    public static class Hashing
    {
        public static byte[] Sha256(byte[] data) => Compute(SHA256.Create(), data);
        public static byte[] Sha256(string text) => Sha256(Utf8(text));
        public static byte[] Sha256(Stream stream) => Compute(SHA256.Create(), stream);

        public static byte[] Sha384(byte[] data) => Compute(SHA384.Create(), data);
        public static byte[] Sha384(string text) => Sha384(Utf8(text));
        public static byte[] Sha384(Stream stream) => Compute(SHA384.Create(), stream);

        public static byte[] Sha512(byte[] data) => Compute(SHA512.Create(), data);
        public static byte[] Sha512(string text) => Sha512(Utf8(text));
        public static byte[] Sha512(Stream stream) => Compute(SHA512.Create(), stream);

        /// <summary>MD5 — NOT cryptographically secure. Use only for legacy checksums / interop, never for security.</summary>
        public static byte[] Md5(byte[] data) => Compute(MD5.Create(), data);

        /// <summary>SHA-256 of a file, streamed (any size).</summary>
        public static byte[] Sha256File(string path) => HashFile(SHA256.Create(), path);
        public static byte[] Sha512File(string path) => HashFile(SHA512.Create(), path);

        /// <summary>Lowercase hex SHA-256 of a file — the form <c>sha256sum</c> prints.</summary>
        public static string Sha256FileHex(string path) => ToHex(Sha256File(path));

        /// <summary>
        /// Hashes data that arrives in pieces (a socket, a capture loop, chunks you already have in memory) without
        /// buffering it: <c>using var h = Hashing.CreateIncremental(); h.Append(chunk); … h.GetHashAndReset()</c>.
        /// </summary>
        public static IncrementalHash CreateIncremental(HashAlgorithmName? algorithm = null)
            => IncrementalHash.CreateHash(algorithm ?? HashAlgorithmName.SHA256);

        private static byte[] HashFile(HashAlgorithm algorithm, string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using (algorithm)
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                return algorithm.ComputeHash(fs);
        }

        /// <summary>Lowercase hex encoding of a hash.</summary>
        public static string ToHex(byte[] hash)
        {
            if (hash is null) throw new ArgumentNullException(nameof(hash));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Base-64 encoding of a hash.</summary>
        public static string ToBase64(byte[] hash)
        {
            if (hash is null) throw new ArgumentNullException(nameof(hash));
            return Convert.ToBase64String(hash);
        }

        private static byte[] Compute(HashAlgorithm algorithm, byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            using (algorithm) return algorithm.ComputeHash(data);
        }

        private static byte[] Compute(HashAlgorithm algorithm, Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            using (algorithm) return algorithm.ComputeHash(stream);
        }

        private static byte[] Utf8(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
