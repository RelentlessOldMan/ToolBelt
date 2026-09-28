using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Configuration;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Configuration
{
    public sealed class ConfigBinderTests
    {
        private enum Mode { Off, On, Auto }

        private sealed class Endpoint { public string? Host { get; set; } public int Port { get; set; } }
        private sealed class Options
        {
            public string? Name { get; set; }
            public int Retries { get; set; }
            public bool Verbose { get; set; }
            public Mode Mode { get; set; }
            public TimeSpan Timeout { get; set; }
            public Endpoint Primary { get; set; } = new Endpoint();
            public List<string> Tags { get; set; } = new List<string>();
            public int[] Ports { get; set; } = Array.Empty<int>();
        }

        private static Dictionary<string, string?> D(params (string, string?)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        public void BindsScalarsEnumsAndDurations()
        {
            var opts = ConfigBinder.Bind<Options>(D(
                ("Name", "svc"), ("Retries", "3"), ("Verbose", "true"),
                ("Mode", "Auto"), ("Timeout", "1m 30s")));

            Check.Equal("svc", opts.Name);
            Check.Equal(3, opts.Retries);
            Check.True(opts.Verbose);
            Check.Equal(Mode.Auto, opts.Mode);
            Check.Equal(TimeSpan.FromSeconds(90), opts.Timeout);
        }

        public void BindsNestedSections()
        {
            var opts = ConfigBinder.Bind<Options>(D(("Primary:Host", "db1"), ("Primary:Port", "5432")));
            Check.Equal("db1", opts.Primary.Host);
            Check.Equal(5432, opts.Primary.Port);
        }

        public void BindsArraysAndLists()
        {
            var opts = ConfigBinder.Bind<Options>(D(
                ("Tags:0", "a"), ("Tags:1", "b"), ("Tags:2", "c"),
                ("Ports:0", "80"), ("Ports:1", "443")));
            Check.True(opts.Tags.SequenceEqual(new[] { "a", "b", "c" }));
            Check.True(opts.Ports.SequenceEqual(new[] { 80, 443 }));
        }

        public void CaseInsensitiveKeys()
        {
            var opts = ConfigBinder.Bind<Options>(D(("name", "x"), ("primary:host", "h")));
            Check.Equal("x", opts.Name);
            Check.Equal("h", opts.Primary.Host);
        }

        public void MissingKeysLeaveDefaults()
        {
            var opts = ConfigBinder.Bind<Options>(D(("Name", "only")));
            Check.Equal("only", opts.Name);
            Check.Equal(0, opts.Retries);
            Check.Equal(0, opts.Tags.Count);
        }

        public void ReportsAllErrorsAtOnce()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Options>(D(
                ("Retries", "notanumber"),
                ("Mode", "Nope"),
                ("Primary:Port", "alsobad"))));
            Check.Equal(3, ex.Errors.Count);
            Check.True(ex.Errors.Any(e => e.Contains("Retries")));
            Check.True(ex.Errors.Any(e => e.Contains("Mode")));
            Check.True(ex.Errors.Any(e => e.Contains("Primary:Port")));
        }

        public void NullValues_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ConfigBinder.Bind<Options>(null!));
        }

        private sealed class SelfReferential
        {
            public string? Name { get; set; }
            public SelfReferential? Child { get; set; } // cyclic type reference
        }

        public void CyclicTypeDoesNotStackOverflow()
        {
            // A self-referential config type must bind without infinite recursion.
            var obj = ConfigBinder.Bind<SelfReferential>(D(("Name", "root")));
            Check.Equal("root", obj.Name);
        }
    }
}
