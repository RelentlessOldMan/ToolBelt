// ToolBelt drop-in — also copy IO/DirectoryUtils.cs (tolerant, link-safe tree walk).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>The differences between an expected and an actual set of file hashes.</summary>
    public sealed class ManifestComparison
    {
        internal ManifestComparison(List<string> matching, List<string> modified, List<string> missing, List<string> added)
        {
            Matching = matching;
            Modified = modified;
            Missing = missing;
            Added = added;
        }

        /// <summary>Paths present in both with the same hash.</summary>
        public IReadOnlyList<string> Matching { get; }

        /// <summary>Paths present in both whose content differs.</summary>
        public IReadOnlyList<string> Modified { get; }

        /// <summary>Paths expected but not present.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>Paths present but not expected.</summary>
        public IReadOnlyList<string> Added { get; }

        /// <summary>True when nothing was modified, missing or added.</summary>
        public bool IsMatch => Modified.Count == 0 && Missing.Count == 0 && Added.Count == 0;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0} matching, {1} modified, {2} missing, {3} added", Matching.Count, Modified.Count, Missing.Count, Added.Count);
    }

    /// <summary>
    /// A SHA-256 manifest of a directory tree: create it, save it, and later verify the tree against it — or compare two
    /// trees by content. The text form is the GNU <c>sha256sum</c> format (<c>&lt;hex&gt;  &lt;path&gt;</c>, one per line,
    /// including its backslash escaping for names containing '\' or line breaks), so a manifest written here checks with
    /// <c>sha256sum -c</c> and vice versa. Paths are relative to the root, '/'-separated, compared ordinally (exact case),
    /// and kept sorted so manifests diff cleanly. The tree walk skips links and reports unreadable entries to an optional
    /// callback rather than aborting.
    /// </summary>
    public sealed class ChecksumManifest
    {
        private readonly SortedDictionary<string, string> _entries;

        /// <summary>Builds a manifest from (relative path, hex SHA-256) pairs; hashes are normalised to lower case.</summary>
        public ChecksumManifest(IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (entries is null) throw new ArgumentNullException(nameof(entries));
            _entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in entries)
            {
                string path = NormalizePath(kv.Key ?? throw new ArgumentException("A path is null.", nameof(entries)));
                string hash = NormalizeHash(kv.Value) ?? throw new ArgumentException($"'{kv.Value}' is not a SHA-256 hex digest.", nameof(entries));
                if (_entries.ContainsKey(path)) throw new ArgumentException($"Duplicate path '{path}'.", nameof(entries));
                _entries.Add(path, hash);
            }
        }

        /// <summary>Relative path → lower-case hex SHA-256, in ordinal path order.</summary>
        public IReadOnlyDictionary<string, string> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>
        /// Hashes every file under <paramref name="directory"/> (optionally only those whose relative path passes
        /// <paramref name="fileFilter"/>). Unreadable directories or files go to <paramref name="onError"/> and are left out.
        /// </summary>
        public static ChecksumManifest Create(string directory, Func<string, bool>? fileFilter = null, Action<string, Exception>? onError = null)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (string file in DirectoryUtils.EnumerateFiles(directory, "*", onError))
            {
                string rel = DirectoryUtils.GetRelativePath(directory, file);
                if (fileFilter != null && !fileFilter(rel)) continue;
                try { pairs.Add(new KeyValuePair<string, string>(rel, HashFile(file))); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (onError is null) throw;
                    onError(file, ex);
                }
            }
            return new ChecksumManifest(pairs);
        }

        /// <summary>The lower-case hex SHA-256 of a file, streamed (any size).</summary>
        public static string HashFile(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(stream));
        }

        /// <summary>Re-hashes <paramref name="directory"/> and compares it with this manifest.</summary>
        public ManifestComparison Verify(string directory, Action<string, Exception>? onError = null)
            => Compare(this, Create(directory, null, onError));

        /// <summary>Compares two manifests: what in <paramref name="actual"/> matches, differs from, lacks or adds to <paramref name="expected"/>.</summary>
        public static ManifestComparison Compare(ChecksumManifest expected, ChecksumManifest actual)
        {
            if (expected is null) throw new ArgumentNullException(nameof(expected));
            if (actual is null) throw new ArgumentNullException(nameof(actual));
            var matching = new List<string>();
            var modified = new List<string>();
            var missing = new List<string>();
            var added = new List<string>();
            foreach (var kv in expected._entries)
            {
                if (!actual._entries.TryGetValue(kv.Key, out string? hash)) missing.Add(kv.Key);
                else if (hash == kv.Value) matching.Add(kv.Key);
                else modified.Add(kv.Key);
            }
            foreach (string path in actual._entries.Keys)
                if (!expected._entries.ContainsKey(path)) added.Add(path);
            return new ManifestComparison(matching, modified, missing, added);
        }

        /// <summary>Compares two directory trees by content.</summary>
        public static ManifestComparison CompareDirectories(string expectedDirectory, string actualDirectory)
            => Compare(Create(expectedDirectory), Create(actualDirectory));

        // ---------- text form (sha256sum) ----------

        /// <summary>The manifest in <c>sha256sum</c> format, LF line endings, sorted by path.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (var kv in _entries)
            {
                bool escape = kv.Key.IndexOf('\\') >= 0 || kv.Key.IndexOf('\n') >= 0 || kv.Key.IndexOf('\r') >= 0;
                if (escape) sb.Append('\\');
                sb.Append(kv.Value).Append("  ");
                sb.Append(escape ? kv.Key.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r") : kv.Key);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public override string ToString() => ToText();

        /// <summary>
        /// Parses <c>sha256sum</c> output: text (<c>"  "</c>) or binary (<c>" *"</c>) markers, escaped names, blank lines and
        /// CRLF are accepted. Throws <see cref="FormatException"/> naming the offending line.
        /// </summary>
        public static ChecksumManifest Parse(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var pairs = new List<KeyValuePair<string, string>>();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].EndsWith("\r", StringComparison.Ordinal) ? lines[i].Substring(0, lines[i].Length - 1) : lines[i];
                if (line.Trim().Length == 0) continue;
                bool escaped = line.StartsWith("\\", StringComparison.Ordinal);
                if (escaped) line = line.Substring(1);
                if (line.Length < 67 || line[64] != ' ' || (line[65] != ' ' && line[65] != '*'))
                    throw new FormatException($"Line {i + 1}: expected '<64 hex digits>  <path>'.");
                string hash = NormalizeHash(line.Substring(0, 64))
                    ?? throw new FormatException($"Line {i + 1}: '{line.Substring(0, 64)}' is not a SHA-256 hex digest.");
                string path = line.Substring(66);
                if (escaped) path = Unescape(path, i + 1);
                pairs.Add(new KeyValuePair<string, string>(path, hash));
            }
            try { return new ChecksumManifest(pairs); }
            catch (ArgumentException ex) { throw new FormatException(ex.Message, ex); }
        }

        /// <summary>Writes the manifest as UTF-8 without a byte-order mark (which <c>sha256sum</c> would choke on).</summary>
        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            File.WriteAllText(path, ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static ChecksumManifest Load(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        // ---------- helpers ----------

        private static string Unescape(string s, int lineNumber)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\') { sb.Append(s[i]); continue; }
                if (++i >= s.Length) throw new FormatException($"Line {lineNumber}: dangling escape.");
                switch (s[i])
                {
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: throw new FormatException($"Line {lineNumber}: unknown escape '\\{s[i]}'.");
                }
            }
            return sb.ToString();
        }

        private static string NormalizePath(string path)
        {
            if (path.Length == 0) throw new ArgumentException("A path is empty.");
            // Backslashes are kept: they are legal in POSIX names. A leading "./" (as `find . | xargs sha256sum`
            // produces) is dropped so such manifests compare with ones written here.
            return path.StartsWith("./", StringComparison.Ordinal) ? path.Substring(2) : path;
        }

        private static string? NormalizeHash(string? hash)
        {
            if (hash is null || hash.Length != 64) return null;
            foreach (char c in hash)
                if (!Uri.IsHexDigit(c)) return null;
            return hash.ToLowerInvariant();
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
