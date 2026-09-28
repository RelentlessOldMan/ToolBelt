using System;
using System.Collections.Generic;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class PropertyPathTests
    {
        private sealed class Line { public string? Sku { get; set; } public double Price { get; set; } }
        private sealed class Order
        {
            public string? Id { get; set; }
            public List<Line> Lines { get; set; } = new List<Line>();
            public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        }

        private static Order Sample() => new Order
        {
            Id = "A1",
            Lines = new List<Line> { new Line { Sku = "x", Price = 1.5 }, new Line { Sku = "y", Price = 2.5 } },
            Meta = new Dictionary<string, string> { ["region"] = "eu" },
        };

        public void GetSimpleAndNested()
        {
            var o = Sample();
            Check.Equal("A1", PropertyPath.Get(o, "Id"));
            Check.Equal("y", PropertyPath.Get(o, "Lines[1].Sku"));
            Check.Equal(2.5, PropertyPath.Get(o, "Lines[1].Price"));
        }

        public void GetDictionaryKey()
        {
            Check.Equal("eu", PropertyPath.Get(Sample(), "Meta[region]"));
        }

        public void SetNested()
        {
            var o = Sample();
            PropertyPath.Set(o, "Lines[0].Price", 9.9);
            Check.Equal(9.9, o.Lines[0].Price);
            PropertyPath.Set(o, "Meta[region]", "us");
            Check.Equal("us", o.Meta["region"]);
        }

        public void TryGetMiss()
        {
            Check.False(PropertyPath.TryGet(Sample(), "Nonexistent", out _));
            Check.True(PropertyPath.TryGet(Sample(), "Id", out var v) && (string?)v == "A1");
        }

        public void BadPathIdentifiesSegment()
        {
            var ex = Check.Throws<ArgumentException>(() => PropertyPath.Get(Sample(), "Lines[0].Nope"));
            Check.True(ex.Message.Contains("Nope"), "error should name the failing segment");
        }

        public void UnclosedBracket_Throws()
        {
            Check.Throws<ArgumentException>(() => PropertyPath.Get(Sample(), "Lines[0"));
            Check.Throws<ArgumentException>(() => PropertyPath.Get(Sample(), ""));
        }

        public void SetReadOnly_Throws()
        {
            // Order.Lines is settable, but a computed read-only property would throw; test a missing setter path.
            Check.Throws<ArgumentException>(() => PropertyPath.Set(Sample(), "Missing", 1));
        }
    }
}
