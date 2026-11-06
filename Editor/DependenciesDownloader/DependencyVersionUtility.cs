using System;
using System.Text.RegularExpressions;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class DependencyVersionUtility
    {
        private static readonly Regex VersionPattern = new Regex(
            @"(?<!\d)(\d+\.\d+(?:\.\d+){0,2})(?:-([0-9A-Za-z.-]+))?",
            RegexOptions.Compiled
        );

        public static string Extract(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var fragment = value.LastIndexOf('#');
            if (fragment >= 0) value = value.Substring(fragment + 1);
            value = value.Replace('_', '.');
            if (value.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 4);
            var match = VersionPattern.Match(value);
            return match.Success ? match.Value : string.Empty;
        }

        public static bool TryCompare(string left, string right, out int comparison)
        {
            comparison = 0;
            var leftVersion = Extract(left);
            var rightVersion = Extract(right);
            if (string.IsNullOrEmpty(leftVersion) || string.IsNullOrEmpty(rightVersion))
                return false;

            var leftParts = leftVersion.Split(new[] { '-' }, 2);
            var rightParts = rightVersion.Split(new[] { '-' }, 2);
            var leftNumbers = leftParts[0].Split('.');
            var rightNumbers = rightParts[0].Split('.');
            for (var i = 0; i < Math.Max(leftNumbers.Length, rightNumbers.Length); i++)
            {
                if (!int.TryParse(i < leftNumbers.Length ? leftNumbers[i] : "0", out var leftNumber) ||
                    !int.TryParse(i < rightNumbers.Length ? rightNumbers[i] : "0", out var rightNumber)) return false;
                comparison = leftNumber.CompareTo(rightNumber);
                if (comparison != 0) return true;
            }

            if (leftParts.Length != rightParts.Length)
            {
                comparison = leftParts.Length == 1 ? 1 : -1;
                return true;
            }
            if (leftParts.Length == 1) return true;

            var leftIdentifiers = leftParts[1].Split('.');
            var rightIdentifiers = rightParts[1].Split('.');
            for (var i = 0; i < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); i++)
            {
                var leftNumeric = int.TryParse(leftIdentifiers[i], out var leftNumber);
                var rightNumeric = int.TryParse(rightIdentifiers[i], out var rightNumber);
                comparison = leftNumeric && rightNumeric
                    ? leftNumber.CompareTo(rightNumber)
                    : leftNumeric != rightNumeric
                        ? (leftNumeric ? -1 : 1)
                        : string.CompareOrdinal(leftIdentifiers[i], rightIdentifiers[i]);
                if (comparison != 0) return true;
            }
            comparison = leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
            return true;
        }

        public static bool Satisfies(string packageName, string installed, string required)
        {
            if (!TryCompare(installed, required, out var comparison)) return false;
            // Firebase SDK modules must stay aligned. EDM is a shared minimum-version
            // requirement and can remain under its existing registry ownership.
            return packageName == "com.google.external-dependency-manager"
                ? comparison >= 0
                : comparison == 0;
        }

        public static string ActionLabel(string installed, string target)
        {
            if (string.IsNullOrEmpty(installed)) return $"Queue install {target}";
            if (!TryCompare(target, installed, out var comparison)) return $"Queue change to {target}";
            return comparison < 0 ? $"Queue downgrade to {target}" : $"Queue update to {target}";
        }
    }
}
