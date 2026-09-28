using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class ObjectMapperTests
    {
        private sealed class Source
        {
            public string? Name { get; set; }
            public int Count { get; set; }
            public string Amount { get; set; } = "3.5";
            public string? Secret { get; set; }
        }

        private sealed class Target
        {
            public string? Name { get; set; }
            public long Count { get; set; }     // widened from int
            public double Amount { get; set; }   // parsed from string
            public string? Secret { get; set; }
            public string? Extra { get; set; }
        }

        public void MapsMatchingPropertiesWithConversion()
        {
            var src = new Source { Name = "Ada", Count = 7, Amount = "3.5", Secret = "hidden" };
            var dto = new ObjectMapper<Source, Target>().Map(src);

            Check.Equal("Ada", dto.Name);
            Check.Equal(7L, dto.Count);          // int -> long
            Check.Close(3.5, dto.Amount, 1e-9);  // "3.5" -> double
            Check.Equal(null, dto.Extra);        // no source, left default
        }

        public void IgnoreSkipsProperty()
        {
            var src = new Source { Secret = "hidden" };
            var dto = new ObjectMapper<Source, Target>().Ignore("Secret").Map(src);
            Check.Equal(null, dto.Secret);
        }

        public void ForMemberOverrides()
        {
            var src = new Source { Name = "ada" };
            var dto = new ObjectMapper<Source, Target>()
                .ForMember("Name", s => s.Name?.ToUpperInvariant())
                .Map(src);
            Check.Equal("ADA", dto.Name);
        }

        public void MapIntoExistingInstance()
        {
            var src = new Source { Name = "Grace", Count = 2 };
            var existing = new Target { Extra = "keep" };
            new ObjectMapper<Source, Target>().Map(src, existing);
            Check.Equal("Grace", existing.Name);
            Check.Equal(2L, existing.Count);
            Check.Equal("keep", existing.Extra); // untouched (no matching source)
        }
    }
}
