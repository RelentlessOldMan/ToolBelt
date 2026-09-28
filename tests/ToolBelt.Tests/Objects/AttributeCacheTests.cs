using System;
using System.Reflection;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    public sealed class AttributeCacheTests
    {
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
        private sealed class LabelAttribute : Attribute
        {
            public string Text { get; }
            public LabelAttribute(string text) => Text = text;
        }

        [Label("on-type")]
        private sealed class Widget
        {
            [Label("on-prop")] public int Size { get; set; }
            public int Plain { get; set; }
        }

        public void GetsTypeAttribute()
        {
            var attr = AttributeCache.Get<LabelAttribute>(typeof(Widget));
            Check.NotNull(attr);
            Check.Equal("on-type", attr!.Text);
        }

        public void GetsMemberAttribute()
        {
            PropertyInfo prop = typeof(Widget).GetProperty(nameof(Widget.Size))!;
            var attr = AttributeCache.Get<LabelAttribute>(prop);
            Check.NotNull(attr);
            Check.Equal("on-prop", attr!.Text);
        }

        public void MissingAttributeIsNull()
        {
            PropertyInfo prop = typeof(Widget).GetProperty(nameof(Widget.Plain))!;
            Check.Equal(null, AttributeCache.Get<LabelAttribute>(prop));
            Check.False(AttributeCache.Has<LabelAttribute>(prop));
        }

        public void CachesSameInstance()
        {
            // Repeated lookups must return the identical cached attribute instance.
            var a = AttributeCache.Get<LabelAttribute>(typeof(Widget));
            var b = AttributeCache.Get<LabelAttribute>(typeof(Widget));
            Check.True(ReferenceEquals(a, b));
        }

        public void NullMember_Throws()
        {
            Check.Throws<ArgumentNullException>(() => AttributeCache.Get<LabelAttribute>(null!));
        }
    }
}
