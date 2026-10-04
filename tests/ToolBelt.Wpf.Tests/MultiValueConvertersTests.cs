using System;
using System.Globalization;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class MultiValueConvertersTests
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public void And_RequiresAllTrue()
        {
            var c = new BooleanAndConverter();
            Check.Equal(true, (bool)c.Convert(new object?[] { true, true, true }, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.Convert(new object?[] { true, false, true }, typeof(bool), null, Ci)!);
            // Non-bool / null count as false.
            Check.Equal(false, (bool)c.Convert(new object?[] { true, null }, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.Convert(new object?[] { true, "x" }, typeof(bool), null, Ci)!);
            // Vacuous truth for an empty set.
            Check.Equal(true, (bool)c.Convert(Array.Empty<object?>(), typeof(bool), null, Ci)!);
            Check.Null(c.ConvertBack(true, new[] { typeof(bool) }, null, Ci));
        }

        public void Or_RequiresAnyTrue()
        {
            var c = new BooleanOrConverter();
            Check.Equal(true, (bool)c.Convert(new object?[] { false, false, true }, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.Convert(new object?[] { false, false }, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.Convert(new object?[] { null, "x" }, typeof(bool), null, Ci)!);
            Check.Equal(false, (bool)c.Convert(Array.Empty<object?>(), typeof(bool), null, Ci)!);
            Check.Null(c.ConvertBack(true, new[] { typeof(bool) }, null, Ci));
        }
    }
}
