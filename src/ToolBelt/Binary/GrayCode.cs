// ToolBelt drop-in — fully self-contained (BCL only).
namespace ToolBelt.Binary
{
    /// <summary>
    /// Reflected binary (Gray) code conversion. Consecutive integers map to Gray codes that differ in
    /// exactly one bit, which is why Gray codes are used in rotary encoders and Karnaugh maps.
    /// <see cref="FromGray(uint)"/> is the exact inverse of <see cref="ToGray(uint)"/>.
    /// </summary>
    public static class GrayCode
    {
        /// <summary>Converts a 32-bit value to its Gray code.</summary>
        public static uint ToGray(uint value) => value ^ (value >> 1);

        /// <summary>Converts a 32-bit Gray code back to its value.</summary>
        public static uint FromGray(uint gray)
        {
            uint value = gray;
            for (uint mask = value >> 1; mask != 0; mask >>= 1)
                value ^= mask;
            return value;
        }

        /// <summary>Converts a 64-bit value to its Gray code.</summary>
        public static ulong ToGray(ulong value) => value ^ (value >> 1);

        /// <summary>Converts a 64-bit Gray code back to its value.</summary>
        public static ulong FromGray(ulong gray)
        {
            ulong value = gray;
            for (ulong mask = value >> 1; mask != 0; mask >>= 1)
                value ^= mask;
            return value;
        }
    }
}
