#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ToolBelt.Configuration;
using ToolBelt.IO;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>JsonFile, JsonLines, SettingsStore, TarUtils and SecretsFile (net8 features).</summary>
    public sealed class JsonTarSecretsTests
    {
        public enum Mode { Fast, Careful }

        public sealed class Config
        {
            public string Name { get; set; } = "default";
            public int Retries { get; set; } = 3;
            public Mode Mode { get; set; } = Mode.Fast;
            public List<double> Gains { get; set; } = new List<double>();
        }

        // ---------- JsonFile ----------

        public void JsonFile_SaveLoadRoundTripWithConventions()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine(Path.Combine("sub", "c.json"));
            JsonFile.Save(path, new Config { Name = "rig", Retries = 5, Mode = Mode.Careful, Gains = { 1.5, double.NaN } });
            string text = File.ReadAllText(path);
            Check.True(text.Contains("\"retries\": 5") && text.Contains("\"Careful\"") && text.Contains("\"NaN\""), text);
            var back = JsonFile.Load<Config>(path);
            Check.Equal("rig", back.Name);
            Check.Equal(Mode.Careful, back.Mode);
            Check.True(double.IsNaN(back.Gains[1]));
            Check.Equal(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
            Check.Equal("x", JsonFile.Deserialize<Config>("{ // comment\n \"NAME\": \"x\", }").Name);
        }

        public void JsonFile_ErrorsNameTheFileAndLine()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine("bad.json");
            File.WriteAllText(path, "{\n  \"name\": \"a\",\n  \"retries\": \"many\"\n}");
            var ex = Check.Throws<JsonException>(() => JsonFile.Load<Config>(path));
            Check.True(ex.Message.Contains("bad.json") && ex.Message.Contains("line 3"), ex.Message);
            Check.False(JsonFile.TryLoad<Config>(path, out _, out string? error));
            Check.NotNull(error);
            Check.False(JsonFile.TryLoad<Config>(tmp.Combine("none.json"), out _, out _));
            Check.Equal("fb", JsonFile.LoadOrDefault(tmp.Combine("none.json"), new Config { Name = "fb" }).Name);
        }

        // ---------- JsonLines ----------

        public sealed class Event
        {
            public int Id { get; set; }
            public string Text { get; set; } = "";
        }

        public async Task JsonLines_RoundTripAppendAndErrors()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine("e.jsonl");
            JsonLines.WriteAll(path, new[] { new Event { Id = 1, Text = "multi\nline" }, new Event { Id = 2 } });
            JsonLines.Append(path, new Event { Id = 3 });
            string[] lines = File.ReadAllLines(path);
            Check.Equal(3, lines.Length);                                             // embedded newline stays escaped
            Check.Equal("1,2,3", string.Join(",", JsonLines.Read<Event>(path).Select(e => e.Id)));
            Check.Equal("multi\nline", JsonLines.Read<Event>(path).First().Text);

            var list = new List<int>();
            await foreach (var e in JsonLines.ReadAsync<Event>(new StringReader("{\"id\":7}\n\n{\"id\":8}\n"))) list.Add(e.Id);
            Check.Equal("7,8", string.Join(",", list));

            var ex = Check.Throws<JsonException>(() => JsonLines.Read<Event>(new StringReader("{\"id\":1}\n\n{\"id\":oops}")).ToList());
            Check.True(ex.Message.StartsWith("Line 3:", StringComparison.Ordinal), ex.Message);
        }

        // ---------- SettingsStore ----------

        public void Settings_DefaultsSaveReloadAndCorruptionRecovery()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine(Path.Combine("App", "settings.json"));
            var s = SettingsStore<Config>.LoadFrom(path);
            Check.Equal("default", s.Value.Name);
            Check.Null(s.LoadWarning);
            s.Update(c => { c.Name = "saved"; c.Mode = Mode.Careful; });
            var again = SettingsStore<Config>.LoadFrom(path);
            Check.Equal("saved", again.Value.Name);
            Check.Equal(Mode.Careful, again.Value.Mode);

            File.WriteAllText(path, "{ \"name\": \"x\", \"futureSetting\": 1 }");    // unknown property from a newer version
            Check.Equal("x", SettingsStore<Config>.LoadFrom(path).Value.Name);
            Check.Equal(3, SettingsStore<Config>.LoadFrom(path).Value.Retries);         // missing keeps default

            File.WriteAllText(path, "{ not json");
            var recovered = SettingsStore<Config>.LoadFrom(path);
            Check.Equal("default", recovered.Value.Name);
            Check.NotNull(recovered.LoadWarning);
            Check.False(File.Exists(path));
            Check.Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*").Length);
            Check.True(SettingsStore<Config>.DefaultPath("Acme", "Tool").EndsWith(Path.Combine("Acme", "Tool", "settings.json"), StringComparison.Ordinal));
        }

        // ---------- TarUtils ----------

        public void Tar_RoundTripPlainAndGzip()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src");
            Directory.CreateDirectory(Path.Combine(src, "a", "b"));
            File.WriteAllText(Path.Combine(src, "top.txt"), "top");
            File.WriteAllBytes(Path.Combine(src, "a", "b", "deep.bin"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
            foreach (string name in new[] { "x.tar", "x.tar.gz", "x.tgz" })
            {
                string archive = tmp.Combine(name);
                TarUtils.CreateFromDirectory(src, archive);
                Check.Equal("a/b/deep.bin,top.txt", string.Join(",", TarUtils.ListEntries(archive).OrderBy(n => n, StringComparer.Ordinal)));
                string dest = tmp.Combine("out_" + name);
                TarUtils.ExtractToDirectory(archive, dest);
                Check.Equal("top", File.ReadAllText(Path.Combine(dest, "top.txt")));
                Check.Equal(5000L, new FileInfo(Path.Combine(dest, "a", "b", "deep.bin")).Length);
                Check.Throws<IOException>(() => TarUtils.ExtractToDirectory(archive, dest));     // no silent overwrite
                TarUtils.ExtractToDirectory(archive, dest, overwrite: true);
            }
            byte[] gz = File.ReadAllBytes(tmp.Combine("x.tgz"));
            Check.True(gz[0] == 0x1f && gz[1] == 0x8b, "gzip magic");
            string filtered = tmp.Combine("f.tar");
            TarUtils.CreateFromDirectory(src, filtered, filter: n => n.EndsWith(".txt", StringComparison.Ordinal));
            Check.Equal(1, TarUtils.ListEntries(filtered).Count);
        }

        private static string HostileTar(TempDirectory tmp, string name, Action<TarWriter> write)
        {
            string path = tmp.Combine(name);
            using (var fs = File.Create(path))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax)) write(w);
            return path;
        }

        public void Tar_RejectsTraversalAbsoluteAndLinks()
        {
            using var tmp = new TempDirectory();
            string dest = tmp.Combine("dest");
            string traversal = HostileTar(tmp, "t.tar", w => w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../evil.txt") { DataStream = new MemoryStream(new byte[] { 1 }) }));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(traversal, dest));
            Check.False(File.Exists(tmp.Combine("evil.txt")));
            string absolute = HostileTar(tmp, "a.tar", w => w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "/etc/evil") { DataStream = new MemoryStream(new byte[] { 1 }) }));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(absolute, dest));
            string link = HostileTar(tmp, "l.tar", w => w.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "ok") { LinkName = "../../outside" }));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(link, dest));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(link, dest, allowLinks: true));   // still points outside
            string prefix = HostileTar(tmp, "p.tar", w => w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../dest2/x") { DataStream = new MemoryStream(new byte[] { 1 }) }));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(prefix, dest));   // sibling sharing the prefix
        }

        // ---------- SecretsFile ----------

        public void Secrets_RoundTripAndFreshNonces()
        {
            var s = new SecretsFile();
            s["db"] = "Server=x;Password=p@ss=word";
            s.Set("token", "ünïcödé\nmultiline");
            byte[] a = s.ToBytes("correct horse", iterations: 1000);
            byte[] b = s.ToBytes("correct horse", iterations: 1000);
            Check.False(a.SequenceEqual(b), "fresh salt and nonce per save");
            var back = SecretsFile.FromBytes(a, "correct horse");
            Check.Equal("Server=x;Password=p@ss=word", back["db"]);
            Check.Equal("ünïcödé\nmultiline", back["token"]);
            Check.Equal("db,token", string.Join(",", back.Keys));
            Check.False(Encoding.UTF8.GetString(a).Contains("Server"), "plaintext must not appear");
        }

        public void Secrets_WrongPassphraseAndTamperingAreDetected()
        {
            var s = new SecretsFile();
            s["k"] = "v";
            byte[] data = s.ToBytes("pw", 1000);
            Check.Throws<CryptographicException>(() => SecretsFile.FromBytes(data, "PW"));
            for (int i = 0; i < data.Length; i++)
            {
                byte[] t = (byte[])data.Clone();
                t[i] ^= 0x01;
                Check.Throws<CryptographicException>(() => SecretsFile.FromBytes(t, "pw"), $"flip at byte {i}");
            }
            Check.Throws<CryptographicException>(() => SecretsFile.FromBytes(data.Take(data.Length - 1).ToArray(), "pw"));
            Check.Throws<CryptographicException>(() => SecretsFile.FromBytes(new byte[10], "pw"));
            Check.Throws<ArgumentException>(() => s.Set("a=b", "x"));
            Check.Throws<KeyNotFoundException>(() => _ = s["nope"]);
        }

        public void Secrets_FileSaveLoad()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine("app.secrets");
            var s = SecretsFile.LoadOrCreate(path, "pw");
            Check.Equal(0, s.Count);
            s["api"] = "123";
            s.Save(path, "pw", 1000);
            var loaded = SecretsFile.Load(path, "pw");
            Check.True(loaded.TryGet("api", out string? v) && v == "123");
            Check.True(loaded.Remove("api"));
        }
    }
}
#endif
