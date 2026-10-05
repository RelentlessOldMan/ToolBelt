// ToolBelt drop-in — fully self-contained (BCL only; net8+ for AesGcm and one-shot PBKDF2).
#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.Security
{
    /// <summary>
    /// A small encrypted key/value store in one file — connection strings, API tokens, instrument passwords — protected by
    /// a passphrase (PBKDF2-SHA256, 600 000 iterations by default) and AES-256-GCM, so the file is both confidential and
    /// tamper-evident: a wrong passphrase or a modified byte is a <see cref="CryptographicException"/>, never garbage
    /// values. Each save uses a fresh salt and nonce and is written atomically. Format:
    /// <c>"TBSECRETS1" | iterations (int32 LE) | salt(16) | nonce(12) | tag(16) | ciphertext</c>, where the plaintext is
    /// UTF-8 <c>key=base64(value)</c> lines and the header is authenticated as associated data. Keys are case-sensitive and
    /// may not contain '=' or line breaks.
    /// </summary>
    public sealed class SecretsFile
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TBSECRETS1");
        private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;
        public const int DefaultIterations = 600_000;

        // Upper bound accepted from a file header, so a tampered header can't make Load run an enormous KDF before the tag check.
        private const int MaxIterations = 10_000_000;

        private readonly SortedDictionary<string, string> _values = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _values.Keys;
        public int Count => _values.Count;

        public string this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"No secret named '{key}'.");
            set => Set(key, value);
        }

        public bool TryGet(string key, out string? value)
        {
            bool ok = _values.TryGetValue(key ?? throw new ArgumentNullException(nameof(key)), out var v);
            value = v;
            return ok;
        }

        public bool Contains(string key) => _values.ContainsKey(key ?? throw new ArgumentNullException(nameof(key)));

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key is required.", nameof(key));
            if (key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0) throw new ArgumentException("Keys may not contain '=' or line breaks.", nameof(key));
            _values[key] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool Remove(string key) => _values.Remove(key ?? throw new ArgumentNullException(nameof(key)));

        /// <summary>Encrypts to bytes (fresh salt and nonce every call).</summary>
        public byte[] ToBytes(string passphrase, int iterations = DefaultIterations)
        {
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("A passphrase is required.", nameof(passphrase));
            if (iterations < 1000 || iterations > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Use 1000 to 10,000,000 iterations.");
            var plain = new StringBuilder();
            foreach (var kv in _values) plain.Append(kv.Key).Append('=').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value))).Append('\n');
            byte[] plaintext = Encoding.UTF8.GetBytes(plain.ToString());

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] header = Header(iterations, salt, nonce);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeySize);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plaintext, cipher, tag, header);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
            return header.Concat(tag).Concat(cipher).ToArray();
        }

        /// <summary>Decrypts; a wrong passphrase, truncation or tampering throws <see cref="CryptographicException"/>.</summary>
        public static SecretsFile FromBytes(byte[] data, string passphrase)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("A passphrase is required.", nameof(passphrase));
            int headerSize = Magic.Length + 4 + SaltSize + NonceSize;
            if (data.Length < headerSize + TagSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw new CryptographicException("Not a secrets file (bad header).");
            int iterations = BitConverter.ToInt32(data, Magic.Length);
            if (!BitConverter.IsLittleEndian) iterations = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(iterations);
            if (iterations < 1000 || iterations > MaxIterations) throw new CryptographicException("Not a secrets file (implausible iteration count).");
            byte[] salt = data.AsSpan(Magic.Length + 4, SaltSize).ToArray();
            byte[] nonce = data.AsSpan(Magic.Length + 4 + SaltSize, NonceSize).ToArray();
            byte[] header = data.AsSpan(0, headerSize).ToArray();
            byte[] tag = data.AsSpan(headerSize, TagSize).ToArray();
            byte[] cipher = data.AsSpan(headerSize + TagSize).ToArray();

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeySize);
            var plaintext = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                try { aes.Decrypt(nonce, cipher, tag, plaintext, header); }
                catch (CryptographicException ex) { throw new CryptographicException("Wrong passphrase, or the secrets file has been modified.", ex); }
                var result = new SecretsFile();
                foreach (string line in Encoding.UTF8.GetString(plaintext).Split('\n'))
                {
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    result._values[line.Substring(0, eq)] = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(eq + 1)));
                }
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>Encrypts and writes atomically.</summary>
        public void Save(string path, string passphrase, int iterations = DefaultIterations)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            byte[] bytes = ToBytes(passphrase, iterations);
            string full = Path.GetFullPath(path);
            string temp = full + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static SecretsFile Load(string path, string passphrase) => FromBytes(File.ReadAllBytes(path ?? throw new ArgumentNullException(nameof(path))), passphrase);

        /// <summary>Loads, or returns an empty store when the file doesn't exist yet.</summary>
        public static SecretsFile LoadOrCreate(string path, string passphrase)
            => File.Exists(path ?? throw new ArgumentNullException(nameof(path))) ? Load(path, passphrase) : new SecretsFile();

        private static byte[] Header(int iterations, byte[] salt, byte[] nonce)
        {
            var h = new byte[Magic.Length + 4 + SaltSize + NonceSize];
            Magic.CopyTo(h, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(Magic.Length), iterations);
            salt.CopyTo(h, Magic.Length + 4);
            nonce.CopyTo(h, Magic.Length + 4 + SaltSize);
            return h;
        }
    }
}
#endif
