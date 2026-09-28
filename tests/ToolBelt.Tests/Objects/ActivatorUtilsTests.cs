using System;
using System.Collections.Generic;
using System.Reflection;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class ActivatorUtilsTests
    {
        private sealed class Multi
        {
            public string Origin { get; }
            public Multi() => Origin = "default";
            public Multi(int n) => Origin = "int:" + n;
            public Multi(string s) => Origin = "string:" + s;
        }

        private abstract class Plugin { public abstract string Name { get; } }
        private sealed class Alpha : Plugin { public override string Name => "alpha"; }
        private sealed class Beta : Plugin { public override string Name => "beta"; }
        private sealed class NeedsArg : Plugin { public NeedsArg(int _) { } public override string Name => "needsarg"; }

        public void CreateSelectsMatchingConstructor()
        {
            Check.Equal("default", ((Multi)ActivatorUtils.Create(typeof(Multi))).Origin);
            Check.Equal("int:5", ((Multi)ActivatorUtils.Create(typeof(Multi), 5)).Origin);
            Check.Equal("string:hi", ((Multi)ActivatorUtils.Create(typeof(Multi), "hi")).Origin);
        }

        public void TryCreateFailsWhenNoMatch()
        {
            Check.False(ActivatorUtils.TryCreate(typeof(Multi), new object?[] { 1, 2 }, out _));
            Check.Throws<MissingMethodException>(() => ActivatorUtils.Create(typeof(Multi), 1.0, 2.0));
        }

        public void AbstractType_Fails()
        {
            Check.False(ActivatorUtils.TryCreate(typeof(Plugin), Array.Empty<object?>(), out _));
        }

        public void ScanAndCreateInstantiatesParameterless()
        {
            var errors = new List<Type>();
            var plugins = ActivatorUtils.ScanAndCreate<Plugin>(
                Assembly.GetExecutingAssembly(), (t, _) => errors.Add(t));

            var names = new HashSet<string>();
            foreach (var p in plugins) names.Add(p.Name);
            Check.True(names.Contains("alpha") && names.Contains("beta"));
            // NeedsArg has no parameterless ctor -> skipped, not an error.
            Check.False(names.Contains("needsarg"));
        }

        public void NullType_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ActivatorUtils.TryCreate(null!, Array.Empty<object?>(), out _));
        }
    }
}
