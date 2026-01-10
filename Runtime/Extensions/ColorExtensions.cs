using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for Unity Color type including tuple conversion and component modification.
    /// </summary>
    /// <example>
    /// <code>
    /// Color color = Color.red;
    /// var (r, g, b) = color; // Deconstruct to RGB
    /// 
    /// Color newColor = color.WithAlpha(0.5f); // Change alpha only
    /// Color blue = color.WithRgb(0, 0, 1); // Change RGB components
    /// </code>
    /// </example>
    public static class ColorExtensions
    {
        /// <summary>
        /// Converts a Color to an RGB tuple.
        /// </summary>
        /// <param name="color">The color to convert.</param>
        /// <returns>A tuple containing (r, g, b) values.</returns>
        public static (float, float, float) ToTupleRgb(this Color color)
        {
            return (color.r, color.g, color.b);
        }

        /// <summary>
        /// Converts a Color to an RGBA tuple.
        /// </summary>
        /// <param name="color">The color to convert.</param>
        /// <returns>A tuple containing (r, g, b, a) values.</returns>
        public static (float, float, float, float) ToTupleRgba(this Color color)
        {
            return (color.r, color.g, color.b, color.a);
        }

        /// <summary>
        /// Deconstructs a Color into RGB components.
        /// </summary>
        /// <param name="color">The color to deconstruct.</param>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        public static void Deconstruct(this Color color, out float r, out float g, out float b)
        {
            r = color.r;
            g = color.g;
            b = color.b;
        }

        /// <summary>
        /// Deconstructs a Color into RGBA components.
        /// </summary>
        /// <param name="color">The color to deconstruct.</param>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <param name="a">The alpha component.</param>
        public static void Deconstruct(this Color color, out float r, out float g, out float b, out float a)
        {
            r = color.r;
            g = color.g;
            b = color.b;
            a = color.a;
        }

        /// <summary>
        /// Creates a new color with a different red component.
        /// </summary>
        /// <param name="color">The original color.</param>
        /// <param name="r">The new red value.</param>
        /// <returns>A new color with the modified red component.</returns>
        public static Color WithRed(this Color color, float r)
        {
            return new Color(r, color.g, color.b, color.a);
        }

        /// <summary>
        /// Creates a new color with a different green component.
        /// </summary>
        /// <param name="color">The original color.</param>
        /// <param name="g">The new green value.</param>
        /// <returns>A new color with the modified green component.</returns>
        public static Color WithGreen(this Color color, float g)
        {
            return new Color(color.r, g, color.b, color.a);
        }

        /// <summary>
        /// Creates a new color with a different blue component.
        /// </summary>
        /// <param name="color">The original color.</param>
        /// <param name="b">The new blue value.</param>
        /// <returns>A new color with the modified blue component.</returns>
        public static Color WithBlue(this Color color, float b)
        {
            return new Color(color.r, color.g, b, color.a);
        }

        /// <summary>
        /// Creates a new color with a different alpha component.
        /// </summary>
        /// <param name="color">The original color.</param>
        /// <param name="a">The new alpha value.</param>
        /// <returns>A new color with the modified alpha component.</returns>
        public static Color WithAlpha(this Color color, float a)
        {
            return new Color(color.r, color.g, color.b, a);
        }

        /// <summary>
        /// Creates a new color with different RGB components while preserving alpha.
        /// </summary>
        /// <param name="color">The original color.</param>
        /// <param name="r">The new red value.</param>
        /// <param name="g">The new green value.</param>
        /// <param name="b">The new blue value.</param>
        /// <returns>A new color with the modified RGB components.</returns>
        public static Color WithRgb(this Color color, float r, float g, float b)
        {
            return new Color(r, g, b, color.a);
        }
    }
}