// ToolBelt drop-in — fully self-contained (BCL only; net8+, System.Text.Json is in-box).
#if !NETSTANDARD2_0
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Per-user settings persisted as JSON: a typed object loaded at startup and saved on change, in the conventional
    /// per-user location (<c>%APPDATA%\Company\App\settings.json</c>, or <c>~/.config/…</c>) unless a path is given.
    /// It gets the boring-but-critical parts right: saves are atomic (temp file + rename); a missing file yields defaults;
    /// a corrupt file (or one holding a value the type rejects) is moved aside to <c>settings.json.corrupt-&lt;timestamp&gt;</c> and defaults are used (reported via
    /// <see cref="LoadWarning"/>) rather than crashing the app on every launch, and a file that can't be read at all
    /// (locked, no permission) also yields defaults with a warning, left in place and not overwritten (<see cref="Save"/>
    /// and <see cref="Update"/> throw until <see cref="Reset"/> deliberately replaces it); unknown properties from newer
    /// versions are ignored and missing ones keep their defaults; NaN and ±∞ in floating-point settings round-trip.
    /// Get-only properties load too: a collection (<c>List&lt;string&gt; Recent { get; } = new()</c>) is cleared and refilled
    /// from the file, and an object is filled in place (one that is null, an array or a read-only collection keeps its
    /// default). Only public properties are stored, not fields. A settings path that is a symbolic link is written through
    /// to its target, and a save waits about half a second for another reader of the file to let go.
    /// Not thread-safe — save from one place.
    /// </summary>
    public sealed class SettingsStore<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        // Set when the file exists but could not be read: saving would replace a file that may be fine with defaults.
        private bool _unread;

        private SettingsStore(string path, T value, string? warning, bool unread = false)
        {
            Path = path;
            Value = value;
            LoadWarning = warning;
            _unread = unread;
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
                // Locked or unreadable: the file may well be fine, so leave it where it is, and don't let a save replace it
                // (the first Update after a lock at startup overwrote the user's file with defaults).
                return new SettingsStore<T>(full, new T(),
                    $"Settings file could not be read ({ex.Message}); defaults were used, and saving is refused until Reset.", unread: true);
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

        /// <summary>Writes the current settings atomically, creating the directory if needed. Throws
        /// <see cref="InvalidOperationException"/> when the file existed but could not be read at load time.</summary>
        public void Save()
        {
            if (_unread)
                throw new InvalidOperationException($"The settings file '{Path}' could not be read when loaded; saving would replace it with defaults. Call Reset to overwrite it deliberately.");
            Write();
        }

        private void Write()
        {
            // Written through a symbolic link to its target: the move replaced the link itself with a plain file, and the
            // target kept the old settings.
            string target = Path;
            var file = new FileInfo(Path);
            if (file.Directory is { Exists: true } && file.LinkTarget != null
                && file.ResolveLinkTarget(returnFinalTarget: true) is FileSystemInfo resolved) target = resolved.FullName;
            string dir = System.IO.Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(dir);
            // A fixed-length name: the file's own name plus 14 characters failed for names near the 255-character limit.
            string temp = System.IO.Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(fs, Value, Options);
                    fs.Flush(flushToDisk: true);
                }
                // Another reader holding the file (an editor, a scanner, another instance loading it) made the move fail at
                // once; it gets about half a second to let go.
                using (var never = new System.Threading.ManualResetEventSlim(false))
                    for (int attempt = 0; ; attempt++)
                    {
                        try { File.Move(temp, target, overwrite: true); break; }
                        catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 10 && File.Exists(target))
                        {
                            never.Wait(50);
                        }
                    }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>Applies <paramref name="change"/> to the settings and saves. If <paramref name="change"/> throws, nothing
        /// is saved and <see cref="Value"/> becomes a fresh copy of the settings as they were before it.</summary>
        public void Update(Action<T> change)
        {
            if (change is null) throw new ArgumentNullException(nameof(change));
            // A change that threw partway left Value half-changed, and the next Save wrote that.
            string before = JsonSerializer.Serialize(Value, Options);
            try { change(Value); }
            catch
            {
                Value = JsonSerializer.Deserialize<T>(before, Options) ?? new T();
                throw;
            }
            Save();
        }

        /// <summary>Back to defaults (and saves), also over a file that could not be read at load time.</summary>
        public void Reset()
        {
            Value = new T();
            Write();
            _unread = false;
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
            // System.Text.Json saves get-only properties but skips them when loading, so `List<string> Recent { get; }`
            // would come back empty and the next save would erase it.
            o.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LoadGetOnlyProperties } };
            return o;
        }

        // Plain metadata (no modifiers), to ask how a property type is serialized without resolving it through Options.
        private static readonly JsonSerializerOptions Plain = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        private static readonly DefaultJsonTypeInfoResolver PlainResolver = new DefaultJsonTypeInfoResolver();

        private static void LoadGetOnlyProperties(JsonTypeInfo info)
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            foreach (JsonPropertyInfo p in info.Properties)
            {
                if (p.Set != null || p.Get == null || p.PropertyType.IsValueType || p.PropertyType == typeof(string)) continue;
                Func<object, object?> get = p.Get;
                if (ClearAndAdd(p.PropertyType) is Action<object, object> refill)
                    p.Set = (owner, value) => { if (get(owner) is object target && value != null) refill(target, value); };
                else if (!p.PropertyType.IsAbstract && !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)
                         && p.CustomConverter is null && PlainResolver.GetTypeInfo(p.PropertyType, Plain).Kind == JsonTypeInfoKind.Object)
                {
                    // A nested settings object. Only a real object: a get-only Uri or Version (a value converter) made every
                    // Save and Load of the type throw ("doesn't support populating").
                    p.ObjectCreationHandling = JsonObjectCreationHandling.Populate;
                    // One that is null (saved as null) failed to load ("Unable to assign 'null'"), so the file was moved
                    // aside as corrupt on every launch; with nothing to fill, it keeps its default.
                    p.Set = (owner, value) => { };
                }
            }
        }

        // Replace semantics for a get-only collection, like a settable property: clear it, then add the loaded items.
        private static Action<object, object>? ClearAndAdd(Type type)
        {
            // An array or read-only collection can't be refilled and keeps its default: Clear threw ("Collection was of a
            // fixed size"), and the valid file was moved aside as corrupt on every launch.
            if (typeof(System.Collections.IList).IsAssignableFrom(type))
                return (target, value) =>
                {
                    var list = (System.Collections.IList)target;
                    if (list.IsReadOnly || list.IsFixedSize) return;
                    list.Clear();
                    foreach (object? item in (System.Collections.IEnumerable)value) list.Add(item);
                };
            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
                return (target, value) =>
                {
                    var map = (System.Collections.IDictionary)target;
                    if (map.IsReadOnly || map.IsFixedSize) return;
                    map.Clear();
                    foreach (System.Collections.DictionaryEntry e in (System.Collections.IDictionary)value) map[e.Key] = e.Value;
                };
            // The type itself too: an interface's GetInterfaces leaves it out, so ICollection<string> was never refilled (and
            // the next save erased it).
            var candidates = new System.Collections.Generic.List<Type>(type.GetInterfaces());
            if (type.IsInterface) candidates.Insert(0, type);
            foreach (Type i in candidates)
            {
                if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(System.Collections.Generic.ICollection<>)) continue;
                MethodInfo clear = i.GetMethod("Clear")!, add = i.GetMethod("Add")!;
                PropertyInfo readOnly = i.GetProperty("IsReadOnly")!;
                return (target, value) =>
                {
                    if ((bool)readOnly.GetValue(target)!) return;
                    // The collection's own exception, not wrapped in a TargetInvocationException.
                    try
                    {
                        clear.Invoke(target, null);
                        foreach (object? item in (System.Collections.IEnumerable)value) add.Invoke(target, new[] { item });
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException != null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }
                };
            }
            return null;
        }
    }
}
#endif
