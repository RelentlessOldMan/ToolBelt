// ToolBelt drop-in — fully self-contained (BCL only; net8+, System.Text.Json is in-box).
#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.IO
{
    /// <summary>
    /// The JSON one-liners every tool needs, with sensible shared options (camelCase, indented, enums as strings,
    /// comments and trailing commas tolerated on read): <see cref="Serialize"/>/<see cref="Deserialize"/>,
    /// <see cref="Load"/>/<see cref="TryLoad"/> with errors that name the file, and <see cref="Save"/>, which writes to a
    /// temporary file and renames it over the target so a crash never leaves half a file.
    /// </summary>
    public static class JsonFile
    {
        /// <summary>The default options (a fresh copy each call, so callers can tweak it).</summary>
        public static JsonSerializerOptions CreateOptions(bool indented = true)
        {
            var o = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = indented,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        private static readonly JsonSerializerOptions Indented = CreateOptions(true);
        private static readonly JsonSerializerOptions Compact = CreateOptions(false);

        public static string Serialize<T>(T value, bool indented = true, JsonSerializerOptions? options = null)
            => JsonSerializer.Serialize(value, options ?? (indented ? Indented : Compact));

        public static T Deserialize<T>(string json, JsonSerializerOptions? options = null)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            T? value = JsonSerializer.Deserialize<T>(json, options ?? Indented);
            return value is null ? throw new JsonException("The JSON was 'null'.") : value;
        }

        /// <summary>Reads and deserializes a file; a parse error is rethrown naming the file, line and position.</summary>
        public static T Load<T>(string path, JsonSerializerOptions? options = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string text = File.ReadAllText(path, Encoding.UTF8);
            try { return Deserialize<T>(text, options); }
            catch (JsonException ex)
            {
                string where = ex.LineNumber is long line ? $" (line {line + 1}, position {ex.BytePositionInLine + 1})" : "";
                throw new JsonException($"Could not read '{path}'{where}: {ex.Message}", ex.Path, ex.LineNumber, ex.BytePositionInLine, ex);
            }
        }

        /// <summary>Loads, or returns <paramref name="fallback"/> when the file is missing; a corrupt file still throws.</summary>
        public static T LoadOrDefault<T>(string path, T fallback, JsonSerializerOptions? options = null)
            => File.Exists(path ?? throw new ArgumentNullException(nameof(path))) ? Load<T>(path, options) : fallback;

        /// <summary>Loads without throwing; false (with the reason) if the file is missing or invalid.</summary>
        public static bool TryLoad<T>(string path, out T? value, out string? error, JsonSerializerOptions? options = null)
        {
            value = default;
            error = null;
            try
            {
                if (!File.Exists(path)) { error = $"'{path}' does not exist."; return false; }
                value = Load<T>(path, options);
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Serializes and writes atomically (temp file in the same directory, then rename), creating the directory if needed.</summary>
        public static void Save<T>(string path, T value, bool indented = true, JsonSerializerOptions? options = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(fs, value, options ?? (indented ? Indented : Compact));
                    fs.Flush(flushToDisk: true);
                }
                File.Move(temp, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// JSON Lines / NDJSON: one JSON value per line — the append-friendly format for logs, event streams and test results.
    /// Reading is streaming and reports the line number of a malformed record; blank lines are skipped. Writing never
    /// emits embedded newlines (values are written compact).
    /// </summary>
    public static class JsonLines
    {
        private static readonly JsonSerializerOptions Options = JsonFile.CreateOptions(indented: false);

        /// <summary>Lazily reads every record from a file.</summary>
        public static IEnumerable<T> Read<T>(string path, JsonSerializerOptions? options = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            return ReadIterator<T>(path, options);
        }

        private static IEnumerable<T> ReadIterator<T>(string path, JsonSerializerOptions? options)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            foreach (var item in Read<T>(reader, options)) yield return item;
        }

        /// <summary>Lazily reads records from a reader.</summary>
        public static IEnumerable<T> Read<T>(TextReader reader, JsonSerializerOptions? options = null)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            int lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                yield return ParseLine<T>(line, lineNumber, options);
            }
        }

        public static async IAsyncEnumerable<T> ReadAsync<T>(TextReader reader, JsonSerializerOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            int lineNumber = 0;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                yield return ParseLine<T>(line, lineNumber, options);
            }
        }

        private static T ParseLine<T>(string line, int lineNumber, JsonSerializerOptions? options)
        {
            try
            {
                T? value = JsonSerializer.Deserialize<T>(line, options ?? Options);
                return value is null ? throw new JsonException("Record is 'null'.") : value;
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Line {lineNumber}: {ex.Message}", ex.Path, lineNumber - 1, ex.BytePositionInLine, ex);
            }
        }

        /// <summary>Writes one record as one line.</summary>
        public static void Write<T>(TextWriter writer, T record, JsonSerializerOptions? options = null)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            var o = options ?? Options;
            if (o.WriteIndented) o = new JsonSerializerOptions(o) { WriteIndented = false };
            writer.Write(JsonSerializer.Serialize(record, o));
            writer.Write('\n');
        }

        /// <summary>Writes all records to a file (replacing it), or appends with <paramref name="append"/>.</summary>
        public static void WriteAll<T>(string path, IEnumerable<T> records, bool append = false, JsonSerializerOptions? options = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (records is null) throw new ArgumentNullException(nameof(records));
            using var writer = new StreamWriter(path, append, new UTF8Encoding(false));
            foreach (var r in records) Write(writer, r, options);
        }

        /// <summary>Appends one record to a file (created if missing) — open, write the line, close.</summary>
        public static void Append<T>(string path, T record, JsonSerializerOptions? options = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var writer = new StreamWriter(path, append: true, new UTF8Encoding(false));
            Write(writer, record, options);
        }
    }
}
#endif
