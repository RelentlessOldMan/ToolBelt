using System.Collections.Generic;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class FlattenObjectTests
    {
        private sealed class Address { public string? City { get; set; } public int Zip { get; set; } }
        private sealed class Person
        {
            public string? Name { get; set; }
            public Address? Home { get; set; }
            public List<string> Roles { get; set; } = new List<string>();
        }
        private sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }

        public void FlattensNestedAndCollections()
        {
            var p = new Person
            {
                Name = "Ada",
                Home = new Address { City = "London", Zip = 12345 },
                Roles = new List<string> { "admin", "user" },
            };
            var flat = FlattenObject.Flatten(p);

            Check.Equal("Ada", flat["Name"]);
            Check.Equal("London", flat["Home.City"]);
            Check.Equal(12345, flat["Home.Zip"]);
            Check.Equal("admin", flat["Roles[0]"]);
            Check.Equal("user", flat["Roles[1]"]);
        }

        public void FlattensDictionary()
        {
            var dict = new Dictionary<string, object?>
            {
                ["a"] = 1,
                ["b"] = new Dictionary<string, object?> { ["c"] = 2 },
            };
            var flat = FlattenObject.Flatten(dict);
            Check.Equal(1, flat["[a]"]);
            Check.Equal(2, flat["[b][c]"]);
        }

        public void CustomSeparator()
        {
            var p = new Person { Home = new Address { City = "Rome" } };
            var flat = FlattenObject.Flatten(p, "/");
            Check.Equal("Rome", flat["Home/City"]);
        }

        public void NullMemberRecorded()
        {
            var p = new Person { Name = null };
            var flat = FlattenObject.Flatten(p);
            Check.True(flat.ContainsKey("Name"));
            Check.Equal(null, flat["Name"]);
        }

        public void CycleDoesNotInfiniteLoop()
        {
            var n = new Node { Value = 1 };
            n.Next = n; // self-reference
            var flat = FlattenObject.Flatten(n);
            Check.Equal(1, flat["Value"]);
            // Next resolves to the already-seen reference and is recorded rather than expanded again.
            Check.True(flat.ContainsKey("Next"));
        }
    }
}
