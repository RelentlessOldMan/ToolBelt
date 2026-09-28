using System.Collections.Generic;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class DeepEqualsTests
    {
        private sealed class Address { public string? City { get; set; } public int Zip { get; set; } }
        private sealed class Person
        {
            public string? Name { get; set; }
            public Address? Home { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
        }
        private sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }

        private static Person Sample() => new Person
        {
            Name = "Ada",
            Home = new Address { City = "London", Zip = 12345 },
            Tags = new List<string> { "a", "b" },
        };

        public void EqualGraphs()
        {
            Check.True(DeepEquals.Equals(Sample(), Sample()));
        }

        public void DifferingLeafIsNotEqual()
        {
            var a = Sample();
            var b = Sample();
            b.Home!.Zip = 99999;
            Check.False(DeepEquals.Equals(a, b));
        }

        public void CollectionOrderMatters()
        {
            var a = Sample();
            var b = Sample();
            b.Tags = new List<string> { "b", "a" };
            Check.False(DeepEquals.Equals(a, b));
        }

        public void NullHandling()
        {
            Check.True(DeepEquals.Equals(null, null));
            Check.False(DeepEquals.Equals(null, Sample()));
            Check.False(DeepEquals.Equals(Sample(), null));
        }

        public void FloatTolerance()
        {
            var opt = new ObjectComparisonOptions { FloatTolerance = 1e-6 };
            Check.True(DeepEquals.Equals(1.0000001, 1.0000002, opt));
            Check.False(DeepEquals.Equals(1.0, 1.1, opt));
        }

        public void DictionaryByContent()
        {
            var a = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
            var b = new Dictionary<string, int> { ["y"] = 2, ["x"] = 1 }; // order-independent
            Check.True(DeepEquals.Equals(a, b));
            b["x"] = 5;
            Check.False(DeepEquals.Equals(a, b));
        }

        public void HandlesCycles()
        {
            var a1 = new Node { Value = 1 };
            a1.Next = a1; // self cycle
            var b1 = new Node { Value = 1 };
            b1.Next = b1;
            Check.True(DeepEquals.Equals(a1, b1));

            var c = new Node { Value = 2 };
            c.Next = c;
            Check.False(DeepEquals.Equals(a1, c));
        }
    }
}
