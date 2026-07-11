// ═══════════════════════════════════════════════════════════
// ── EditorGuiStrings ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System.Text.RegularExpressions;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Shared string utilities for editor GUI display.
    /// </summary>
    public static class EditorGuiStrings
    {
        #region Private Fields

        private static readonly Regex PascalCaseRegex = new Regex(
            @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled
        );

        #endregion

        #region Public Methods

        /// <summary>
        /// Truncates a string to <paramref name="maxLength"/> characters, appending an ellipsis if shortened.
        /// </summary>
        public static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text.Length > maxLength
                ? text.Substring(0, maxLength - 1) + "\u2026"
                : text;
        }

        /// <summary>
        /// Converts PascalCase or camelCase to space-separated words.
        /// Example: "MyVariableName" → "My Variable Name".
        /// </summary>
        public static string PascalCaseToSpaced(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase))
                return pascalCase;

            return PascalCaseRegex.Replace(pascalCase, " ");
        }

        #endregion
    }
}
