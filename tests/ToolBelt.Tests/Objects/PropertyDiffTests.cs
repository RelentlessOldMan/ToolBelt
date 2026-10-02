using System.Collections.Generic;
using System.Linq;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class PropertyDiffTests
    {
        private sealed class Address { public string? City { get; set; } public int Zip { get; set; } }
        private sealed class Person
        {
            public string? Name { get; set; }
            public Address? Home { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
        }

        private static Person Sample() => new Person
        {
            Name = "Ada",
            Home = new Address { City = "London", Zip = 12345 },
            Tags = new List<string> { "a", "b" },
        };

        public void NoDifferencesWhenEqual()
        {
            Check.Equal(0, PropertyDiff.Compare(Sample(), Sample()).Count);
        }

        public void NestedLeafDifference()
        {
            var a = Sample();
            var b = Sample();
            b.Home!.City = "Paris";
            var diffs = PropertyDiff.Compare(a, b);
            Check.Equal(1, diffs.Count);
            Check.Equal("Home.City", diffs[0].Path);
            Check.Equal("London", diffs[0].OldValue);
            Check.Equal("Paris", diffs[0].NewValue);
        }

        public void CollectionElementDifference()
        {
            var a = Sample();
            var b = Sample();
            b.Tags[1] = "z";
            var diffs = PropertyDiff.Compare(a, b);
            Check.Equal(1, diffs.Count);
            Check.Equal("Tags[1]", diffs[0].Path);
        }

        public void CollectionLengthDifference()
        {
            var a = Sample();
            var b = Sample();
            b.Tags.Add("c");
            var diffs = PropertyDiff.Compare(a, b);
            Check.Equal(1, diffs.Count);
            Check.Equal("Tags[2]", diffs[0].Path);
            Check.Equal(null, diffs[0].OldValue);
            Check.Equal("c", diffs[0].NewValue);
        }

        public void DictionaryDifferences()
        {
            var a = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
            var b = new Dictionary<string, int> { ["x"] = 1, ["y"] = 9, ["z"] = 3 };
            var diffs = PropertyDiff.Compare(a, b);
            Check.True(diffs.Any(d => d.Path == "[y]"));
            Check.True(diffs.Any(d => d.Path == "[z]"));
            Check.Equal(2, diffs.Count);
        }

        // Consistency: DeepEquals is true iff PropertyDiff finds no differences.
        public void Property_AgreesWithDeepEquals()
        {
            var a = Sample();
            var b = Sample();
            Check.Equal(DeepEquals.Equals(a, b), PropertyDiff.Compare(a, b).Count == 0);

            b.Name = "Grace";
            b.Home!.Zip = 5;
            Check.Equal(DeepEquals.Equals(a, b), PropertyDiff.Compare(a, b).Count == 0);
            Check.Equal(2, PropertyDiff.Compare(a, b).Count);
        }

        public void Difference_ToString_RendersPathAndValues()
        {
            // ToString is the human-facing audit-log line; exercise both the value and the null-substitution branches.
            var a = Sample();
            var b = Sample();
            b.Home!.City = "Paris";
            Check.Equal("Home.City: London -> Paris", PropertyDiff.Compare(a, b)[0].ToString());

            var c = Sample();
            c.Tags.Add("c"); // added element: OldValue is null → "null" substitution
            Check.Equal("Tags[2]: null -> c", PropertyDiff.Compare(a, c)[0].ToString());
        }
    }
}
