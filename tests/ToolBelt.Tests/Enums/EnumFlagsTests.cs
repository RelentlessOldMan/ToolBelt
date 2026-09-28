using System;
using ToolBelt.Enums;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Enums
{
    public sealed class EnumFlagsTests
    {
        [Flags]
        private enum Access
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
            All = Read | Write | Execute,
        }

        public void HasAllAndAny()
        {
            var rw = Access.Read | Access.Write;
            Check.True(EnumFlags.HasAllFlags(rw, Access.Read));
            Check.True(EnumFlags.HasAllFlags(rw, Access.Read | Access.Write));
            Check.False(EnumFlags.HasAllFlags(rw, Access.Execute));
            Check.True(EnumFlags.HasAnyFlags(rw, Access.Execute | Access.Read));
            Check.False(EnumFlags.HasAnyFlags(rw, Access.Execute));
        }

        public void AddRemoveToggle()
        {
            Check.Equal(Access.Read | Access.Write, EnumFlags.Add(Access.Read, Access.Write));
            Check.Equal(Access.Read, EnumFlags.Remove(Access.Read | Access.Write, Access.Write));
            Check.Equal(Access.Write, EnumFlags.Toggle(Access.Read | Access.Write, Access.Read));
            Check.Equal(Access.Read | Access.Write, EnumFlags.Toggle(Access.Read, Access.Write));
        }

        public void Combine()
        {
            Check.Equal(Access.All, EnumFlags.Combine(Access.Read, Access.Write, Access.Execute));
            Check.Equal(Access.None, EnumFlags.Combine<Access>());
        }

        public void Idempotent_AddRemove()
        {
            var v = Access.Read;
            Check.Equal(v, EnumFlags.Add(v, Access.Read));                 // adding present flag is a no-op
            Check.Equal(v, EnumFlags.Remove(EnumFlags.Add(v, Access.Write), Access.Write));
        }
    }
}
