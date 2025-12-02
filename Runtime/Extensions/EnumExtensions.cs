using System;
using System.Runtime.CompilerServices;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class EnumExtensions
    {
        public static bool HasFlag<T>(this T value, T flag)
            where T : Enum
        {
            var underlying = Convert.ToUInt32(value);
            var mask = Convert.ToUInt32(flag);
            return (underlying & mask) == mask;
        }

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