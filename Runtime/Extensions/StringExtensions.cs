using System;
using System.Globalization;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Extensions
{
    public static class StringExtensions
    {
        public static string ToPascalCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            if (str.Length == 1)
                return str.ToUpper();

            return char.ToUpper(str[0]) + str.Substring(1);
        }

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