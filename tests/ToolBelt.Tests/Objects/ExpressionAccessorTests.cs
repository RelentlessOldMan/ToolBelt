using System;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class ExpressionAccessorTests
    {
        private sealed class Poco
        {
            public int Number { get; set; }
            public string Name { get; set; } = "";
            public double Ratio = 0.0;        // public field (written via accessor/reflection in tests)
            public int ReadOnly { get; } = 42; // no setter
        }

        public void GetSet_RoundTripsProperty()
        {
            var p = new Poco();
            ExpressionAccessor.SetValue(p, "Number", 7);
            ExpressionAccessor.SetValue(p, "Name", "hello");
            Check.Equal(7, (int)ExpressionAccessor.GetValue(p, "Number")!);
            Check.Equal("hello", (string)ExpressionAccessor.GetValue(p, "Name")!);
        }

        public void GetSet_WorksOnPublicField()
        {
            var p = new Poco();
            ExpressionAccessor.SetValue(p, "Ratio", 1.5);
            Check.Close(1.5, (double)ExpressionAccessor.GetValue(p, "Ratio")!);
        }

        public void Typed_FastPath_NoBoxing()
        {
            var get = ExpressionAccessor.Getter<Poco, int>("Number");
            var set = ExpressionAccessor.Setter<Poco, int>("Number");
            var p = new Poco();
            set(p, 99);
            Check.Equal(99, get(p));
            Check.Equal(99, p.Number);
        }

        public void Delegates_AreCachedByIdentity()
        {
            var a = ExpressionAccessor.Getter(typeof(Poco), "Number");
            var b = ExpressionAccessor.Getter(typeof(Poco), "Number");
            Check.True(ReferenceEquals(a, b), "object getter cached");

            var g1 = ExpressionAccessor.Getter<Poco, int>("Number");
            var g2 = ExpressionAccessor.Getter<Poco, int>("Number");
            Check.True(ReferenceEquals(g1, g2), "typed getter cached");
        }

        public void ReadOnlyProperty_SetterThrows()
        {
            Check.Throws<InvalidOperationException>(() => ExpressionAccessor.Setter(typeof(Poco), "ReadOnly"));
            // but it is readable
            var p = new Poco();
            Check.Equal(42, (int)ExpressionAccessor.GetValue(p, "ReadOnly")!);
        }

        public void UnknownMember_Throws()
        {
            Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(typeof(Poco), "Nope"));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Getter(null!, "x"));
            Check.Throws<ArgumentNullException>(() => ExpressionAccessor.Getter(typeof(Poco), null!));
            Check.Throws<ArgumentNullException>(() => ExpressionAccessor.GetValue(null!, "x"));
            Check.Throws<ArgumentNullException>(() => ExpressionAccessor.SetValue(null!, "x", 1));
        }

        public void DifferentialVsReflection()
        {
            var rng = new DeterministicRandom(42424242);
            PropertyInfo numberProp = typeof(Poco).GetProperty("Number")!;
            PropertyInfo nameProp = typeof(Poco).GetProperty("Name")!;
            FieldInfo ratioField = typeof(Poco).GetField("Ratio")!;

            for (int trial = 0; trial < 500; trial++)
            {
                var p = new Poco();
                int num = rng.Next(-1000, 1000);
                string name = "n" + rng.Next(0, 10000);
                double ratio = rng.NextDouble() * 100;

                // Write via ExpressionAccessor, read via reflection.
                ExpressionAccessor.SetValue(p, "Number", num);
                ExpressionAccessor.SetValue(p, "Name", name);
                ExpressionAccessor.SetValue(p, "Ratio", ratio);
                Check.Equal(num, (int)numberProp.GetValue(p)!);
                Check.Equal(name, (string)nameProp.GetValue(p)!);
                Check.Close(ratio, (double)ratioField.GetValue(p)!);

                // Write via reflection, read via ExpressionAccessor.
                var q = new Poco();
                numberProp.SetValue(q, num);
                nameProp.SetValue(q, name);
                ratioField.SetValue(q, ratio);
                Check.Equal(num, (int)ExpressionAccessor.GetValue(q, "Number")!);
                Check.Equal(name, (string)ExpressionAccessor.GetValue(q, "Name")!);
                Check.Close(ratio, (double)ExpressionAccessor.GetValue(q, "Ratio")!);
            }
        }
    }
}
