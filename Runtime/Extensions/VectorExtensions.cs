using System;
using System.Globalization;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class VectorExtensions
    {
        public static (float, float) ToTuple(this Vector2 vector)
        {
            return (vector.x, vector.y);
        }

        public static (int, int) ToTuple(this Vector2Int vector)
        {
            return (vector.x, vector.y);
        }

        public static (float, float, float) ToTuple(this Vector3 vector)
        {
            return (vector.x, vector.y, vector.z);
        }

        public static (int, int, int) ToTuple(this Vector3Int vector)
        {
            return (vector.x, vector.y, vector.z);
        }

        public static void Deconstruct(this Vector2 vector, out float x, out float y)
        {
            x = vector.x;
            y = vector.y;
        }

        public static void Deconstruct(this Vector2Int vector, out int x, out int y)
        {
            x = vector.x;
            y = vector.y;
        }

        public static void Deconstruct(this Vector3 vector, out float x, out float y, out float z)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
        }

        public static void Deconstruct(this Vector3Int vector, out int x, out int y, out int z)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
        }

        public static void Deconstruct(this Bounds bounds, out Vector3 center, out Vector3 size)
        {
            center = bounds.center;
            size = bounds.size;
        }

        public static Vector2 WithX(this Vector2 vector, float x)
        {
            return new Vector2(x, vector.y);
        }

        public static Vector2 WithY(this Vector2 vector, float y)
        {
            return new Vector2(vector.x, y);
        }

        public static Vector2Int WithX(this Vector2Int vector, int x)
        {
            return new Vector2Int(x, vector.y);
        }

        public static Vector2Int WithY(this Vector2Int vector, int y)
        {
            return new Vector2Int(vector.x, y);
        }

        public static Vector3 WithX(this Vector3 vector, float x)
        {
            return new Vector3(x, vector.y, vector.z);
        }

        public static Vector3 WithY(this Vector3 vector, float y)
        {
            return new Vector3(vector.x, y, vector.z);
        }

        public static Vector3 WithZ(this Vector3 vector, float z)
        {
            return new Vector3(vector.x, vector.y, z);
        }

        public static Vector3Int WithX(this Vector3Int vector, int x)
        {
            return new Vector3Int(x, vector.y, vector.z);
        }

        public static Vector3Int WithY(this Vector3Int vector, int y)
        {
            return new Vector3Int(vector.x, y, vector.z);
        }

        public static Vector3Int WithZ(this Vector3Int vector, int z)
        {
            return new Vector3Int(vector.x, vector.y, z);
        }

        public static Vector2 GetXY(this Vector3 vector)
        {
            return new Vector2(vector.x, vector.y);
        }

        public static Vector2 GetXZ(this Vector3 vector)
        {
            return new Vector2(vector.x, vector.z);
        }

        public static Vector2 GetYZ(this Vector3 vector)
        {
            return new Vector2(vector.y, vector.z);
        }

        public static Vector3 ToXYVector3(this Vector2 vector)
        {
            return new Vector3(vector.x, vector.y, 0);
        }

        public static Vector3 ToXZVector3(this Vector2 vector)
        {
            return new Vector3(vector.x, 0, vector.y);
        }

        public static Vector3 ToYZVector3(this Vector2 vector)
        {
            return new Vector3(0, vector.x, vector.y);
        }

        public static string ToConsiseString(this Vector2 vector2, string format = "F4")
        {
            return $"({vector2.x.ToString(format, CultureInfo.InvariantCulture)},{vector2.y.ToString(format, CultureInfo.InvariantCulture)})";
        }

        public static string ToConsiseString(this Vector2Int vector2Int)
        {
            return $"({vector2Int.x.ToString(CultureInfo.InvariantCulture)},{vector2Int.y.ToString(CultureInfo.InvariantCulture)})";
        }

        public static string ToConsiseString(this Vector3 vector3, string format = "F4")
        {
            return $"({vector3.x.ToString(format, CultureInfo.InvariantCulture)},{vector3.y.ToString(format, CultureInfo.InvariantCulture)},{vector3.z.ToString(format, CultureInfo.InvariantCulture)})";
        }

        public static string ToConsiseString(this Vector3Int vector3Int)
        {
            return $"({vector3Int.x.ToString(CultureInfo.InvariantCulture)},{vector3Int.y.ToString(CultureInfo.InvariantCulture)},{vector3Int.z.ToString(CultureInfo.InvariantCulture)})";
        }

        public static void FromConsiseString(this Vector2 vector2, string consiseString)
        {
            if (string.IsNullOrEmpty(consiseString) || consiseString.Length < 5 || consiseString[0] != '(' || consiseString[^1] != ')')
            {
                throw new ArgumentException("Invalid consise string format.");
            }

            var parts = consiseString[1..^1].Split(',');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Consise string must contain exactly two components.");
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                throw new FormatException("Invalid number format in consise string.");
            }

            vector2.x = x;
            vector2.y = y;
        }

        public static void FromConsiseString(this Vector2Int vector2Int, string consiseString)
        {
            if (string.IsNullOrEmpty(consiseString) || consiseString.Length < 5 || consiseString[0] != '(' || consiseString[^1] != ')')
            {
                throw new ArgumentException("Invalid consise string format.");
            }

            var parts = consiseString[1..^1].Split(',');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Consise string must contain exactly two components.");
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                throw new FormatException("Invalid number format in consise string.");
            }

            vector2Int.x = x;
            vector2Int.y = y;
        }

        public static void FromConsiseString(this Vector3 vector3, string consiseString)
        {
            if (string.IsNullOrEmpty(consiseString) || consiseString.Length < 7 || consiseString[0] != '(' || consiseString[^1] != ')')
            {
                throw new ArgumentException("Invalid consise string format.");
            }

            var parts = consiseString[1..^1].Split(',');
            if (parts.Length != 3)
            {
                throw new ArgumentException("Consise string must contain exactly three components.");
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                throw new FormatException("Invalid number format in consise string.");
            }

            vector3.x = x;
            vector3.y = y;
            vector3.z = z;
        }

        public static void FromConsiseString(this Vector3Int vector3Int, string consiseString)
        {
            if (string.IsNullOrEmpty(consiseString) || consiseString.Length < 7 || consiseString[0] != '(' || consiseString[^1] != ')')
            {
                throw new ArgumentException("Invalid consise string format.");
            }

            var parts = consiseString[1..^1].Split(',');
            if (parts.Length != 3)
            {
                throw new ArgumentException("Consise string must contain exactly three components.");
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            {
                throw new FormatException("Invalid number format in consise string.");
            }

            vector3Int.x = x;
            vector3Int.y = y;
            vector3Int.z = z;
        }

        public static Vector2 ToVector2(this string s)
        {
            // (x,y)
            if (string.IsNullOrEmpty(s))
            {
                return Vector2.zero;
            }
            s = s.Trim('(', ')');
            var parts = s.Split(',');
            if (parts.Length != 2)
            {
                throw new FormatException("String must be in the format '(x,y)'");
            }

            return new Vector2(
                parts[0].ToFloat(),
                parts[1].ToFloat()
            );
        }

        public static Vector3 ToVector3(this string s)
        {
            // (x,y,z)
            if (string.IsNullOrEmpty(s))
            {
                return Vector3.zero;
            }
            s = s.Trim('(', ')');
            var parts = s.Split(',');
            if (parts.Length != 3)
            {
                throw new FormatException("String must be in the format '(x,y,z)'");
            }

            return new Vector3(
                parts[0].ToFloat(),
                parts[1].ToFloat(),
                parts[2].ToFloat()
            );
        }

        public static Vector2Int ToVector2Int(this string s)
        {
            // (x,y)
            if (string.IsNullOrEmpty(s))
            {
                return Vector2Int.zero;
            }
            s = s.Trim('(', ')');
            var parts = s.Split(',');
            if (parts.Length != 2)
            {
                throw new FormatException("String must be in the format '(x,y)'");
            }

            return new Vector2Int(
                parts[0].ToInt(),
                parts[1].ToInt()
            );
        }

        public static Vector3Int ToVector3Int(this string s)
        {
            // (x,y,z)
            if (string.IsNullOrEmpty(s))
            {
                return Vector3Int.zero;
            }
            s = s.Trim('(', ')');
            var parts = s.Split(',');
            if (parts.Length != 3)
            {
                throw new FormatException("String must be in the format '(x,y,z)'");
            }

            return new Vector3Int(
                parts[0].ToInt(),
                parts[1].ToInt(),
                parts[2].ToInt()
            );
        }
    }

    public static class VectorSwizzleExtensions
    {
        // Swizzles for Vector2
        public static Vector2 SwizzleXX(this Vector2 v) => new Vector2(v.x, v.x);
        public static Vector2 SwizzleXY(this Vector2 v) => new Vector2(v.x, v.y);
        public static Vector2 SwizzleYX(this Vector2 v) => new Vector2(v.y, v.x);
        public static Vector2 SwizzleYY(this Vector2 v) => new Vector2(v.y, v.y);
        public static Vector3 SwizzleXXX(this Vector2 v) => new Vector3(v.x, v.x, v.x);
        public static Vector3 SwizzleXXY(this Vector2 v) => new Vector3(v.x, v.x, v.y);
        public static Vector3 SwizzleXYX(this Vector2 v) => new Vector3(v.x, v.y, v.x);
        public static Vector3 SwizzleXYY(this Vector2 v) => new Vector3(v.x, v.y, v.y);
        public static Vector3 SwizzleYXX(this Vector2 v) => new Vector3(v.y, v.x, v.x);
        public static Vector3 SwizzleYXY(this Vector2 v) => new Vector3(v.y, v.x, v.y);
        public static Vector3 SwizzleYYX(this Vector2 v) => new Vector3(v.y, v.y, v.x);
        public static Vector3 SwizzleYYY(this Vector2 v) => new Vector3(v.y, v.y, v.y);
        public static Vector4 SwizzleXXXX(this Vector2 v) => new Vector4(v.x, v.x, v.x, v.x);
        public static Vector4 SwizzleXXXY(this Vector2 v) => new Vector4(v.x, v.x, v.x, v.y);
        public static Vector4 SwizzleXXYX(this Vector2 v) => new Vector4(v.x, v.x, v.y, v.x);
        public static Vector4 SwizzleXXYY(this Vector2 v) => new Vector4(v.x, v.x, v.y, v.y);
        public static Vector4 SwizzleXYXX(this Vector2 v) => new Vector4(v.x, v.y, v.x, v.x);
        public static Vector4 SwizzleXYXY(this Vector2 v) => new Vector4(v.x, v.y, v.x, v.y);
        public static Vector4 SwizzleXYYX(this Vector2 v) => new Vector4(v.x, v.y, v.y, v.x);
        public static Vector4 SwizzleXYYY(this Vector2 v) => new Vector4(v.x, v.y, v.y, v.y);
        public static Vector4 SwizzleYXXX(this Vector2 v) => new Vector4(v.y, v.x, v.x, v.x);
        public static Vector4 SwizzleYXXY(this Vector2 v) => new Vector4(v.y, v.x, v.x, v.y);
        public static Vector4 SwizzleYXYX(this Vector2 v) => new Vector4(v.y, v.x, v.y, v.x);
        public static Vector4 SwizzleYXYY(this Vector2 v) => new Vector4(v.y, v.x, v.y, v.y);
        public static Vector4 SwizzleYYXX(this Vector2 v) => new Vector4(v.y, v.y, v.x, v.x);
        public static Vector4 SwizzleYYXY(this Vector2 v) => new Vector4(v.y, v.y, v.x, v.y);
        public static Vector4 SwizzleYYYX(this Vector2 v) => new Vector4(v.y, v.y, v.y, v.x);
        public static Vector4 SwizzleYYYY(this Vector2 v) => new Vector4(v.y, v.y, v.y, v.y);

        // Swizzles for Vector3
        public static Vector2 SwizzleXX(this Vector3 v) => new Vector2(v.x, v.x);
        public static Vector2 SwizzleXY(this Vector3 v) => new Vector2(v.x, v.y);
        public static Vector2 SwizzleXZ(this Vector3 v) => new Vector2(v.x, v.z);
        public static Vector2 SwizzleYX(this Vector3 v) => new Vector2(v.y, v.x);
        public static Vector2 SwizzleYY(this Vector3 v) => new Vector2(v.y, v.y);
        public static Vector2 SwizzleYZ(this Vector3 v) => new Vector2(v.y, v.z);
        public static Vector2 SwizzleZX(this Vector3 v) => new Vector2(v.z, v.x);
        public static Vector2 SwizzleZY(this Vector3 v) => new Vector2(v.z, v.y);
        public static Vector2 SwizzleZZ(this Vector3 v) => new Vector2(v.z, v.z);
        public static Vector3 SwizzleXXX(this Vector3 v) => new Vector3(v.x, v.x, v.x);
        public static Vector3 SwizzleXXY(this Vector3 v) => new Vector3(v.x, v.x, v.y);
        public static Vector3 SwizzleXXZ(this Vector3 v) => new Vector3(v.x, v.x, v.z);
        public static Vector3 SwizzleXYX(this Vector3 v) => new Vector3(v.x, v.y, v.x);
        public static Vector3 SwizzleXYY(this Vector3 v) => new Vector3(v.x, v.y, v.y);
        public static Vector3 SwizzleXYZ(this Vector3 v) => new Vector3(v.x, v.y, v.z);
        public static Vector3 SwizzleXZX(this Vector3 v) => new Vector3(v.x, v.z, v.x);
        public static Vector3 SwizzleXZY(this Vector3 v) => new Vector3(v.x, v.z, v.y);
        public static Vector3 SwizzleXZZ(this Vector3 v) => new Vector3(v.x, v.z, v.z);
        public static Vector3 SwizzleYXX(this Vector3 v) => new Vector3(v.y, v.x, v.x);
        public static Vector3 SwizzleYXY(this Vector3 v) => new Vector3(v.y, v.x, v.y);
        public static Vector3 SwizzleYXZ(this Vector3 v) => new Vector3(v.y, v.x, v.z);
        public static Vector3 SwizzleYYX(this Vector3 v) => new Vector3(v.y, v.y, v.x);
        public static Vector3 SwizzleYYY(this Vector3 v) => new Vector3(v.y, v.y, v.y);
        public static Vector3 SwizzleYYZ(this Vector3 v) => new Vector3(v.y, v.y, v.z);
        public static Vector3 SwizzleYZX(this Vector3 v) => new Vector3(v.y, v.z, v.x);
        public static Vector3 SwizzleYZY(this Vector3 v) => new Vector3(v.y, v.z, v.y);
        public static Vector3 SwizzleYZZ(this Vector3 v) => new Vector3(v.y, v.z, v.z);
        public static Vector3 SwizzleZXX(this Vector3 v) => new Vector3(v.z, v.x, v.x);
        public static Vector3 SwizzleZXY(this Vector3 v) => new Vector3(v.z, v.x, v.y);
        public static Vector3 SwizzleZXZ(this Vector3 v) => new Vector3(v.z, v.x, v.z);
        public static Vector3 SwizzleZYX(this Vector3 v) => new Vector3(v.z, v.y, v.x);
        public static Vector3 SwizzleZYY(this Vector3 v) => new Vector3(v.z, v.y, v.y);
        public static Vector3 SwizzleZYZ(this Vector3 v) => new Vector3(v.z, v.y, v.z);
        public static Vector3 SwizzleZZX(this Vector3 v) => new Vector3(v.z, v.z, v.x);
        public static Vector3 SwizzleZZY(this Vector3 v) => new Vector3(v.z, v.z, v.y);
        public static Vector3 SwizzleZZZ(this Vector3 v) => new Vector3(v.z, v.z, v.z);
        public static Vector4 SwizzleXXXX(this Vector3 v) => new Vector4(v.x, v.x, v.x, v.x);
        public static Vector4 SwizzleXXXY(this Vector3 v) => new Vector4(v.x, v.x, v.x, v.y);
        public static Vector4 SwizzleXXXZ(this Vector3 v) => new Vector4(v.x, v.x, v.x, v.z);
        public static Vector4 SwizzleXXYX(this Vector3 v) => new Vector4(v.x, v.x, v.y, v.x);
        public static Vector4 SwizzleXXYY(this Vector3 v) => new Vector4(v.x, v.x, v.y, v.y);
        public static Vector4 SwizzleXXYZ(this Vector3 v) => new Vector4(v.x, v.x, v.y, v.z);
        public static Vector4 SwizzleXXZX(this Vector3 v) => new Vector4(v.x, v.x, v.z, v.x);
        public static Vector4 SwizzleXXZY(this Vector3 v) => new Vector4(v.x, v.x, v.z, v.y);
        public static Vector4 SwizzleXXZZ(this Vector3 v) => new Vector4(v.x, v.x, v.z, v.z);
        public static Vector4 SwizzleXYXX(this Vector3 v) => new Vector4(v.x, v.y, v.x, v.x);
        public static Vector4 SwizzleXYXY(this Vector3 v) => new Vector4(v.x, v.y, v.x, v.y);
        public static Vector4 SwizzleXYXZ(this Vector3 v) => new Vector4(v.x, v.y, v.x, v.z);
        public static Vector4 SwizzleXYYX(this Vector3 v) => new Vector4(v.x, v.y, v.y, v.x);
        public static Vector4 SwizzleXYYY(this Vector3 v) => new Vector4(v.x, v.y, v.y, v.y);
        public static Vector4 SwizzleXYYZ(this Vector3 v) => new Vector4(v.x, v.y, v.y, v.z);
        public static Vector4 SwizzleXYZX(this Vector3 v) => new Vector4(v.x, v.y, v.z, v.x);
        public static Vector4 SwizzleXYZY(this Vector3 v) => new Vector4(v.x, v.y, v.z, v.y);
        public static Vector4 SwizzleXYZZ(this Vector3 v) => new Vector4(v.x, v.y, v.z, v.z);
        public static Vector4 SwizzleXZXX(this Vector3 v) => new Vector4(v.x, v.z, v.x, v.x);
        public static Vector4 SwizzleXZXY(this Vector3 v) => new Vector4(v.x, v.z, v.x, v.y);
        public static Vector4 SwizzleXZXZ(this Vector3 v) => new Vector4(v.x, v.z, v.x, v.z);
        public static Vector4 SwizzleXZYX(this Vector3 v) => new Vector4(v.x, v.z, v.y, v.x);
        public static Vector4 SwizzleXZYY(this Vector3 v) => new Vector4(v.x, v.z, v.y, v.y);
        public static Vector4 SwizzleXZYZ(this Vector3 v) => new Vector4(v.x, v.z, v.y, v.z);
        public static Vector4 SwizzleXZZX(this Vector3 v) => new Vector4(v.x, v.z, v.z, v.x);
        public static Vector4 SwizzleXZZY(this Vector3 v) => new Vector4(v.x, v.z, v.z, v.y);
        public static Vector4 SwizzleXZZZ(this Vector3 v) => new Vector4(v.x, v.z, v.z, v.z);
        public static Vector4 SwizzleYXXX(this Vector3 v) => new Vector4(v.y, v.x, v.x, v.x);
        public static Vector4 SwizzleYXXY(this Vector3 v) => new Vector4(v.y, v.x, v.x, v.y);
        public static Vector4 SwizzleYXXZ(this Vector3 v) => new Vector4(v.y, v.x, v.x, v.z);
        public static Vector4 SwizzleYXYX(this Vector3 v) => new Vector4(v.y, v.x, v.y, v.x);
        public static Vector4 SwizzleYXYY(this Vector3 v) => new Vector4(v.y, v.x, v.y, v.y);
        public static Vector4 SwizzleYXYZ(this Vector3 v) => new Vector4(v.y, v.x, v.y, v.z);
        public static Vector4 SwizzleYXZX(this Vector3 v) => new Vector4(v.y, v.x, v.z, v.x);
        public static Vector4 SwizzleYXZY(this Vector3 v) => new Vector4(v.y, v.x, v.z, v.y);
        public static Vector4 SwizzleYXZZ(this Vector3 v) => new Vector4(v.y, v.x, v.z, v.z);
        public static Vector4 SwizzleYYXX(this Vector3 v) => new Vector4(v.y, v.y, v.x, v.x);
        public static Vector4 SwizzleYYXY(this Vector3 v) => new Vector4(v.y, v.y, v.x, v.y);
        public static Vector4 SwizzleYYXZ(this Vector3 v) => new Vector4(v.y, v.y, v.x, v.z);
        public static Vector4 SwizzleYYYX(this Vector3 v) => new Vector4(v.y, v.y, v.y, v.x);
        public static Vector4 SwizzleYYYY(this Vector3 v) => new Vector4(v.y, v.y, v.y, v.y);
        public static Vector4 SwizzleYYYZ(this Vector3 v) => new Vector4(v.y, v.y, v.y, v.z);
        public static Vector4 SwizzleYYZX(this Vector3 v) => new Vector4(v.y, v.y, v.z, v.x);
        public static Vector4 SwizzleYYZY(this Vector3 v) => new Vector4(v.y, v.y, v.z, v.y);
        public static Vector4 SwizzleYYZZ(this Vector3 v) => new Vector4(v.y, v.y, v.z, v.z);
        public static Vector4 SwizzleYZXX(this Vector3 v) => new Vector4(v.y, v.z, v.x, v.x);
        public static Vector4 SwizzleYZXY(this Vector3 v) => new Vector4(v.y, v.z, v.x, v.y);
        public static Vector4 SwizzleYZXZ(this Vector3 v) => new Vector4(v.y, v.z, v.x, v.z);
        public static Vector4 SwizzleYZYX(this Vector3 v) => new Vector4(v.y, v.z, v.y, v.x);
        public static Vector4 SwizzleYZYY(this Vector3 v) => new Vector4(v.y, v.z, v.y, v.y);
        public static Vector4 SwizzleYZYZ(this Vector3 v) => new Vector4(v.y, v.z, v.y, v.z);
        public static Vector4 SwizzleYZZX(this Vector3 v) => new Vector4(v.y, v.z, v.z, v.x);
        public static Vector4 SwizzleYZZY(this Vector3 v) => new Vector4(v.y, v.z, v.z, v.y);
        public static Vector4 SwizzleYZZZ(this Vector3 v) => new Vector4(v.y, v.z, v.z, v.z);
        public static Vector4 SwizzleZXXX(this Vector3 v) => new Vector4(v.z, v.x, v.x, v.x);
        public static Vector4 SwizzleZXXY(this Vector3 v) => new Vector4(v.z, v.x, v.x, v.y);
        public static Vector4 SwizzleZXXZ(this Vector3 v) => new Vector4(v.z, v.x, v.x, v.z);
        public static Vector4 SwizzleZXYX(this Vector3 v) => new Vector4(v.z, v.x, v.y, v.x);
        public static Vector4 SwizzleZXYY(this Vector3 v) => new Vector4(v.z, v.x, v.y, v.y);
        public static Vector4 SwizzleZXYZ(this Vector3 v) => new Vector4(v.z, v.x, v.y, v.z);
        public static Vector4 SwizzleZXZX(this Vector3 v) => new Vector4(v.z, v.x, v.z, v.x);
        public static Vector4 SwizzleZXZY(this Vector3 v) => new Vector4(v.z, v.x, v.z, v.y);
        public static Vector4 SwizzleZXZZ(this Vector3 v) => new Vector4(v.z, v.x, v.z, v.z);
        public static Vector4 SwizzleZYXX(this Vector3 v) => new Vector4(v.z, v.y, v.x, v.x);
        public static Vector4 SwizzleZYXY(this Vector3 v) => new Vector4(v.z, v.y, v.x, v.y);
        public static Vector4 SwizzleZYXZ(this Vector3 v) => new Vector4(v.z, v.y, v.x, v.z);
        public static Vector4 SwizzleZYYX(this Vector3 v) => new Vector4(v.z, v.y, v.y, v.x);
        public static Vector4 SwizzleZYYY(this Vector3 v) => new Vector4(v.z, v.y, v.y, v.y);
        public static Vector4 SwizzleZYYZ(this Vector3 v) => new Vector4(v.z, v.y, v.y, v.z);
        public static Vector4 SwizzleZYZX(this Vector3 v) => new Vector4(v.z, v.y, v.z, v.x);
        public static Vector4 SwizzleZYZY(this Vector3 v) => new Vector4(v.z, v.y, v.z, v.y);
        public static Vector4 SwizzleZYZZ(this Vector3 v) => new Vector4(v.z, v.y, v.z, v.z);
        public static Vector4 SwizzleZZXX(this Vector3 v) => new Vector4(v.z, v.z, v.x, v.x);
        public static Vector4 SwizzleZZXY(this Vector3 v) => new Vector4(v.z, v.z, v.x, v.y);
        public static Vector4 SwizzleZZXZ(this Vector3 v) => new Vector4(v.z, v.z, v.x, v.z);
        public static Vector4 SwizzleZZYX(this Vector3 v) => new Vector4(v.z, v.z, v.y, v.x);
        public static Vector4 SwizzleZZYY(this Vector3 v) => new Vector4(v.z, v.z, v.y, v.y);
        public static Vector4 SwizzleZZYZ(this Vector3 v) => new Vector4(v.z, v.z, v.y, v.z);
        public static Vector4 SwizzleZZZX(this Vector3 v) => new Vector4(v.z, v.z, v.z, v.x);
        public static Vector4 SwizzleZZZY(this Vector3 v) => new Vector4(v.z, v.z, v.z, v.y);
        public static Vector4 SwizzleZZZZ(this Vector3 v) => new Vector4(v.z, v.z, v.z, v.z);

        // Swizzles for Vector4
        public static Vector2 SwizzleXX(this Vector4 v) => new Vector2(v.x, v.x);
        public static Vector2 SwizzleXY(this Vector4 v) => new Vector2(v.x, v.y);
        public static Vector2 SwizzleXZ(this Vector4 v) => new Vector2(v.x, v.z);
        public static Vector2 SwizzleXW(this Vector4 v) => new Vector2(v.x, v.w);
        public static Vector2 SwizzleYX(this Vector4 v) => new Vector2(v.y, v.x);
        public static Vector2 SwizzleYY(this Vector4 v) => new Vector2(v.y, v.y);
        public static Vector2 SwizzleYZ(this Vector4 v) => new Vector2(v.y, v.z);
        public static Vector2 SwizzleYW(this Vector4 v) => new Vector2(v.y, v.w);
        public static Vector2 SwizzleZX(this Vector4 v) => new Vector2(v.z, v.x);
        public static Vector2 SwizzleZY(this Vector4 v) => new Vector2(v.z, v.y);
        public static Vector2 SwizzleZZ(this Vector4 v) => new Vector2(v.z, v.z);
        public static Vector2 SwizzleZW(this Vector4 v) => new Vector2(v.z, v.w);
        public static Vector2 SwizzleWX(this Vector4 v) => new Vector2(v.w, v.x);
        public static Vector2 SwizzleWY(this Vector4 v) => new Vector2(v.w, v.y);
        public static Vector2 SwizzleWZ(this Vector4 v) => new Vector2(v.w, v.z);
        public static Vector2 SwizzleWW(this Vector4 v) => new Vector2(v.w, v.w);
        public static Vector3 SwizzleXXX(this Vector4 v) => new Vector3(v.x, v.x, v.x);
        public static Vector3 SwizzleXXY(this Vector4 v) => new Vector3(v.x, v.x, v.y);
        public static Vector3 SwizzleXXZ(this Vector4 v) => new Vector3(v.x, v.x, v.z);
        public static Vector3 SwizzleXXW(this Vector4 v) => new Vector3(v.x, v.x, v.w);
        public static Vector3 SwizzleXYX(this Vector4 v) => new Vector3(v.x, v.y, v.x);
        public static Vector3 SwizzleXYY(this Vector4 v) => new Vector3(v.x, v.y, v.y);
        public static Vector3 SwizzleXYZ(this Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static Vector3 SwizzleXYW(this Vector4 v) => new Vector3(v.x, v.y, v.w);
        public static Vector3 SwizzleXZX(this Vector4 v) => new Vector3(v.x, v.z, v.x);
        public static Vector3 SwizzleXZY(this Vector4 v) => new Vector3(v.x, v.z, v.y);
        public static Vector3 SwizzleXZZ(this Vector4 v) => new Vector3(v.x, v.z, v.z);
        public static Vector3 SwizzleXZW(this Vector4 v) => new Vector3(v.x, v.z, v.w);
        public static Vector3 SwizzleXWX(this Vector4 v) => new Vector3(v.x, v.w, v.x);
        public static Vector3 SwizzleXWY(this Vector4 v) => new Vector3(v.x, v.w, v.y);
        public static Vector3 SwizzleXWZ(this Vector4 v) => new Vector3(v.x, v.w, v.z);
        public static Vector3 SwizzleXWW(this Vector4 v) => new Vector3(v.x, v.w, v.w);
        public static Vector3 SwizzleYXX(this Vector4 v) => new Vector3(v.y, v.x, v.x);
        public static Vector3 SwizzleYXY(this Vector4 v) => new Vector3(v.y, v.x, v.y);
        public static Vector3 SwizzleYXZ(this Vector4 v) => new Vector3(v.y, v.x, v.z);
        public static Vector3 SwizzleYXW(this Vector4 v) => new Vector3(v.y, v.x, v.w);
        public static Vector3 SwizzleYYX(this Vector4 v) => new Vector3(v.y, v.y, v.x);
        public static Vector3 SwizzleYYY(this Vector4 v) => new Vector3(v.y, v.y, v.y);
        public static Vector3 SwizzleYYZ(this Vector4 v) => new Vector3(v.y, v.y, v.z);
        public static Vector3 SwizzleYYW(this Vector4 v) => new Vector3(v.y, v.y, v.w);
        public static Vector3 SwizzleYZX(this Vector4 v) => new Vector3(v.y, v.z, v.x);
        public static Vector3 SwizzleYZY(this Vector4 v) => new Vector3(v.y, v.z, v.y);
        public static Vector3 SwizzleYZZ(this Vector4 v) => new Vector3(v.y, v.z, v.z);
        public static Vector3 SwizzleYZW(this Vector4 v) => new Vector3(v.y, v.z, v.w);
        public static Vector3 SwizzleYWX(this Vector4 v) => new Vector3(v.y, v.w, v.x);
        public static Vector3 SwizzleYWY(this Vector4 v) => new Vector3(v.y, v.w, v.y);
        public static Vector3 SwizzleYWZ(this Vector4 v) => new Vector3(v.y, v.w, v.z);
        public static Vector3 SwizzleYWW(this Vector4 v) => new Vector3(v.y, v.w, v.w);
        public static Vector3 SwizzleZXX(this Vector4 v) => new Vector3(v.z, v.x, v.x);
        public static Vector3 SwizzleZXY(this Vector4 v) => new Vector3(v.z, v.x, v.y);
        public static Vector3 SwizzleZXZ(this Vector4 v) => new Vector3(v.z, v.x, v.z);
        public static Vector3 SwizzleZXW(this Vector4 v) => new Vector3(v.z, v.x, v.w);
        public static Vector3 SwizzleZYX(this Vector4 v) => new Vector3(v.z, v.y, v.x);
        public static Vector3 SwizzleZYY(this Vector4 v) => new Vector3(v.z, v.y, v.y);
        public static Vector3 SwizzleZYZ(this Vector4 v) => new Vector3(v.z, v.y, v.z);
        public static Vector3 SwizzleZYW(this Vector4 v) => new Vector3(v.z, v.y, v.w);
        public static Vector3 SwizzleZZX(this Vector4 v) => new Vector3(v.z, v.z, v.x);
        public static Vector3 SwizzleZZY(this Vector4 v) => new Vector3(v.z, v.z, v.y);
        public static Vector3 SwizzleZZZ(this Vector4 v) => new Vector3(v.z, v.z, v.z);
        public static Vector3 SwizzleZZW(this Vector4 v) => new Vector3(v.z, v.z, v.w);
        public static Vector3 SwizzleZWX(this Vector4 v) => new Vector3(v.z, v.w, v.x);
        public static Vector3 SwizzleZWY(this Vector4 v) => new Vector3(v.z, v.w, v.y);
        public static Vector3 SwizzleZWZ(this Vector4 v) => new Vector3(v.z, v.w, v.z);
        public static Vector3 SwizzleZWW(this Vector4 v) => new Vector3(v.z, v.w, v.w);
        public static Vector3 SwizzleWXX(this Vector4 v) => new Vector3(v.w, v.x, v.x);
        public static Vector3 SwizzleWXY(this Vector4 v) => new Vector3(v.w, v.x, v.y);
        public static Vector3 SwizzleWXZ(this Vector4 v) => new Vector3(v.w, v.x, v.z);
        public static Vector3 SwizzleWXW(this Vector4 v) => new Vector3(v.w, v.x, v.w);
        public static Vector3 SwizzleWYX(this Vector4 v) => new Vector3(v.w, v.y, v.x);
        public static Vector3 SwizzleWYY(this Vector4 v) => new Vector3(v.w, v.y, v.y);
        public static Vector3 SwizzleWYZ(this Vector4 v) => new Vector3(v.w, v.y, v.z);
        public static Vector3 SwizzleWYW(this Vector4 v) => new Vector3(v.w, v.y, v.w);
        public static Vector3 SwizzleWZX(this Vector4 v) => new Vector3(v.w, v.z, v.x);
        public static Vector3 SwizzleWZY(this Vector4 v) => new Vector3(v.w, v.z, v.y);
        public static Vector3 SwizzleWZZ(this Vector4 v) => new Vector3(v.w, v.z, v.z);
        public static Vector3 SwizzleWZW(this Vector4 v) => new Vector3(v.w, v.z, v.w);
        public static Vector3 SwizzleWWX(this Vector4 v) => new Vector3(v.w, v.w, v.x);
        public static Vector3 SwizzleWWY(this Vector4 v) => new Vector3(v.w, v.w, v.y);
        public static Vector3 SwizzleWWZ(this Vector4 v) => new Vector3(v.w, v.w, v.z);
        public static Vector3 SwizzleWWW(this Vector4 v) => new Vector3(v.w, v.w, v.w);
        public static Vector4 SwizzleXXXX(this Vector4 v) => new Vector4(v.x, v.x, v.x, v.x);
        public static Vector4 SwizzleXXXY(this Vector4 v) => new Vector4(v.x, v.x, v.x, v.y);
        public static Vector4 SwizzleXXXZ(this Vector4 v) => new Vector4(v.x, v.x, v.x, v.z);
        public static Vector4 SwizzleXXXW(this Vector4 v) => new Vector4(v.x, v.x, v.x, v.w);
        public static Vector4 SwizzleXXYX(this Vector4 v) => new Vector4(v.x, v.x, v.y, v.x);
        public static Vector4 SwizzleXXYY(this Vector4 v) => new Vector4(v.x, v.x, v.y, v.y);
        public static Vector4 SwizzleXXYZ(this Vector4 v) => new Vector4(v.x, v.x, v.y, v.z);
        public static Vector4 SwizzleXXYW(this Vector4 v) => new Vector4(v.x, v.x, v.y, v.w);
        public static Vector4 SwizzleXXZX(this Vector4 v) => new Vector4(v.x, v.x, v.z, v.x);
        public static Vector4 SwizzleXXZY(this Vector4 v) => new Vector4(v.x, v.x, v.z, v.y);
        public static Vector4 SwizzleXXZZ(this Vector4 v) => new Vector4(v.x, v.x, v.z, v.z);
        public static Vector4 SwizzleXXZW(this Vector4 v) => new Vector4(v.x, v.x, v.z, v.w);
        public static Vector4 SwizzleXXWX(this Vector4 v) => new Vector4(v.x, v.x, v.w, v.x);
        public static Vector4 SwizzleXXWY(this Vector4 v) => new Vector4(v.x, v.x, v.w, v.y);
        public static Vector4 SwizzleXXWZ(this Vector4 v) => new Vector4(v.x, v.x, v.w, v.z);
        public static Vector4 SwizzleXXWW(this Vector4 v) => new Vector4(v.x, v.x, v.w, v.w);
        public static Vector4 SwizzleXYXX(this Vector4 v) => new Vector4(v.x, v.y, v.x, v.x);
        public static Vector4 SwizzleXYXY(this Vector4 v) => new Vector4(v.x, v.y, v.x, v.y);
        public static Vector4 SwizzleXYXZ(this Vector4 v) => new Vector4(v.x, v.y, v.x, v.z);
        public static Vector4 SwizzleXYXW(this Vector4 v) => new Vector4(v.x, v.y, v.x, v.w);
        public static Vector4 SwizzleXYYX(this Vector4 v) => new Vector4(v.x, v.y, v.y, v.x);
        public static Vector4 SwizzleXYYY(this Vector4 v) => new Vector4(v.x, v.y, v.y, v.y);
        public static Vector4 SwizzleXYYZ(this Vector4 v) => new Vector4(v.x, v.y, v.y, v.z);
        public static Vector4 SwizzleXYYW(this Vector4 v) => new Vector4(v.x, v.y, v.y, v.w);
        public static Vector4 SwizzleXYZX(this Vector4 v) => new Vector4(v.x, v.y, v.z, v.x);
        public static Vector4 SwizzleXYZY(this Vector4 v) => new Vector4(v.x, v.y, v.z, v.y);
        public static Vector4 SwizzleXYZZ(this Vector4 v) => new Vector4(v.x, v.y, v.z, v.z);
        public static Vector4 SwizzleXYZW(this Vector4 v) => new Vector4(v.x, v.y, v.z, v.w);
        public static Vector4 SwizzleXYWX(this Vector4 v) => new Vector4(v.x, v.y, v.w, v.x);
        public static Vector4 SwizzleXYWY(this Vector4 v) => new Vector4(v.x, v.y, v.w, v.y);
        public static Vector4 SwizzleXYWZ(this Vector4 v) => new Vector4(v.x, v.y, v.w, v.z);
        public static Vector4 SwizzleXYWW(this Vector4 v) => new Vector4(v.x, v.y, v.w, v.w);
        public static Vector4 SwizzleXZXX(this Vector4 v) => new Vector4(v.x, v.z, v.x, v.x);
        public static Vector4 SwizzleXZXY(this Vector4 v) => new Vector4(v.x, v.z, v.x, v.y);
        public static Vector4 SwizzleXZXZ(this Vector4 v) => new Vector4(v.x, v.z, v.x, v.z);
        public static Vector4 SwizzleXZXW(this Vector4 v) => new Vector4(v.x, v.z, v.x, v.w);
        public static Vector4 SwizzleXZYX(this Vector4 v) => new Vector4(v.x, v.z, v.y, v.x);
        public static Vector4 SwizzleXZYY(this Vector4 v) => new Vector4(v.x, v.z, v.y, v.y);
        public static Vector4 SwizzleXZYZ(this Vector4 v) => new Vector4(v.x, v.z, v.y, v.z);
        public static Vector4 SwizzleXZYW(this Vector4 v) => new Vector4(v.x, v.z, v.y, v.w);
        public static Vector4 SwizzleXZZX(this Vector4 v) => new Vector4(v.x, v.z, v.z, v.x);
        public static Vector4 SwizzleXZZY(this Vector4 v) => new Vector4(v.x, v.z, v.z, v.y);
        public static Vector4 SwizzleXZZZ(this Vector4 v) => new Vector4(v.x, v.z, v.z, v.z);
        public static Vector4 SwizzleXZZW(this Vector4 v) => new Vector4(v.x, v.z, v.z, v.w);
        public static Vector4 SwizzleXZWX(this Vector4 v) => new Vector4(v.x, v.z, v.w, v.x);
        public static Vector4 SwizzleXZWY(this Vector4 v) => new Vector4(v.x, v.z, v.w, v.y);
        public static Vector4 SwizzleXZWZ(this Vector4 v) => new Vector4(v.x, v.z, v.w, v.z);
        public static Vector4 SwizzleXZWW(this Vector4 v) => new Vector4(v.x, v.z, v.w, v.w);
        public static Vector4 SwizzleXWXX(this Vector4 v) => new Vector4(v.x, v.w, v.x, v.x);
        public static Vector4 SwizzleXWXY(this Vector4 v) => new Vector4(v.x, v.w, v.x, v.y);
        public static Vector4 SwizzleXWXZ(this Vector4 v) => new Vector4(v.x, v.w, v.x, v.z);
        public static Vector4 SwizzleXWXW(this Vector4 v) => new Vector4(v.x, v.w, v.x, v.w);
        public static Vector4 SwizzleXWYX(this Vector4 v) => new Vector4(v.x, v.w, v.y, v.x);
        public static Vector4 SwizzleXWYY(this Vector4 v) => new Vector4(v.x, v.w, v.y, v.y);
        public static Vector4 SwizzleXWYZ(this Vector4 v) => new Vector4(v.x, v.w, v.y, v.z);
        public static Vector4 SwizzleXWYW(this Vector4 v) => new Vector4(v.x, v.w, v.y, v.w);
        public static Vector4 SwizzleXWZX(this Vector4 v) => new Vector4(v.x, v.w, v.z, v.x);
        public static Vector4 SwizzleXWZY(this Vector4 v) => new Vector4(v.x, v.w, v.z, v.y);
        public static Vector4 SwizzleXWZZ(this Vector4 v) => new Vector4(v.x, v.w, v.z, v.z);
        public static Vector4 SwizzleXWZW(this Vector4 v) => new Vector4(v.x, v.w, v.z, v.w);
        public static Vector4 SwizzleXWWX(this Vector4 v) => new Vector4(v.x, v.w, v.w, v.x);
        public static Vector4 SwizzleXWWY(this Vector4 v) => new Vector4(v.x, v.w, v.w, v.y);
        public static Vector4 SwizzleXWWZ(this Vector4 v) => new Vector4(v.x, v.w, v.w, v.z);
        public static Vector4 SwizzleXWWW(this Vector4 v) => new Vector4(v.x, v.w, v.w, v.w);
        public static Vector4 SwizzleYXXX(this Vector4 v) => new Vector4(v.y, v.x, v.x, v.x);
        public static Vector4 SwizzleYXXY(this Vector4 v) => new Vector4(v.y, v.x, v.x, v.y);
        public static Vector4 SwizzleYXXZ(this Vector4 v) => new Vector4(v.y, v.x, v.x, v.z);
        public static Vector4 SwizzleYXXW(this Vector4 v) => new Vector4(v.y, v.x, v.x, v.w);
        public static Vector4 SwizzleYXYX(this Vector4 v) => new Vector4(v.y, v.x, v.y, v.x);
        public static Vector4 SwizzleYXYY(this Vector4 v) => new Vector4(v.y, v.x, v.y, v.y);
        public static Vector4 SwizzleYXYZ(this Vector4 v) => new Vector4(v.y, v.x, v.y, v.z);
        public static Vector4 SwizzleYXYW(this Vector4 v) => new Vector4(v.y, v.x, v.y, v.w);
        public static Vector4 SwizzleYXZX(this Vector4 v) => new Vector4(v.y, v.x, v.z, v.x);
        public static Vector4 SwizzleYXZY(this Vector4 v) => new Vector4(v.y, v.x, v.z, v.y);
        public static Vector4 SwizzleYXZZ(this Vector4 v) => new Vector4(v.y, v.x, v.z, v.z);
        public static Vector4 SwizzleYXZW(this Vector4 v) => new Vector4(v.y, v.x, v.z, v.w);
        public static Vector4 SwizzleYXWX(this Vector4 v) => new Vector4(v.y, v.x, v.w, v.x);
        public static Vector4 SwizzleYXWY(this Vector4 v) => new Vector4(v.y, v.x, v.w, v.y);
        public static Vector4 SwizzleYXWZ(this Vector4 v) => new Vector4(v.y, v.x, v.w, v.z);
        public static Vector4 SwizzleYXWW(this Vector4 v) => new Vector4(v.y, v.x, v.w, v.w);
        public static Vector4 SwizzleYYXX(this Vector4 v) => new Vector4(v.y, v.y, v.x, v.x);
        public static Vector4 SwizzleYYXY(this Vector4 v) => new Vector4(v.y, v.y, v.x, v.y);
        public static Vector4 SwizzleYYXZ(this Vector4 v) => new Vector4(v.y, v.y, v.x, v.z);
        public static Vector4 SwizzleYYXW(this Vector4 v) => new Vector4(v.y, v.y, v.x, v.w);
        public static Vector4 SwizzleYYYX(this Vector4 v) => new Vector4(v.y, v.y, v.y, v.x);
        public static Vector4 SwizzleYYYY(this Vector4 v) => new Vector4(v.y, v.y, v.y, v.y);
        public static Vector4 SwizzleYYYZ(this Vector4 v) => new Vector4(v.y, v.y, v.y, v.z);
        public static Vector4 SwizzleYYYW(this Vector4 v) => new Vector4(v.y, v.y, v.y, v.w);
        public static Vector4 SwizzleYYZX(this Vector4 v) => new Vector4(v.y, v.y, v.z, v.x);
        public static Vector4 SwizzleYYZY(this Vector4 v) => new Vector4(v.y, v.y, v.z, v.y);
        public static Vector4 SwizzleYYZZ(this Vector4 v) => new Vector4(v.y, v.y, v.z, v.z);
        public static Vector4 SwizzleYYZW(this Vector4 v) => new Vector4(v.y, v.y, v.z, v.w);
        public static Vector4 SwizzleYYWX(this Vector4 v) => new Vector4(v.y, v.y, v.w, v.x);
        public static Vector4 SwizzleYYWY(this Vector4 v) => new Vector4(v.y, v.y, v.w, v.y);
        public static Vector4 SwizzleYYWZ(this Vector4 v) => new Vector4(v.y, v.y, v.w, v.z);
        public static Vector4 SwizzleYYWW(this Vector4 v) => new Vector4(v.y, v.y, v.w, v.w);
        public static Vector4 SwizzleYZXX(this Vector4 v) => new Vector4(v.y, v.z, v.x, v.x);
        public static Vector4 SwizzleYZXY(this Vector4 v) => new Vector4(v.y, v.z, v.x, v.y);
        public static Vector4 SwizzleYZXZ(this Vector4 v) => new Vector4(v.y, v.z, v.x, v.z);
        public static Vector4 SwizzleYZXW(this Vector4 v) => new Vector4(v.y, v.z, v.x, v.w);
        public static Vector4 SwizzleYZYX(this Vector4 v) => new Vector4(v.y, v.z, v.y, v.x);
        public static Vector4 SwizzleYZYY(this Vector4 v) => new Vector4(v.y, v.z, v.y, v.y);
        public static Vector4 SwizzleYZYZ(this Vector4 v) => new Vector4(v.y, v.z, v.y, v.z);
        public static Vector4 SwizzleYZYW(this Vector4 v) => new Vector4(v.y, v.z, v.y, v.w);
        public static Vector4 SwizzleYZZX(this Vector4 v) => new Vector4(v.y, v.z, v.z, v.x);
        public static Vector4 SwizzleYZZY(this Vector4 v) => new Vector4(v.y, v.z, v.z, v.y);
        public static Vector4 SwizzleYZZZ(this Vector4 v) => new Vector4(v.y, v.z, v.z, v.z);
        public static Vector4 SwizzleYZZW(this Vector4 v) => new Vector4(v.y, v.z, v.z, v.w);
        public static Vector4 SwizzleYZWX(this Vector4 v) => new Vector4(v.y, v.z, v.w, v.x);
        public static Vector4 SwizzleYZWY(this Vector4 v) => new Vector4(v.y, v.z, v.w, v.y);
        public static Vector4 SwizzleYZWZ(this Vector4 v) => new Vector4(v.y, v.z, v.w, v.z);
        public static Vector4 SwizzleYZWW(this Vector4 v) => new Vector4(v.y, v.z, v.w, v.w);
        public static Vector4 SwizzleYWXX(this Vector4 v) => new Vector4(v.y, v.w, v.x, v.x);
        public static Vector4 SwizzleYWXY(this Vector4 v) => new Vector4(v.y, v.w, v.x, v.y);
        public static Vector4 SwizzleYWXZ(this Vector4 v) => new Vector4(v.y, v.w, v.x, v.z);
        public static Vector4 SwizzleYWXW(this Vector4 v) => new Vector4(v.y, v.w, v.x, v.w);
        public static Vector4 SwizzleYWYX(this Vector4 v) => new Vector4(v.y, v.w, v.y, v.x);
        public static Vector4 SwizzleYWYY(this Vector4 v) => new Vector4(v.y, v.w, v.y, v.y);
        public static Vector4 SwizzleYWYZ(this Vector4 v) => new Vector4(v.y, v.w, v.y, v.z);
        public static Vector4 SwizzleYWYW(this Vector4 v) => new Vector4(v.y, v.w, v.y, v.w);
        public static Vector4 SwizzleYWZX(this Vector4 v) => new Vector4(v.y, v.w, v.z, v.x);
        public static Vector4 SwizzleYWZY(this Vector4 v) => new Vector4(v.y, v.w, v.z, v.y);
        public static Vector4 SwizzleYWZZ(this Vector4 v) => new Vector4(v.y, v.w, v.z, v.z);
        public static Vector4 SwizzleYWZW(this Vector4 v) => new Vector4(v.y, v.w, v.z, v.w);
        public static Vector4 SwizzleYWWX(this Vector4 v) => new Vector4(v.y, v.w, v.w, v.x);
        public static Vector4 SwizzleYWWY(this Vector4 v) => new Vector4(v.y, v.w, v.w, v.y);
        public static Vector4 SwizzleYWWZ(this Vector4 v) => new Vector4(v.y, v.w, v.w, v.z);
        public static Vector4 SwizzleYWWW(this Vector4 v) => new Vector4(v.y, v.w, v.w, v.w);
        public static Vector4 SwizzleZXXX(this Vector4 v) => new Vector4(v.z, v.x, v.x, v.x);
        public static Vector4 SwizzleZXXY(this Vector4 v) => new Vector4(v.z, v.x, v.x, v.y);
        public static Vector4 SwizzleZXXZ(this Vector4 v) => new Vector4(v.z, v.x, v.x, v.z);
        public static Vector4 SwizzleZXXW(this Vector4 v) => new Vector4(v.z, v.x, v.x, v.w);
        public static Vector4 SwizzleZXYX(this Vector4 v) => new Vector4(v.z, v.x, v.y, v.x);
        public static Vector4 SwizzleZXYY(this Vector4 v) => new Vector4(v.z, v.x, v.y, v.y);
        public static Vector4 SwizzleZXYZ(this Vector4 v) => new Vector4(v.z, v.x, v.y, v.z);
        public static Vector4 SwizzleZXYW(this Vector4 v) => new Vector4(v.z, v.x, v.y, v.w);
        public static Vector4 SwizzleZXZX(this Vector4 v) => new Vector4(v.z, v.x, v.z, v.x);
        public static Vector4 SwizzleZXZY(this Vector4 v) => new Vector4(v.z, v.x, v.z, v.y);
        public static Vector4 SwizzleZXZZ(this Vector4 v) => new Vector4(v.z, v.x, v.z, v.z);
        public static Vector4 SwizzleZXZW(this Vector4 v) => new Vector4(v.z, v.x, v.z, v.w);
        public static Vector4 SwizzleZXWX(this Vector4 v) => new Vector4(v.z, v.x, v.w, v.x);
        public static Vector4 SwizzleZXWY(this Vector4 v) => new Vector4(v.z, v.x, v.w, v.y);
        public static Vector4 SwizzleZXWZ(this Vector4 v) => new Vector4(v.z, v.x, v.w, v.z);
        public static Vector4 SwizzleZXWW(this Vector4 v) => new Vector4(v.z, v.x, v.w, v.w);
        public static Vector4 SwizzleZYXX(this Vector4 v) => new Vector4(v.z, v.y, v.x, v.x);
        public static Vector4 SwizzleZYXY(this Vector4 v) => new Vector4(v.z, v.y, v.x, v.y);
        public static Vector4 SwizzleZYXZ(this Vector4 v) => new Vector4(v.z, v.y, v.x, v.z);
        public static Vector4 SwizzleZYXW(this Vector4 v) => new Vector4(v.z, v.y, v.x, v.w);
        public static Vector4 SwizzleZYYX(this Vector4 v) => new Vector4(v.z, v.y, v.y, v.x);
        public static Vector4 SwizzleZYYY(this Vector4 v) => new Vector4(v.z, v.y, v.y, v.y);
        public static Vector4 SwizzleZYYZ(this Vector4 v) => new Vector4(v.z, v.y, v.y, v.z);
        public static Vector4 SwizzleZYYW(this Vector4 v) => new Vector4(v.z, v.y, v.y, v.w);
        public static Vector4 SwizzleZYZX(this Vector4 v) => new Vector4(v.z, v.y, v.z, v.x);
        public static Vector4 SwizzleZYZY(this Vector4 v) => new Vector4(v.z, v.y, v.z, v.y);
        public static Vector4 SwizzleZYZZ(this Vector4 v) => new Vector4(v.z, v.y, v.z, v.z);
        public static Vector4 SwizzleZYZW(this Vector4 v) => new Vector4(v.z, v.y, v.z, v.w);
        public static Vector4 SwizzleZYWX(this Vector4 v) => new Vector4(v.z, v.y, v.w, v.x);
        public static Vector4 SwizzleZYWY(this Vector4 v) => new Vector4(v.z, v.y, v.w, v.y);
        public static Vector4 SwizzleZYWZ(this Vector4 v) => new Vector4(v.z, v.y, v.w, v.z);
        public static Vector4 SwizzleZYWW(this Vector4 v) => new Vector4(v.z, v.y, v.w, v.w);
        public static Vector4 SwizzleZZXX(this Vector4 v) => new Vector4(v.z, v.z, v.x, v.x);
        public static Vector4 SwizzleZZXY(this Vector4 v) => new Vector4(v.z, v.z, v.x, v.y);
        public static Vector4 SwizzleZZXZ(this Vector4 v) => new Vector4(v.z, v.z, v.x, v.z);
        public static Vector4 SwizzleZZXW(this Vector4 v) => new Vector4(v.z, v.z, v.x, v.w);
        public static Vector4 SwizzleZZYX(this Vector4 v) => new Vector4(v.z, v.z, v.y, v.x);
        public static Vector4 SwizzleZZYY(this Vector4 v) => new Vector4(v.z, v.z, v.y, v.y);
        public static Vector4 SwizzleZZYZ(this Vector4 v) => new Vector4(v.z, v.z, v.y, v.z);
        public static Vector4 SwizzleZZYW(this Vector4 v) => new Vector4(v.z, v.z, v.y, v.w);
        public static Vector4 SwizzleZZZX(this Vector4 v) => new Vector4(v.z, v.z, v.z, v.x);
        public static Vector4 SwizzleZZZY(this Vector4 v) => new Vector4(v.z, v.z, v.z, v.y);
        public static Vector4 SwizzleZZZZ(this Vector4 v) => new Vector4(v.z, v.z, v.z, v.z);
        public static Vector4 SwizzleZZZW(this Vector4 v) => new Vector4(v.z, v.z, v.z, v.w);
        public static Vector4 SwizzleZZWX(this Vector4 v) => new Vector4(v.z, v.z, v.w, v.x);
        public static Vector4 SwizzleZZWY(this Vector4 v) => new Vector4(v.z, v.z, v.w, v.y);
        public static Vector4 SwizzleZZWZ(this Vector4 v) => new Vector4(v.z, v.z, v.w, v.z);
        public static Vector4 SwizzleZZWW(this Vector4 v) => new Vector4(v.z, v.z, v.w, v.w);
        public static Vector4 SwizzleZWXX(this Vector4 v) => new Vector4(v.z, v.w, v.x, v.x);
        public static Vector4 SwizzleZWXY(this Vector4 v) => new Vector4(v.z, v.w, v.x, v.y);
        public static Vector4 SwizzleZWXZ(this Vector4 v) => new Vector4(v.z, v.w, v.x, v.z);
        public static Vector4 SwizzleZWXW(this Vector4 v) => new Vector4(v.z, v.w, v.x, v.w);
        public static Vector4 SwizzleZWYX(this Vector4 v) => new Vector4(v.z, v.w, v.y, v.x);
        public static Vector4 SwizzleZWYY(this Vector4 v) => new Vector4(v.z, v.w, v.y, v.y);
        public static Vector4 SwizzleZWYZ(this Vector4 v) => new Vector4(v.z, v.w, v.y, v.z);
        public static Vector4 SwizzleZWYW(this Vector4 v) => new Vector4(v.z, v.w, v.y, v.w);
        public static Vector4 SwizzleZWZX(this Vector4 v) => new Vector4(v.z, v.w, v.z, v.x);
        public static Vector4 SwizzleZWZY(this Vector4 v) => new Vector4(v.z, v.w, v.z, v.y);
        public static Vector4 SwizzleZWZZ(this Vector4 v) => new Vector4(v.z, v.w, v.z, v.z);
        public static Vector4 SwizzleZWZW(this Vector4 v) => new Vector4(v.z, v.w, v.z, v.w);
        public static Vector4 SwizzleZWWX(this Vector4 v) => new Vector4(v.z, v.w, v.w, v.x);
        public static Vector4 SwizzleZWWY(this Vector4 v) => new Vector4(v.z, v.w, v.w, v.y);
        public static Vector4 SwizzleZWWZ(this Vector4 v) => new Vector4(v.z, v.w, v.w, v.z);
        public static Vector4 SwizzleZWWW(this Vector4 v) => new Vector4(v.z, v.w, v.w, v.w);
        public static Vector4 SwizzleWXXX(this Vector4 v) => new Vector4(v.w, v.x, v.x, v.x);
        public static Vector4 SwizzleWXXY(this Vector4 v) => new Vector4(v.w, v.x, v.x, v.y);
        public static Vector4 SwizzleWXXZ(this Vector4 v) => new Vector4(v.w, v.x, v.x, v.z);
        public static Vector4 SwizzleWXXW(this Vector4 v) => new Vector4(v.w, v.x, v.x, v.w);
        public static Vector4 SwizzleWXYX(this Vector4 v) => new Vector4(v.w, v.x, v.y, v.x);
        public static Vector4 SwizzleWXYY(this Vector4 v) => new Vector4(v.w, v.x, v.y, v.y);
        public static Vector4 SwizzleWXYZ(this Vector4 v) => new Vector4(v.w, v.x, v.y, v.z);
        public static Vector4 SwizzleWXYW(this Vector4 v) => new Vector4(v.w, v.x, v.y, v.w);
        public static Vector4 SwizzleWXZX(this Vector4 v) => new Vector4(v.w, v.x, v.z, v.x);
        public static Vector4 SwizzleWXZY(this Vector4 v) => new Vector4(v.w, v.x, v.z, v.y);
        public static Vector4 SwizzleWXZZ(this Vector4 v) => new Vector4(v.w, v.x, v.z, v.z);
        public static Vector4 SwizzleWXZW(this Vector4 v) => new Vector4(v.w, v.x, v.z, v.w);
        public static Vector4 SwizzleWXWX(this Vector4 v) => new Vector4(v.w, v.x, v.w, v.x);
        public static Vector4 SwizzleWXWY(this Vector4 v) => new Vector4(v.w, v.x, v.w, v.y);
        public static Vector4 SwizzleWXWZ(this Vector4 v) => new Vector4(v.w, v.x, v.w, v.z);
        public static Vector4 SwizzleWXWW(this Vector4 v) => new Vector4(v.w, v.x, v.w, v.w);
        public static Vector4 SwizzleWYXX(this Vector4 v) => new Vector4(v.w, v.y, v.x, v.x);
        public static Vector4 SwizzleWYXY(this Vector4 v) => new Vector4(v.w, v.y, v.x, v.y);
        public static Vector4 SwizzleWYXZ(this Vector4 v) => new Vector4(v.w, v.y, v.x, v.z);
        public static Vector4 SwizzleWYXW(this Vector4 v) => new Vector4(v.w, v.y, v.x, v.w);
        public static Vector4 SwizzleWYYX(this Vector4 v) => new Vector4(v.w, v.y, v.y, v.x);
        public static Vector4 SwizzleWYYY(this Vector4 v) => new Vector4(v.w, v.y, v.y, v.y);
        public static Vector4 SwizzleWYYZ(this Vector4 v) => new Vector4(v.w, v.y, v.y, v.z);
        public static Vector4 SwizzleWYYW(this Vector4 v) => new Vector4(v.w, v.y, v.y, v.w);
        public static Vector4 SwizzleWYZX(this Vector4 v) => new Vector4(v.w, v.y, v.z, v.x);
        public static Vector4 SwizzleWYZY(this Vector4 v) => new Vector4(v.w, v.y, v.z, v.y);
        public static Vector4 SwizzleWYZZ(this Vector4 v) => new Vector4(v.w, v.y, v.z, v.z);
        public static Vector4 SwizzleWYZW(this Vector4 v) => new Vector4(v.w, v.y, v.z, v.w);
        public static Vector4 SwizzleWYWX(this Vector4 v) => new Vector4(v.w, v.y, v.w, v.x);
        public static Vector4 SwizzleWYWY(this Vector4 v) => new Vector4(v.w, v.y, v.w, v.y);
        public static Vector4 SwizzleWYWZ(this Vector4 v) => new Vector4(v.w, v.y, v.w, v.z);
        public static Vector4 SwizzleWYWW(this Vector4 v) => new Vector4(v.w, v.y, v.w, v.w);
        public static Vector4 SwizzleWZXX(this Vector4 v) => new Vector4(v.w, v.z, v.x, v.x);
        public static Vector4 SwizzleWZXY(this Vector4 v) => new Vector4(v.w, v.z, v.x, v.y);
        public static Vector4 SwizzleWZXZ(this Vector4 v) => new Vector4(v.w, v.z, v.x, v.z);
        public static Vector4 SwizzleWZXW(this Vector4 v) => new Vector4(v.w, v.z, v.x, v.w);
        public static Vector4 SwizzleWZYX(this Vector4 v) => new Vector4(v.w, v.z, v.y, v.x);
        public static Vector4 SwizzleWZYY(this Vector4 v) => new Vector4(v.w, v.z, v.y, v.y);
        public static Vector4 SwizzleWZYZ(this Vector4 v) => new Vector4(v.w, v.z, v.y, v.z);
        public static Vector4 SwizzleWZYW(this Vector4 v) => new Vector4(v.w, v.z, v.y, v.w);
        public static Vector4 SwizzleWZZX(this Vector4 v) => new Vector4(v.w, v.z, v.z, v.x);
        public static Vector4 SwizzleWZZY(this Vector4 v) => new Vector4(v.w, v.z, v.z, v.y);
        public static Vector4 SwizzleWZZZ(this Vector4 v) => new Vector4(v.w, v.z, v.z, v.z);
        public static Vector4 SwizzleWZZW(this Vector4 v) => new Vector4(v.w, v.z, v.z, v.w);
        public static Vector4 SwizzleWZWX(this Vector4 v) => new Vector4(v.w, v.z, v.w, v.x);
        public static Vector4 SwizzleWZWY(this Vector4 v) => new Vector4(v.w, v.z, v.w, v.y);
        public static Vector4 SwizzleWZWZ(this Vector4 v) => new Vector4(v.w, v.z, v.w, v.z);
        public static Vector4 SwizzleWZWW(this Vector4 v) => new Vector4(v.w, v.z, v.w, v.w);
        public static Vector4 SwizzleWWXX(this Vector4 v) => new Vector4(v.w, v.w, v.x, v.x);
        public static Vector4 SwizzleWWXY(this Vector4 v) => new Vector4(v.w, v.w, v.x, v.y);
        public static Vector4 SwizzleWWXZ(this Vector4 v) => new Vector4(v.w, v.w, v.x, v.z);
        public static Vector4 SwizzleWWXW(this Vector4 v) => new Vector4(v.w, v.w, v.x, v.w);
        public static Vector4 SwizzleWWYX(this Vector4 v) => new Vector4(v.w, v.w, v.y, v.x);
        public static Vector4 SwizzleWWYY(this Vector4 v) => new Vector4(v.w, v.w, v.y, v.y);
        public static Vector4 SwizzleWWYZ(this Vector4 v) => new Vector4(v.w, v.w, v.y, v.z);
        public static Vector4 SwizzleWWYW(this Vector4 v) => new Vector4(v.w, v.w, v.y, v.w);
        public static Vector4 SwizzleWWZX(this Vector4 v) => new Vector4(v.w, v.w, v.z, v.x);
        public static Vector4 SwizzleWWZY(this Vector4 v) => new Vector4(v.w, v.w, v.z, v.y);
        public static Vector4 SwizzleWWZZ(this Vector4 v) => new Vector4(v.w, v.w, v.z, v.z);
        public static Vector4 SwizzleWWZW(this Vector4 v) => new Vector4(v.w, v.w, v.z, v.w);
        public static Vector4 SwizzleWWWX(this Vector4 v) => new Vector4(v.w, v.w, v.w, v.x);
        public static Vector4 SwizzleWWWY(this Vector4 v) => new Vector4(v.w, v.w, v.w, v.y);
        public static Vector4 SwizzleWWWZ(this Vector4 v) => new Vector4(v.w, v.w, v.w, v.z);
        public static Vector4 SwizzleWWWW(this Vector4 v) => new Vector4(v.w, v.w, v.w, v.w);

    }
}