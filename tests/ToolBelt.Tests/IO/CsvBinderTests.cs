using System;
using System.Linq;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class CsvBinderTests
    {
        public sealed class Person
        {
            public string? Name { get; set; }
            public int Age { get; set; }
            public bool Active { get; set; }
        }

        private static readonly string[] Header = { "Name", "Age", "Active" };

        public void BindConvertsTypes()
        {
            var binder = new CsvBinder<Person>(Header);
            var p = binder.Bind(new[] { "Ada", "30", "true" });
            Check.Equal("Ada", p.Name);
            Check.Equal(30, p.Age);
            Check.True(p.Active);
        }

        public void HeaderMatchingIsCaseInsensitive()
        {
            var binder = new CsvBinder<Person>(new[] { "name", "AGE", "active" });
            var p = binder.Bind(new[] { "Grace", "45", "false" });
            Check.Equal("Grace", p.Name);
            Check.Equal(45, p.Age);
            Check.False(p.Active);
        }

        public void ToFieldsExtractsInHeaderOrder()
        {
            var binder = new CsvBinder<Person>(Header);
            var fields = binder.ToFields(new Person { Name = "Ada", Age = 30, Active = true });
            Check.True(fields.SequenceEqual(new[] { "Ada", "30", "True" }));
        }

        public void UnknownColumnIgnored()
        {
            var binder = new CsvBinder<Person>(new[] { "Name", "Age", "Active", "Extra" });
            var p = binder.Bind(new[] { "Ada", "30", "true", "ignored" });
            Check.Equal("Ada", p.Name);
        }

        public void RoundTripThroughCsvLine()
        {
            var binder = new CsvBinder<Person>(Header);
            var original = new Person { Name = "O'Brien, Jr", Age = 22, Active = true };
            string line = CsvLine.Format(binder.ToFields(original));
            var parsed = binder.Bind(CsvLine.Parse(line));
            Check.Equal(original.Name, parsed.Name);
            Check.Equal(original.Age, parsed.Age);
            Check.Equal(original.Active, parsed.Active);
        }

        public void BadValue_Throws()
        {
            var binder = new CsvBinder<Person>(Header);
            var ex = Check.Throws<FormatException>(() => binder.Bind(new[] { "Ada", "notanumber", "true" }));
            Check.True(ex.Message.Contains("Age"), "error should name the column");
        }

        public void WrongFieldCount_Throws()
        {
            var binder = new CsvBinder<Person>(Header);
            Check.Throws<ArgumentException>(() => binder.Bind(new[] { "Ada", "30" }));
        }
    }
}
