using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class DataTableLiteTests
    {
        private static DataTableLite People()
        {
            var t = new DataTableLite(new[] { "Name", "Age", "City" });
            t.AddRow("Ada", 36, "London");
            t.AddRow("Grace", 45, "New York");
            t.AddRow("Alan", 41, "London");
            t.AddRow("Edsger", 54, "Austin");
            return t;
        }

        public void ConstructAndShape()
        {
            var t = People();
            Check.Equal(3, t.ColumnCount);
            Check.Equal(4, t.RowCount);
            Check.True(t.Columns.SequenceEqual(new[] { "Name", "Age", "City" }));
            Check.True(t.HasColumn("Age"));
            Check.False(t.HasColumn("Nope"));
            Check.Equal(1, t.IndexOf("Age"));
            Check.Equal(-1, t.IndexOf("Nope"));
        }

        public void Indexers()
        {
            var t = People();
            Check.Equal("Ada", t[0, "Name"]);
            Check.Equal(45, t[1, "Age"]);
            Check.Equal("Austin", t[3, 2]);
            Check.Throws<ArgumentException>(() => { var _ = t[0, "Missing"]; });
        }

        public void AddRow_WrongLength_Throws()
        {
            var t = new DataTableLite(new[] { "A", "B" });
            Check.Throws<ArgumentException>(() => t.AddRow(1, 2, 3));
            Check.Throws<ArgumentException>(() => t.AddRow(1));
        }

        public void Constructor_Validates()
        {
            Check.Throws<ArgumentException>(() => new DataTableLite(Array.Empty<string>()));
            Check.Throws<ArgumentException>(() => new DataTableLite(new[] { "A", "A" }));
            Check.Throws<ArgumentException>(() => new DataTableLite(new string[] { "A", null! }));
        }

        public void Select_ProjectsAndReorders()
        {
            var t = People().Select("City", "Name");
            Check.True(t.Columns.SequenceEqual(new[] { "City", "Name" }));
            Check.Equal("London", t[0, "City"]);
            Check.Equal("Ada", t[0, "Name"]);
            Check.Throws<ArgumentException>(() => People().Select("Nope"));
        }

        public void Where_FiltersByColumnName()
        {
            var londoners = People().Where(r => (string)r["City"]! == "London");
            Check.Equal(2, londoners.RowCount);
            Check.Equal("Ada", londoners[0, "Name"]);
            Check.Equal("Alan", londoners[1, "Name"]);
        }

        public void OrderBy_IsStableAndHandlesDescending()
        {
            var byCityThenInput = People().OrderBy("City");
            // Austin, London, London, New York — the two Londoners keep input order (Ada before Alan).
            Check.True(Column(byCityThenInput, "Name").SequenceEqual(new[] { "Edsger", "Ada", "Alan", "Grace" }));

            var byAgeDesc = People().OrderByDescending("Age");
            Check.True(Column(byAgeDesc, "Name").SequenceEqual(new[] { "Edsger", "Grace", "Alan", "Ada" }));
        }

        public void OrderBy_NullsSortFirstAscending()
        {
            var t = new DataTableLite(new[] { "K" });
            t.AddRow(new object?[] { 3 });
            t.AddRow(new object?[] { null });
            t.AddRow(new object?[] { 1 });
            var sorted = t.OrderBy("K");
            Check.Null(sorted[0, "K"]);
            Check.Equal(1, sorted[1, "K"]);
            Check.Equal(3, sorted[2, "K"]);
        }

        public void Distinct_RemovesDuplicateRows()
        {
            var t = new DataTableLite(new[] { "A", "B" });
            t.AddRow(1, "x");
            t.AddRow(1, "x");
            t.AddRow(1, "y");
            t.AddRow(1, "x");
            var d = t.Distinct();
            Check.Equal(2, d.RowCount);
            Check.Equal(1, d[0, "A"]);
            Check.Equal("x", d[0, "B"]);
            Check.Equal("y", d[1, "B"]);
        }

        public void GroupBy_PreservesFirstSeenKeyOrder()
        {
            var groups = People().GroupBy("City");
            Check.True(groups.Select(g => (string)g.Key!).SequenceEqual(new[] { "London", "New York", "Austin" }));
            var london = groups.First(g => (string)g.Key! == "London");
            Check.Equal(2, london.Rows.RowCount);
        }

        public void GroupBy_NullKeyFormsItsOwnGroup()
        {
            var t = new DataTableLite(new[] { "K", "V" });
            t.AddRow("a", 1);
            t.AddRow(null, 2);
            t.AddRow("a", 3);
            t.AddRow(null, 4);
            var groups = t.GroupBy("K");
            Check.Equal(2, groups.Count);
            var nullGroup = groups.First(g => g.Key is null);
            Check.Equal(2, nullGroup.Rows.RowCount);
        }

        public void InnerJoin_MatchesOnKeys()
        {
            var people = new DataTableLite(new[] { "Name", "DeptId" });
            people.AddRow("Ada", 1);
            people.AddRow("Grace", 2);
            people.AddRow("Alan", 1);
            people.AddRow("Nobody", 99); // no matching dept -> dropped

            var depts = new DataTableLite(new[] { "Id", "Dept" });
            depts.AddRow(1, "Research");
            depts.AddRow(2, "Ops");

            var joined = people.InnerJoin(depts, "DeptId", "Id");
            Check.True(joined.Columns.SequenceEqual(new[] { "Name", "DeptId", "Id", "Dept" }));
            Check.Equal(3, joined.RowCount); // Ada, Grace, Alan; Nobody dropped
            var names = Column(joined, "Name").Cast<string>().OrderBy(s => s).ToArray();
            Check.True(names.SequenceEqual(new[] { "Ada", "Alan", "Grace" }));
        }

        public void InnerJoin_CollidingColumnName_Throws()
        {
            var a = new DataTableLite(new[] { "Id", "X" });
            a.AddRow(1, "a");
            var b = new DataTableLite(new[] { "Id", "Y" });
            b.AddRow(1, "b");
            // Both have "Id" -> collision without a prefix.
            Check.Throws<ArgumentException>(() => a.InnerJoin(b, "Id", "Id"));
            // With a prefix it works.
            var joined = a.InnerJoin(b, "Id", "Id", rightPrefix: "r_");
            Check.True(joined.Columns.SequenceEqual(new[] { "Id", "X", "r_Id", "r_Y" }));
            Check.Equal(1, joined.RowCount);
        }

        public void InnerJoin_NullKeysNeverMatch()
        {
            var a = new DataTableLite(new[] { "K", "V" });
            a.AddRow(new object?[] { null, "a" });
            var b = new DataTableLite(new[] { "K", "W" });
            b.AddRow(new object?[] { null, "b" });
            Check.Equal(0, a.InnerJoin(b, "K", "K", rightPrefix: "r_").RowCount);
        }

        public void Queries_DoNotMutateSource()
        {
            var t = People();
            int before = t.RowCount;
            _ = t.Where(r => false);
            _ = t.OrderBy("Age");
            _ = t.Select("Name");
            _ = t.Distinct();
            Check.Equal(before, t.RowCount);
            Check.Equal("Ada", t[0, "Name"]); // untouched
        }

        public void Property_WhereOrderBy_AgreesWithLinq()
        {
            var t = People();
            var expected = Enumerable.Range(0, t.RowCount)
                .Select(i => new { Name = (string)t[i, "Name"]!, Age = (int)t[i, "Age"]! })
                .Where(p => p.Age >= 41)
                .OrderBy(p => p.Age)
                .Select(p => p.Name)
                .ToArray();
            var actual = Column(t.Where(r => (int)r["Age"]! >= 41).OrderBy("Age"), "Name");
            Check.True(actual.SequenceEqual(expected));
        }

        public void ToString_ContainsHeaderAndData()
        {
            string s = People().ToString();
            Check.True(s.Contains("Name"));
            Check.True(s.Contains("Grace"));
        }

        private static List<object?> Column(DataTableLite t, string name)
        {
            var list = new List<object?>();
            for (int i = 0; i < t.RowCount; i++) list.Add(t[i, name]);
            return list;
        }
    }
}
