// ToolBelt drop-in — fully self-contained (BCL only; net8+, System.Text.Json is in-box).
#if !NETSTANDARD2_0
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Per-user settings persisted as JSON: a typed object loaded at startup and saved on change, in the conventional
    /// per-user location (<c>%APPDATA%\Company\App\settings.json</c>, or <c>~/.config/…</c>) unless a path is given.
    /// It gets the boring-but-critical parts right: saves are atomic (temp file + rename); a missing file yields defaults;
    /// a corrupt file (or one holding a value the type rejects) is moved aside to <c>settings.json.corrupt-&lt;timestamp&gt;</c> and defaults are used (reported via
    /// <see cref="LoadWarning"/>) rather than crashing the app on every launch, and a file that can't be read at all
    /// (locked, no permission) also yields defaults with a warning, left in place; unknown properties from newer
    /// versions are ignored and missing ones keep their defaults; NaN and ±∞ in floating-point settings round-trip.
    /// Not thread-safe — save from one place.
    /// </summary>
    public sealed class SettingsStore<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        private SettingsStore(string path, T value, string? warning)
        {
            Path = path;
            Value = value;
            LoadWarning = warning;
        }

        /// <summary>The settings file.</summary>
        public string Path { get; }

        /// <summary>The current settings. Change properties, then <see cref="Save"/>.</summary>
        public T Value { get; private set; }

        /// <summary>Why defaults were used instead of the file (corrupt file moved aside), or null.</summary>
        public string? LoadWarning { get; }

        /// <summary>The default per-user location: <c>&lt;ApplicationData&gt;/company/app/fileName</c>.</summary>
        public static string DefaultPath(string company, string app, string fileName = "settings.json")
        {
            if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
            if (string.IsNullOrWhiteSpace(app)) throw new ArgumentException("App is required.", nameof(app));
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            return System.IO.Path.Combine(root, company, app, fileName);
        }

        /// <summary>Loads from the default per-user location.</summary>
        public static SettingsStore<T> Load(string company, string app) => LoadFrom(DefaultPath(company, app));

        /// <summary>Loads from <paramref name="path"/> (defaults if missing; corrupt files are moved aside).</summary>
        public static SettingsStore<T> LoadFrom(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string full = System.IO.Path.GetFullPath(path);
            if (!File.Exists(full)) return new SettingsStore<T>(full, new T(), null);
            string text;
            try
            {
                text = File.ReadAllText(full, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                return new SettingsStore<T>(full, new T(), null);                // deleted since the Exists check
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Locked or unreadable: the file may well be fine, so leave it where it is.
                return new SettingsStore<T>(full, new T(), $"Settings file could not be read ({ex.Message}); defaults were used.");
            }
            try
            {
                T? value = JsonSerializer.Deserialize<T>(text, Options);
                return new SettingsStore<T>(full, value ?? new T(), null);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                // Malformed JSON, but also a value the type itself rejects (a validating setter or constructor that
                // throws): either way this file would crash every launch, so it is set aside like a corrupt one.
                if (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                // Unique name (never overwrite an earlier preserved copy); if the move itself fails, still start with defaults.
                string aside = full + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)
                               + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                try
                {
                    File.Move(full, aside);
                    return new SettingsStore<T>(full, new T(), $"Settings file was unreadable ({ex.Message}); moved to '{aside}' and defaults were used.");
                }
                catch (Exception moveEx) when (moveEx is IOException || moveEx is UnauthorizedAccessException)
                {
                    return new SettingsStore<T>(full, new T(),
                        $"Settings file was unreadable ({ex.Message}) and could not be moved aside ({moveEx.Message}); defaults were used and saving will overwrite it.");
                }
            }
        }

        /// <summary>Writes the current settings atomically, creating the directory if needed.</summary>
        public void Save()
        {
            string dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            string temp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(fs, Value, Options);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(temp, Path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>Applies <paramref name="change"/> to the settings and saves.</summary>
        public void Update(Action<T> change)
        {
            if (change is null) throw new ArgumentNullException(nameof(change));
            change(Value);
            Save();
        }

        /// <summary>Back to defaults (and saves).</summary>
        public void Reset()
        {
            Value = new T();
            Save();
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,   // NaN is a common "unset"
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }
    }
}
#endif
