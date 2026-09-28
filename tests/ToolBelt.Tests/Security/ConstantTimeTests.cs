using System;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Security
{
    public sealed class ConstantTimeTests
    {
        public void EqualArrays()
        {
            Check.True(ConstantTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));
            Check.True(ConstantTime.Equals(Array.Empty<byte>(), Array.Empty<byte>()));
        }

        public void DifferingContentOrLength()
        {
            Check.False(ConstantTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }));
            Check.False(ConstantTime.Equals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }));
        }

        public void Strings()
        {
            Check.True(ConstantTime.Equals("token-abc", "token-abc"));
            Check.False(ConstantTime.Equals("token-abc", "token-abd"));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ConstantTime.Equals((byte[])null!, new byte[0]));
            Check.Throws<ArgumentNullException>(() => ConstantTime.Equals("a", (string)null!));
        }
    }
}
