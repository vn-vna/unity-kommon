using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class ColorExtensions
    {
        public static (float, float, float) ToTupleRgb(this Color color)
        {
            return (color.r, color.g, color.b);
        }

        public static (float, float, float, float) ToTupleRgba(this Color color)
        {
            return (color.r, color.g, color.b, color.a);
        }

        public static void Deconstruct(this Color color, out float r, out float g, out float b)
        {
            r = color.r;
            g = color.g;
            b = color.b;
        }

        public static void Deconstruct(this Color color, out float r, out float g, out float b, out float a)
        {
            r = color.r;
            g = color.g;
            b = color.b;
            a = color.a;
        }

        public static Color WithRed(this Color color, float r)
        {
            return new Color(r, color.g, color.b, color.a);
        }

        public static Color WithGreen(this Color color, float g)
        {
            return new Color(color.r, g, color.b, color.a);
        }

        public static Color WithBlue(this Color color, float b)
        {
            return new Color(color.r, color.g, b, color.a);
        }

        public static Color WithAlpha(this Color color, float a)
        {
            return new Color(color.r, color.g, color.b, a);
        }

        public static Color WithRgb(this Color color, float r, float g, float b)
        {
            return new Color(r, g, b, color.a);
        }
    }
}