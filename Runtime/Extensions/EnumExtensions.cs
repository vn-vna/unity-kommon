using System;
using System.Runtime.CompilerServices;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for enum types including flag operations.
    /// </summary>
    /// <example>
    /// <code>
    /// [Flags]
    /// enum MyFlags { None = 0, A = 1, B = 2, C = 4 }
    /// 
    /// MyFlags flags = MyFlags.A | MyFlags.C;
    /// bool hasA = flags.HasFlag(MyFlags.A); // true
    /// int count = flags.CountFlagOns(); // 2
    /// </code>
    /// </example>
    public static class EnumExtensions
    {
        /// <summary>
        /// Determines whether a specific flag is set in the enum value.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="value">The enum value to check.</param>
        /// <param name="flag">The flag to check for.</param>
        /// <returns>True if the flag is set; otherwise, false.</returns>
        public static bool HasFlag<T>(this T value, T flag)
            where T : Enum
        {
            var underlying = Convert.ToUInt32(value);
            var mask = Convert.ToUInt32(flag);
            return (underlying & mask) == mask;
        }

        /// <summary>
        /// Counts the number of flags that are set (bits that are 1) in the enum value.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="value">The enum value to count flags in.</param>
        /// <returns>The number of set flags.</returns>
        public static int CountFlagOns<T>(this T value)
            where T : Enum
        {
            var underlying = Convert.ToUInt32(value);
            int count = 0;
            while (underlying != 0)
            {
                count += (int)(underlying & 1);
                underlying >>= 1;
            }
            return count;
        }
    }
}