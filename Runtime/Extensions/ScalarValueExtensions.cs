using System;
using System.Globalization;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class ScalarValueExtensions
    {
        public static Quaternion AsXRotation(this int angle)
        {
            return Quaternion.Euler(angle, 0, 0);
        }

        public static Vector3 AsXRotation(this float angle)
        {
            return new Vector3(angle, 0, 0);
        }

        public static Quaternion AsYRotation(this int angle)
        {
            return Quaternion.Euler(0, angle, 0);
        }

        public static Vector3 AsYRotation(this float angle)
        {
            return new Vector3(0, angle, 0);
        }

        public static Quaternion AsZRotation(this int angle)
        {
            return Quaternion.Euler(0, 0, angle);
        }

        public static Quaternion AsZRotation(this float angle)
        {
            return Quaternion.Euler(0, 0, angle);
        }

        public static float ToFLoat(this string s, IFormatProvider provider = null, float defaultValue = 0.0f)
        {
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            return float.Parse(s, provider ?? CultureInfo.InvariantCulture);
        }

        public static int ToInt(this string s, IFormatProvider provider = null, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            return int.Parse(s, provider ?? CultureInfo.InvariantCulture);
        }

        public static long ToLong(this string s, IFormatProvider provider = null, long defaultValue = 0)
        {
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            return long.Parse(s, provider ?? CultureInfo.InvariantCulture);
        }

        public static double ToDouble(this string s, IFormatProvider provider = null, double defaultValue = 0.0)
        {
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            return double.Parse(s, provider ?? CultureInfo.InvariantCulture);
        }

        public static float ToFloat(this string s, IFormatProvider provider = null, float defaultValue = 0.0f)
        {
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            return float.Parse(s, provider ?? CultureInfo.InvariantCulture);
        }

        public static string ToReadableString(this int value)
        {
            if (value < 10000)
            {
                return value.ToString();
            }
            else if (value < 1000000)
            {
                return $"{value / 1000f:0.#}K";
            }
            else if (value < 1000000000)
            {
                return $"{value / 1000000f:0.#}M";
            }
            else
            {
                return $"{value / 1000000000f:0.#}B";
            }
        }

        public static string ToReadableString(this float value)
        {
            if (value < 10000)
            {
                return value.ToString();
            }
            else if (value < 1000000)
            {
                return $"{value / 1000f:0.#}K";
            }
            else if (value < 1000000000)
            {
                return $"{value / 1000000f:0.#}M";
            }
            else
            {
                return $"{value / 1000000000f:0.#}B";
            }
        }

        public static string ToReadableString(this long value)
        {
            if (value < 10000)
            {
                return value.ToString();
            }
            else if (value < 1000000)
            {
                return $"{value / 1000f:0.#}K";
            }
            else if (value < 1000000000)
            {
                return $"{value / 1000000f:0.#}M";
            }
            else
            {
                return $"{value / 1000000000f:0.#}B";
            }
        }

        public static string ToReadableString(this double value)
        {
            if (value < 10000)
            {
                return value.ToString();
            }
            else if (value < 1000000)
            {
                return $"{value / 1000f:0.#}K";
            }
            else if (value < 1000000000)
            {
                return $"{value / 1000000f:0.#}M";
            }
            else
            {
                return $"{value / 1000000000f:0.#}B";
            }
        }

        public static bool InRange<T>(this T value, T min, T max) where T : IComparable<T>
        {
            return value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
        }

        public static bool OutRange<T>(this T value, T min, T max) where T : IComparable<T>
        {
            return value.CompareTo(min) < 0 || value.CompareTo(max) > 0;
        }

        public static Vector2Int ToPositionOnGrid(this int index, int gridWith)
        {
            if (gridWith <= 0) throw new ArgumentOutOfRangeException(nameof(gridWith), "Grid width must be greater than zero.");
            return new Vector2Int(index % gridWith, index / gridWith);
        }

    }
}