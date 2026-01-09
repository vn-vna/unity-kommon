using System;
using System.Globalization;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for working with Unity strings including case conversions.
    /// </summary>
    /// <example>
    /// <code>
    /// string name = "myVariable";
    /// string pascal = name.ToPascalCase(); // "MyVariable"
    /// string camel = "MyVariable".ToCamelCase(); // "myVariable"
    /// </code>
    /// </example>
    public static class StringExtensions
    {
        /// <summary>
        /// Converts a string to PascalCase (first letter uppercase).
        /// </summary>
        /// <param name="str">The string to convert.</param>
        /// <returns>The string in PascalCase format.</returns>
        public static string ToPascalCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            if (str.Length == 1)
                return str.ToUpper();

            return char.ToUpper(str[0]) + str.Substring(1);
        }

        /// <summary>
        /// Converts a string to camelCase (first letter lowercase).
        /// </summary>
        /// <param name="str">The string to convert.</param>
        /// <returns>The string in camelCase format.</returns>
        public static string ToCamelCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            if (str.Length == 1)
                return str.ToLower();

            return char.ToLower(str[0]) + str.Substring(1);
        }
    }
}