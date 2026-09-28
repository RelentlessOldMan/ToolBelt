using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class TypeUtilsTests
    {
        private abstract class Shape { }
        private sealed class Circle : Shape { }
        private sealed class Square : Shape { }

        [AttributeUsage(AttributeTargets.Class)]
        private sealed class TaggedAttribute : Attribute { }
        [Tagged] private sealed class Marked { }

        private enum Color { Red, Green, Blue }

        public void FriendlyName()
        {
            Check.Equal("int", TypeUtils.FriendlyName(typeof(int)));
            Check.Equal("int?", TypeUtils.FriendlyName(typeof(int?)));
            Check.Equal("string[]", TypeUtils.FriendlyName(typeof(string[])));
            Check.Equal("int[,]", TypeUtils.FriendlyName(typeof(int[,])));
            Check.Equal("Dictionary<string, List<int>>",
                TypeUtils.FriendlyName(typeof(Dictionary<string, List<int>>)));
        }

        public void DefaultValue()
        {
            Check.Equal(0, TypeUtils.DefaultValue(typeof(int)));
            Check.Equal(null, TypeUtils.DefaultValue(typeof(string)));
            Check.Equal(null, TypeUtils.DefaultValue(typeof(int?)));
        }

        public void UnwrapNullableAndNumeric()
        {
            Check.Equal(typeof(int), TypeUtils.UnwrapNullable(typeof(int?)));
            Check.True(TypeUtils.IsNumeric(typeof(double)));
            Check.True(TypeUtils.IsNumeric(typeof(int?)));
            Check.False(TypeUtils.IsNumeric(typeof(string)));
            Check.False(TypeUtils.IsNumeric(typeof(char)));
        }

        public void TryConvert()
        {
            Check.True(TypeUtils.TryConvert("42", typeof(int), out var n) && (int)n! == 42);
            Check.True(TypeUtils.TryConvert(42, typeof(long), out var l) && (long)l! == 42L);
            Check.True(TypeUtils.TryConvert("Green", typeof(Color), out var c) && (Color)c! == Color.Green);
            Check.True(TypeUtils.TryConvert(2, typeof(Color), out var c2) && (Color)c2! == Color.Blue);
            Check.False(TypeUtils.TryConvert("notanumber", typeof(int), out _));
            Check.Equal(-1, TypeUtils.ConvertOrDefault("bad", typeof(int), -1));
        }

        public void TryConvertNulls()
        {
            Check.True(TypeUtils.TryConvert(null, typeof(string), out var s) && s == null);
            Check.True(TypeUtils.TryConvert(null, typeof(int?), out _));
            Check.False(TypeUtils.TryConvert(null, typeof(int), out _)); // null into non-nullable value type
        }

        public void GetDerivedTypes()
        {
            var derived = TypeUtils.GetDerivedTypes(typeof(Shape), Assembly.GetExecutingAssembly());
            Check.True(derived.Contains(typeof(Circle)));
            Check.True(derived.Contains(typeof(Square)));
            Check.False(derived.Contains(typeof(Shape))); // abstract base excluded
        }

        public void GetTypesWithAttribute()
        {
            var tagged = TypeUtils.GetTypesWithAttribute<TaggedAttribute>(Assembly.GetExecutingAssembly());
            Check.True(tagged.Contains(typeof(Marked)));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => TypeUtils.FriendlyName(null!));
            Check.Throws<ArgumentNullException>(() => TypeUtils.IsNumeric(null!));
        }
    }
}
