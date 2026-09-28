using System;
using System.Linq;
using ToolBelt.Enums;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Enums
{
    public sealed class EnumMapTests
    {
        private enum Color { Red, Green, Blue }

        public void DefaultValue_AllSlotsPresent()
        {
            var map = new EnumMap<Color, int>(defaultValue: 7);
            Check.Equal(3, map.Count);
            Check.Equal(7, map[Color.Red]);
            Check.Equal(7, map[Color.Green]);
            Check.Equal(7, map[Color.Blue]);
        }

        public void SetAndGet()
        {
            var map = new EnumMap<Color, string>("?");
            map[Color.Green] = "go";
            Check.Equal("go", map[Color.Green]);
            Check.Equal("?", map[Color.Red]); // others unchanged
        }

        public void FactoryInitialization()
        {
            var map = new EnumMap<Color, string>(c => c.ToString().ToLowerInvariant());
            Check.Equal("red", map[Color.Red]);
            Check.Equal("blue", map[Color.Blue]);
        }

        public void Keys_AreAllMembers()
        {
            var map = new EnumMap<Color, int>();
            Check.True(map.Keys.SequenceEqual(new[] { Color.Red, Color.Green, Color.Blue }));
        }

        public void Entries_EnumerateAll()
        {
            var map = new EnumMap<Color, int>(1);
            map[Color.Blue] = 9;
            var entries = map.ToDictionary(kv => kv.Key, kv => kv.Value);
            Check.Equal(3, entries.Count);
            Check.Equal(1, entries[Color.Red]);
            Check.Equal(9, entries[Color.Blue]);
        }

        public void TryGetValue()
        {
            var map = new EnumMap<Color, int>(4);
            Check.True(map.TryGetValue(Color.Red, out int v) && v == 4);
            Check.False(map.TryGetValue((Color)999, out _)); // undefined member
        }

        public void UndefinedMember_IndexerThrows()
        {
            var map = new EnumMap<Color, int>();
            Check.Throws<ArgumentOutOfRangeException>(() => _ = map[(Color)999]);
        }

        public void NullFactory_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new EnumMap<Color, int>((Func<Color, int>)null!));
        }
    }
}
