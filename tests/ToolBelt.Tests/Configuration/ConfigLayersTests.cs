using System;
using System.Collections.Generic;
using ToolBelt.Configuration;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Configuration
{
    public sealed class ConfigLayersTests
    {
        private static Dictionary<string, string?> D(params (string, string?)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        public void HigherPriorityWins()
        {
            var layers = new ConfigLayers()
                .Add("defaults", D(("host", "localhost"), ("port", "80")))
                .Add("file", D(("port", "8080")))
                .Add("env", D(("host", "prod.example.com")));

            var resolved = layers.Resolve();
            Check.Equal("prod.example.com", resolved["host"]);
            Check.Equal("8080", resolved["port"]);
        }

        public void ProvenanceTracksWinningSource()
        {
            var layers = new ConfigLayers()
                .Add("defaults", D(("host", "localhost"), ("port", "80")))
                .Add("file", D(("port", "8080")))
                .Add("env", D(("host", "prod")));

            var prov = layers.Provenance();
            Check.Equal("env", prov["host"]);
            Check.Equal("file", prov["port"]);
            Check.Equal("file", layers.SourceOf("port"));
            Check.Equal(null, layers.SourceOf("missing"));
        }

        public void CaseInsensitiveKeys()
        {
            var layers = new ConfigLayers().Add("s", D(("Host", "a")));
            Check.True(layers.TryGet("host", out var v) && v == "a");
        }

        public void TryGetMiss()
        {
            var layers = new ConfigLayers().Add("s", D(("a", "1")));
            Check.False(layers.TryGet("b", out _));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new ConfigLayers().Add(null!, D()));
            Check.Throws<ArgumentNullException>(() => new ConfigLayers().Add("s", null!));
        }
    }
}
