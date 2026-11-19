// ToolBelt drop-in — fully self-contained (BCL only; net8+, System.Text.Json is in-box).
#if !NETSTANDARD2_0
using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Get-only properties load too: a collection (<c>List&lt;string&gt; Recent { get; } = new()</c>, a queue, a stack, a
    /// concurrent collection, or one declared as <c>IEnumerable&lt;T&gt;</c>) is cleared and refilled from the file, and an
    /// object is filled in place (one that is null, an array or a read-only collection keeps its default). A public
    /// property with a private setter loads through that setter. A stack keeps its order. Only public properties are
    /// stored, not fields. A type System.Text.Json cannot load at all (an abstract property type, a
    /// <c>ConcurrentBag</c>) makes <see cref="LoadFrom"/> throw rather than move a good file aside. A string holding a lone
    /// surrogate, or a <see cref="char"/> that is one (which JSON cannot store), makes a save throw
    /// <see cref="ArgumentException"/> rather than write U+FFFD, and a value no load could read back (an interface-typed
    /// property holding an object) makes it throw <see cref="NotSupportedException"/>. A save keeps the file's ACL (and on
    /// Unix its mode).
    /// A settings path that is a symbolic link is written through to its target; a save keeps the file's Hidden and System
    /// attributes, fails at once on a read-only file, waits about half a second for another reader of the file to let go,
    /// and removes temp files that a crashed save left behind more than an hour ago.
    /// A file that is not valid UTF-8 counts as corrupt, and one reached through a symbolic link is moved aside at its
    /// target. A value the type would reject when loaded back (setters that check each other) makes a save throw
    /// <see cref="InvalidOperationException"/>, as do Save, Update and Reset called from inside an Update's change. A path
    /// that is a directory makes <see cref="LoadFrom"/> throw <see cref="ArgumentException"/>.
    /// Not thread-safe — save from one place.
    /// </summary>
    public sealed class SettingsStore<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        // Set when the file exists but could not be read: saving would replace a file that may be fine with defaults.
        private bool _unread;

        // Set when an Update failed and the settings could not be put back exactly: Value may be half-changed.
        private bool _broken;

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
            // A directory is no settings file: it loaded as "no file yet", and Save then failed with an access error.
            if (Directory.Exists(full)) throw new ArgumentException($"'{full}' is a directory, not a settings file.", nameof(path));
            byte[] bytes;
            for (int attempt = 0; ; attempt++)
            {
                if (!File.Exists(full) && !Reappears(full)) return new SettingsStore<T>(full, new T(), null);
                try
                {
                    bytes = File.ReadAllBytes(full);
                    break;
                }
                catch (FileNotFoundException) when (attempt < 3)
                {
                    // Gone since the Exists check: deleted, or another instance is replacing it. Look again.
                }
                catch (FileNotFoundException)
                {
                    return new SettingsStore<T>(full, new T(), null);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Locked or unreadable: the file may well be fine, so leave it where it is, and don't let a save replace it
                    // (the first Update after a lock at startup overwrote the user's file with defaults).
                    return new SettingsStore<T>(full, new T(),
                        $"Settings file could not be read ({ex.Message}); defaults were used, and saving is refused until Reset.", unread: true);
                }
            }
            try
            {
                // Strict UTF-8: a file in another encoding (Latin-1 "café") loaded with U+FFFD in place of its bytes, and
                // the next save made that permanent. It is set aside like any other unreadable file.
                string text;
                using (var reader = new StreamReader(new MemoryStream(bytes), Strict, detectEncodingFromByteOrderMarks: true))
                    text = reader.ReadToEnd();
                T? value = JsonSerializer.Deserialize<T>(text, Options);
                return new SettingsStore<T>(full, value ?? new T(), null);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                // Malformed JSON, but also a value the type itself rejects (a validating setter or constructor that
                // throws): either way this file would crash every launch, so it is set aside like a corrupt one.
                if (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                // A type System.Text.Json can't load at all (an abstract property, a ConcurrentBag) fails for every file,
                // so it is the program's fault, not the file's: a valid file was moved aside on every launch.
                if ((ex is NotSupportedException || ex is InvalidOperationException) && IsSerializerFault(ex)) throw;
                // Unique name (never overwrite an earlier preserved copy); if the move itself fails, still start with defaults.
                // A symbolic link's target is moved, not the link: the link was renamed, still pointing at the corrupt file,
                // and the next save put a plain file where it had been.
                string real = LinkTarget(full);
                string aside = AsideName(real, ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)
                               + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                try
                {
                    File.Move(real, aside);
                    return new SettingsStore<T>(full, new T(), $"Settings file was unreadable ({ex.Message}); moved to '{aside}' and defaults were used.");
                }
                catch (Exception moveEx) when (moveEx is IOException || moveEx is UnauthorizedAccessException)
                {
                    return new SettingsStore<T>(full, new T(),
                        $"Settings file was unreadable ({ex.Message}) and could not be moved aside ({moveEx.Message}); defaults were used and saving will overwrite it.");
                }
            }
        }

        private static readonly UTF8Encoding Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        // The file a path names, through any symbolic links (the path itself when it is no link).
        private static string LinkTarget(string path)
        {
            var file = new FileInfo(path);
            if (file.Directory is { Exists: true } && file.LinkTarget != null
                && file.ResolveLinkTarget(returnFinalTarget: true) is FileSystemInfo resolved) return resolved.FullName;
            return path;
        }

        // Thrown by System.Text.Json itself (not by the settings type's own code, which a bad value can trigger).
        private static bool IsSerializerFault(Exception ex)
            => ex.TargetSite?.DeclaringType?.Assembly == typeof(JsonSerializer).Assembly;

        // The file name plus a suffix, the name shortened so the result stays within 255 UTF-16 units and 255 UTF-8 bytes
        // (the Windows and Unix limits): a name near the limit could not be moved aside, so it was overwritten.
        private static string AsideName(string full, string suffix)
        {
            string dir = System.IO.Path.GetDirectoryName(full)!, name = System.IO.Path.GetFileName(full);
            int keep = name.Length;
            while (keep > 0 && (keep + suffix.Length > 255 || Encoding.UTF8.GetByteCount(name.Substring(0, keep)) + suffix.Length > 255))
                keep--;
            if (keep > 0 && keep < name.Length && char.IsHighSurrogate(name[keep - 1])) keep--;
            return System.IO.Path.Combine(dir, name.Substring(0, keep) + suffix);
        }

        /// <summary>Writes the current settings atomically, creating the directory if needed. Throws
        /// <see cref="InvalidOperationException"/> when the file existed but could not be read at load time, or after an
        /// <see cref="Update"/> whose change failed and could not be undone.</summary>
        public void Save()
        {
            ThrowIfUpdating();
            ThrowIfCannotSave();
            Write(Value);
        }

        // Set while an Update's change runs. A nested Update or Save wrote the outer change half-done, and when the outer
        // change then threw, Value was rolled back but the file kept what the caller saw fail.
        private bool _updating;

        private void ThrowIfUpdating()
        {
            if (_updating)
                throw new InvalidOperationException("The settings can't be saved, updated or reset from inside an Update's change; it saves when it returns.");
        }

        private void ThrowIfCannotSave()
        {
            if (_unread)
                throw new InvalidOperationException($"The settings file '{Path}' could not be read when loaded; saving would replace it with defaults. Call Reset to overwrite it deliberately.");
            if (_broken)
                throw new InvalidOperationException($"An Update of '{Path}' failed and its settings could not be restored, so they may be half-changed; saving is refused until Reset.");
        }

        private void Write(T value)
        {
            // Written through a symbolic link to its target: the move replaced the link itself with a plain file, and the
            // target kept the old settings.
            string target = LinkTarget(Path);
            string dir = System.IO.Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(dir);
            // A fixed-length name: the file's own name plus 14 characters failed for names near the 255-character limit.
            string temp = System.IO.Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
            RemoveStaleTemps(dir, System.IO.Path.GetFileName(target));
            FileAttributes keep = 0;
            // Read once, not after an Exists check: another instance replacing the file leaves no file at the name for a
            // moment, and the save failed there with FileNotFoundException.
            FileAttributes? old = AttributesOf(target);
            if (old is FileAttributes found)
            {
                // A read-only file went through the whole retry loop before failing.
                if ((found & FileAttributes.ReadOnly) != 0)
                    throw new UnauthorizedAccessException($"The settings file '{target}' is read-only.");
                keep = found & (FileAttributes.Hidden | FileAttributes.System);
            }
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            // A value no load could read back (an interface-typed property, say) was saved, and from then on every
            // LoadFrom threw: the program failed at each launch after its first save. It fails at the save instead.
            try { JsonSerializer.Deserialize<T>(json, Options); }
            catch (Exception ex) when ((ex is NotSupportedException || ex is InvalidOperationException) && IsSerializerFault(ex))
            {
                throw new NotSupportedException($"These settings could not be loaded back ({ex.Message}), so they were not saved.", ex);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                // A value the type itself rejects on load (setters that check each other, run in file order) was saved,
                // and the next load moved the file aside as corrupt: every setting was lost.
                if (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                throw new InvalidOperationException($"These settings would be rejected when loaded back ({ex.Message}), so they were not saved.", ex);
            }
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(json, 0, json.Length);
                    fs.Flush(flushToDisk: true);
                }
                // A Unix file's mode (0600 for one holding a token) became the umask default.
                if (!OperatingSystem.IsWindows() && File.Exists(target))
                {
                    try { File.SetUnixFileMode(temp, File.GetUnixFileMode(target)); }
                    catch (FileNotFoundException) { }                            // replaced by another instance just now
                }
                // Another reader holding the file (an editor, a scanner, another instance loading it) made the move fail at
                // once; it gets about half a second to let go.
                using (var never = new System.Threading.ManualResetEventSlim(false))
                    for (int attempt = 0; ; attempt++)
                    {
                        // Replace keeps the file's own ACL: the move gave it the folder's inherited one, so an owner-only file
                        // became readable by everyone the folder admits.
                        try
                        {
                            if (OperatingSystem.IsWindows() && File.Exists(target)) File.Replace(temp, target, null);
                            else File.Move(temp, target, overwrite: true);
                            break;
                        }
                        // Also when the target is missing: another instance replacing it leaves no file at the name for a
                        // moment, and the save failed at once. The next attempt moves the temp file into place.
                        catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 10 && File.Exists(temp))
                        {
                            never.Wait(50);
                        }
                    }
                // The move gave the file the temp file's attributes, so a hidden settings file became visible. The save has
                // happened by now, so this is best effort: a failure here rolled Value back while the file held the new data.
                if (keep != 0 && OperatingSystem.IsWindows())
                {
                    try { File.SetAttributes(target, File.GetAttributes(target) | keep); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
            }
            finally
            {
                // A failed delete replaced the real error (a full disk, a refused move) with its own.
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }

        // The file's attributes, or null when there is no file (or it is gone for a moment mid-replace).
        private static FileAttributes? AttributesOf(string path)
        {
            try { return File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException) { return null; }
        }

        // A save cut short by a crash or power loss left its temp file, and they piled up; so did the "name~RF....TMP" copy
        // a failed File.Replace leaves. One more than a day old is no longer being written (an hour was too close for a
        // share whose clock runs behind); one still open can't be deleted, and any error is ignored.
        private static void RemoveStaleTemps(string dir, string name)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (string f in Temps(dir, name))
            {
                try { if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }

        // This store's temp files in the folder, and those File.Replace leaves for the named file.
        private static List<string> Temps(string dir, string name)
        {
            var found = new List<string>();
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, ".tb-*.tmp"))
                {
                    string n = System.IO.Path.GetFileName(f);
                    if (n.Length == 40 && IsHex(n, 4, 32)) found.Add(f);
                }
                if (OperatingSystem.IsWindows() && name.IndexOfAny(new[] { '*', '?' }) < 0)
                    found.AddRange(Directory.EnumerateFiles(dir, name + "~RF*.TMP"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return found;
        }

        // A file missing while another instance saves it (File.Replace leaves no file at the name for a moment) loaded as
        // "no file yet", with no warning, and the next save overwrote the user's settings with defaults. While a temp file
        // written in the last minute is there, the load waits up to half a second for the file to come back.
        private static bool Reappears(string full)
        {
            string dir = System.IO.Path.GetDirectoryName(full)!, name = System.IO.Path.GetFileName(full);
            if (!Directory.Exists(dir)) return false;
            using (var never = new System.Threading.ManualResetEventSlim(false))
                for (int i = 0; i < 50; i++)
                {
                    DateTime recent = DateTime.UtcNow.AddMinutes(-1);
                    bool saving = false;
                    foreach (string f in Temps(dir, name))
                    {
                        try { if (File.GetLastWriteTimeUtc(f) > recent) { saving = true; break; } }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    }
                    if (!saving) break;
                    never.Wait(10);
                    if (File.Exists(full)) return true;
                }
            return File.Exists(full);
        }

        private static bool IsHex(string s, int start, int count)
        {
            for (int i = start; i < start + count; i++)
                if (!(s[i] >= '0' && s[i] <= '9' || s[i] >= 'a' && s[i] <= 'f')) return false;
            return true;
        }

        /// <summary>Applies <paramref name="change"/> to the settings and saves. If <paramref name="change"/> or the save
        /// throws, the exception propagates and <see cref="Value"/> becomes a fresh copy of the settings as they were before
        /// it; if that copy would differ from them, <see cref="Value"/> is left as it is and saving is refused until
        /// <see cref="Reset"/>. Throws <see cref="InvalidOperationException"/>, without running the change, when saving is
        /// refused.</summary>
        public void Update(Action<T> change)
        {
            if (change is null) throw new ArgumentNullException(nameof(change));
            ThrowIfUpdating();
            // Checked first: a change run on a store that then refused to save left Value changed but never saved.
            ThrowIfCannotSave();
            // A change that threw partway left Value half-changed, and the next Save wrote that.
            string before = JsonSerializer.Serialize(Value, Options);
            try
            {
                _updating = true;
                try { change(Value); }
                finally { _updating = false; }
                Write(Value);
            }
            catch
            {
                Restore(before);
                throw;
            }
        }

        // The copy is used only if it saves exactly as the original did: one that lost something (a property that doesn't
        // load) would be saved later as if it were the user's settings. A restore that throws must not replace the
        // caller's exception either.
        private void Restore(string before)
        {
            try
            {
                T restored = JsonSerializer.Deserialize<T>(before, Options) ?? new T();
                if (JsonSerializer.Serialize(restored, Options) == before) { Value = restored; return; }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { }
            _broken = true;
        }

        /// <summary>Back to defaults (and saves), also over a file that could not be read at load time. If the save fails,
        /// <see cref="Value"/> is unchanged.</summary>
        public void Reset()
        {
            ThrowIfUpdating();
            var fresh = new T();
            Write(fresh);
            Value = fresh;
            _unread = false;
            _broken = false;
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
            o.Converters.Add(new StrictStringConverter());
            o.Converters.Add(new StrictCharConverter());
            o.Converters.Add(new StackConverterFactory());
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
                // A public property with a private setter was saved but never loaded, so the next save reset it.
                if (p.Set == null && p.Get != null && p.AttributeProvider is PropertyInfo prop && prop.GetSetMethod(nonPublic: true) is MethodInfo setter)
                {
                    p.Set = (owner, value) =>
                    {
                        try { setter.Invoke(owner, new[] { value }); }
                        catch (TargetInvocationException ex) when (ex.InnerException != null)
                        {
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                        }
                    };
                    continue;
                }
                if (p.Set != null || p.Get == null || p.PropertyType.IsValueType || p.PropertyType == typeof(string)) continue;
                Func<object, object?> get = p.Get;
                if (IsCollection(p.PropertyType) && CanRead(p.PropertyType))
                    p.Set = (owner, value) => { if (get(owner) is object target && value != null) Refill(target, value); };
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

        // A property System.Text.Json reads as a JSON array or object: what a get-only property holds is refilled from it.
        private static bool IsCollection(Type type)
        {
            if (!typeof(IEnumerable).IsAssignableFrom(type)) return false;
            try
            {
                JsonTypeInfoKind kind = PlainResolver.GetTypeInfo(type, Plain).Kind;
                return kind == JsonTypeInfoKind.Enumerable || kind == JsonTypeInfoKind.Dictionary;
            }
            catch (NotSupportedException) { return false; }
        }

        // A ReadOnlyCollection or ReadOnlyDictionary can't be read at all (no constructor System.Text.Json can use): with a
        // setter installed, every load of a saved file threw. Without one the property is skipped and keeps its default.
        private static bool CanRead(Type type)
        {
            try
            {
                JsonSerializer.Deserialize(PlainResolver.GetTypeInfo(type, Plain).Kind == JsonTypeInfoKind.Dictionary ? "{}" : "[]", type, Plain);
                return true;
            }
            catch (NotSupportedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        // Replace semantics for a get-only collection, like a settable property: clear it, then add the loaded items. The
        // object the property holds decides how, not the declared type: a Queue, a Stack, a concurrent collection, or a
        // List declared as IEnumerable<T> or IReadOnlyList<T> was never refilled, and the next save erased it. An array or
        // read-only collection can't be refilled and keeps its default: Clear threw ("Collection was of a fixed size"), and
        // the valid file was moved aside as corrupt on every launch.
        private static void Refill(object target, object value)
        {
            if (target is IList list)
            {
                if (list.IsReadOnly || list.IsFixedSize) return;
                list.Clear();
                foreach (object? item in (IEnumerable)value) list.Add(item);
                return;
            }
            if (target is IDictionary map)
            {
                if (map.IsReadOnly || map.IsFixedSize) return;
                map.Clear();
                foreach (object? item in (IEnumerable)value)
                {
                    if (item is DictionaryEntry e) map[e.Key] = e.Value;
                    else map[KeyValue(item!, "Key")!] = KeyValue(item!, "Value");
                }
                return;
            }
            Type type = target.GetType();
            try
            {
                foreach (Type i in type.GetInterfaces())
                {
                    if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(ICollection<>)) continue;
                    if ((bool)i.GetProperty("IsReadOnly")!.GetValue(target)!) return;
                    MethodInfo add = i.GetMethod("Add")!;
                    i.GetMethod("Clear")!.Invoke(target, null);
                    foreach (object? item in (IEnumerable)value) add.Invoke(target, new[] { item });
                    return;
                }
                // Queue<T>, Stack<T>, ConcurrentQueue<T>, ConcurrentStack<T>: a public Clear and a one-item add.
                MethodInfo? clear = type.GetMethod("Clear", Type.EmptyTypes);
                MethodInfo? put = null;
                bool push = false;
                foreach (string name in new[] { "Enqueue", "Push", "Add" })
                {
                    foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        if (m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1) { put = m; break; }
                    if (put != null) { push = name == "Push"; break; }
                }
                if (clear == null || put == null) return;
                var items = new List<object?>();
                foreach (object? item in (IEnumerable)value) items.Add(item);
                // A stack lists its top first, so pushing in that order turned it upside down.
                if (push) items.Reverse();
                clear.Invoke(target, null);
                foreach (object? item in items) put.Invoke(target, new[] { item });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // The collection's own exception, not wrapped in a TargetInvocationException.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private static object? KeyValue(object pair, string name) => pair.GetType().GetProperty(name)!.GetValue(pair);

        // JSON can't hold a lone surrogate: System.Text.Json wrote U+FFFD in its place, so the setting changed silently on
        // the next load. Saving it is refused instead.
        private sealed class StrictStringConverter : JsonConverter<string>
        {
            public override bool HandleNull => false;

            public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                Check(value);
                writer.WriteStringValue(value);
            }

            public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

            public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                Check(value);
                writer.WritePropertyName(value);
            }

            private static void Check(string value)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
                    if (char.IsSurrogate(c))
                        throw new ArgumentException($"A setting holds a lone surrogate (U+{(int)c:X4} at index {i}), which JSON cannot store.");
                }
            }
        }

        // A char that is half a surrogate pair was written as U+FFFD too (a char, char[] or List<char> setting), silently.
        private sealed class StrictCharConverter : JsonConverter<char>
        {
            public override char Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                JsonMetadataServices.CharConverter.Read(ref reader, typeToConvert, options);

            public override void Write(Utf8JsonWriter writer, char value, JsonSerializerOptions options)
            {
                Check(value);
                JsonMetadataServices.CharConverter.Write(writer, value, options);
            }

            public override char ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                JsonMetadataServices.CharConverter.ReadAsPropertyName(ref reader, typeToConvert, options);

            public override void WriteAsPropertyName(Utf8JsonWriter writer, char value, JsonSerializerOptions options)
            {
                Check(value);
                JsonMetadataServices.CharConverter.WriteAsPropertyName(writer, value, options);
            }

            private static void Check(char c)
            {
                if (char.IsSurrogate(c))
                    throw new ArgumentException($"A setting holds a lone surrogate (U+{(int)c:X4}), which JSON cannot store.");
            }
        }

        // System.Text.Json writes a stack top first but rebuilds it by pushing in file order, so every save and load
        // reversed it. Read pushes the items back in reverse.
        private sealed class StackConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type t)
                => t == typeof(Stack) || t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Stack<>)
                   || t.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentStack<>));

            public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options)
            {
                Type item = t.IsGenericType ? t.GetGenericArguments()[0] : typeof(object);
                // Nested in SettingsStore<T>, so T is its first type argument.
                return (JsonConverter)Activator.CreateInstance(typeof(StackConverter<,>).MakeGenericType(typeof(T), t, item))!;
            }
        }

        private sealed class StackConverter<TStack, TItem> : JsonConverter<TStack> where TStack : class, IEnumerable
        {
            public override TStack? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                List<TItem>? items = JsonSerializer.Deserialize<List<TItem>>(ref reader, options);
                if (items == null) return null;
                items.Reverse();
                object stack = Activator.CreateInstance(typeof(TStack))!;
                MethodInfo push = typeof(TStack).GetMethod("Push", new[] { typeof(TItem) })!;
                foreach (TItem item in items) push.Invoke(stack, new object?[] { item });
                return (TStack)stack;
            }

            public override void Write(Utf8JsonWriter writer, TStack value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (object? item in value) JsonSerializer.Serialize(writer, (TItem)item!, options);
                writer.WriteEndArray();
            }
        }
    }
}
#endif
