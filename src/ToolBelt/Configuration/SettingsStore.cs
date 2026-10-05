// ToolBelt drop-in — fully self-contained (BCL only; net8+, System.Text.Json is in-box).
#if !NETSTANDARD2_0
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Per-user settings persisted as JSON: a typed object loaded at startup and saved on change, in the conventional
    /// per-user location (<c>%APPDATA%\Company\App\settings.json</c>, or <c>~/.config/…</c>) unless a path is given.
    /// It gets the boring-but-critical parts right: saves are atomic (temp file + rename); a missing file yields defaults;
    /// a corrupt file is moved aside to <c>settings.json.corrupt-&lt;timestamp&gt;</c> and defaults are used (reported via
    /// <see cref="LoadWarning"/>) rather than crashing the app on every launch; unknown properties from newer versions are
    /// ignored and missing ones keep their defaults. Not thread-safe — save from one place.
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
            try
            {
                T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(full, Encoding.UTF8), Options);
                return new SettingsStore<T>(full, value ?? new T(), null);
            }
            catch (JsonException ex)
            {
                string aside = full + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                File.Move(full, aside, overwrite: true);
                return new SettingsStore<T>(full, new T(), $"Settings file was unreadable ({ex.Message}); moved to '{aside}' and defaults were used.");
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
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }
    }
}
#endif
