using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class ChecksumManifestTests
    {
        private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";     // SHA-256("abc")
        private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";   // SHA-256("")

        private static string Tree(TempDirectory tmp, string name = "tree")
        {
            string root = tmp.Combine(name);
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "abc");
            File.WriteAllText(Path.Combine(root, "sub", "empty.dat"), "");
            File.WriteAllText(Path.Combine(root, "sub", "deep", "my file.txt"), "abc");
            return root;
        }

        private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

        public void HashFile_KnownVectors()
        {
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("abc"), "abc");
            File.WriteAllText(tmp.Combine("empty"), "");
            Check.Equal(AbcHash, ChecksumManifest.HashFile(tmp.Combine("abc")));
            Check.Equal(EmptyHash, ChecksumManifest.HashFile(tmp.Combine("empty")));
        }

        public void Create_RelativeSortedPaths()
        {
            using var tmp = new TempDirectory();
            ChecksumManifest m = ChecksumManifest.Create(Tree(tmp));
            Check.Equal("a.txt|sub/deep/my file.txt|sub/empty.dat", string.Join("|", m.Entries.Keys));
            Check.Equal(AbcHash, m.Entries["sub/deep/my file.txt"]);
            Check.Equal(EmptyHash, m.Entries["sub/empty.dat"]);
            Check.Equal(2, ChecksumManifest.Create(tmp.Combine("tree"), rel => rel.EndsWith(".txt", StringComparison.Ordinal)).Count);
        }

        public void ToText_IsSha256sumFormat()
        {
            using var tmp = new TempDirectory();
            string text = ChecksumManifest.Create(Tree(tmp)).ToText();
            Check.Equal(
                AbcHash + "  a.txt\n" +
                AbcHash + "  sub/deep/my file.txt\n" +
                EmptyHash + "  sub/empty.dat\n", text);
        }

        public void Parse_AcceptsSha256sumVariants()
        {
            // Binary marker, upper-case hex, CRLF, blank lines, a "./" prefix and an escaped name with \ and newline.
            string text =
                AbcHash.ToUpperInvariant() + " *./bin/tool.exe\r\n" +
                "\r\n" +
                "\\" + EmptyHash + "  dir\\\\odd\\nname\n";
            ChecksumManifest m = ChecksumManifest.Parse(text);
            Check.Equal(2, m.Count);
            Check.Equal(AbcHash, m.Entries["bin/tool.exe"]);
            Check.Equal(EmptyHash, m.Entries["dir\\odd\nname"]);
            // ...and the escaped name survives a round trip.
            Check.True(m.ToText().Contains("\\" + EmptyHash + "  dir\\\\odd\\nname\n"));
            Check.Equal(m.ToText(), ChecksumManifest.Parse(m.ToText()).ToText());
        }

        public void Parse_RejectsMalformedLinesWithTheLineNumber()
        {
            var ex = Check.Throws<FormatException>(() => ChecksumManifest.Parse(AbcHash + "  ok\nnot a hash line\n"));
            Check.True(ex.Message.StartsWith("Line 2:", StringComparison.Ordinal), ex.Message);
            Check.Throws<FormatException>(() => ChecksumManifest.Parse(new string('g', 64) + "  x\n"));        // not hex
            Check.Throws<FormatException>(() => ChecksumManifest.Parse(AbcHash + "  dup\n" + AbcHash + "  dup\n"));
            Check.Throws<FormatException>(() => ChecksumManifest.Parse("\\" + AbcHash + "  bad\\q\n"));        // unknown escape
        }

        public void Verify_ReportsModifiedMissingAndAdded()
        {
            using var tmp = new TempDirectory();
            string root = Tree(tmp);
            ChecksumManifest m = ChecksumManifest.Create(root);
            Check.True(m.Verify(root).IsMatch);

            File.WriteAllText(Path.Combine(root, "a.txt"), "abd");
            File.Delete(Path.Combine(root, "sub", "empty.dat"));
            File.WriteAllText(Path.Combine(root, "new.txt"), "n");
            ManifestComparison c = m.Verify(root);
            Check.False(c.IsMatch);
            Check.Equal("a.txt", string.Join("|", c.Modified));
            Check.Equal("sub/empty.dat", string.Join("|", c.Missing));
            Check.Equal("new.txt", string.Join("|", c.Added));
            Check.Equal("sub/deep/my file.txt", string.Join("|", c.Matching));
            Check.Equal("1 matching, 1 modified, 1 missing, 1 added", c.ToString());
        }

        public void SaveLoad_RoundTripWithoutBom()
        {
            using var tmp = new TempDirectory();
            ChecksumManifest m = ChecksumManifest.Create(Tree(tmp));
            string file = tmp.Combine("SHA256SUMS");
            m.Save(file);
            byte[] bytes = File.ReadAllBytes(file);
            Check.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB, "no UTF-8 BOM (sha256sum would reject it)");
            Check.Equal(m.ToText(), ChecksumManifest.Load(file).ToText());
        }

        public void CompareDirectories_ByContent()
        {
            using var tmp = new TempDirectory();
            string a = Tree(tmp, "a");
            string b = tmp.Combine("b");
            DirectoryUtils.Copy(a, b);
            Check.True(ChecksumManifest.CompareDirectories(a, b).IsMatch);
            File.AppendAllText(Path.Combine(b, "sub", "deep", "my file.txt"), "!");
            ManifestComparison c = ChecksumManifest.CompareDirectories(a, b);
            Check.Equal("sub/deep/my file.txt", string.Join("|", c.Modified));
        }

        public void Constructor_Validation()
        {
            Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { KV("x", "123") }));
            Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { KV("", AbcHash) }));
            Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { KV("x", AbcHash), KV("./x", AbcHash) }));   // same path
            Check.Equal(AbcHash, new ChecksumManifest(new[] { KV("x", AbcHash.ToUpperInvariant()) }).Entries["x"]);
        }
    }
}
