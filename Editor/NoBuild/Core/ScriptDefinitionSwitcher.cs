// ═══════════════════════════════════════════════════════════
// ── ScriptDefinitionSwitcher ──────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Applies a <see cref="ScriptDefinitionSet"/> to the project's scripting define symbols.
    ///
    /// Uses a merge strategy: adds enabled symbols and removes disabled symbols
    /// that were previously set, while preserving all externally-managed defines
    /// (those not present in any slot of the current set).
    /// </summary>
    internal static class ScriptDefinitionSwitcher
    {
        // ══════════════════════════════════════════════════
        // ── Constants
        // ══════════════════════════════════════════════════

        private static readonly Regex ValidIdentifierRegex =
            new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private const string InvalidCharsPattern = @"[^A-Za-z0-9_]";

        // ══════════════════════════════════════════════════
        // ── Events & Delegates
        // ══════════════════════════════════════════════════

        /// <summary>Invoked after script defines are successfully applied.</summary>
        public static event Action<ScriptDefinitionSet> ScriptDefinitionsApplied;

        /// <summary>Invoked when validation or application fails.</summary>
        public static event Action<string> ScriptDefinitionsFailed;

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Applies a script definition set to the specified build target group.
        /// Merges with existing defines: adds enabled symbols, removes disabled
        /// symbols that belong to this set.
        /// </summary>
        /// <param name="set">The definition set to apply.</param>
        /// <param name="targetGroup">
        /// The build target group to modify. Defaults to Android.
        /// Use <c>EditorUserBuildSettings.selectedBuildTargetGroup</c> for the
        /// currently active platform.
        /// </param>
        public static void ApplySet(
            ScriptDefinitionSet set,
            BuildTargetGroup? targetGroup = null)
        {
            if (set == null)
            {
                FireFailed("Script definition set is null.");
                return;
            }

            BuildTargetGroup target = targetGroup
                                      ?? EditorUserBuildSettings.selectedBuildTargetGroup;

            // Validate all symbols first
            string validationError = ValidateSet(set);
            if (validationError != null)
            {
                FireFailed(validationError);
                return;
            }

            try
            {
                // Current defines as a set
                string currentDefinesString =
                    PlayerSettings.GetScriptingDefineSymbols(
                        UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(target));
                HashSet<string> currentDefines = ParseDefines(currentDefinesString);

                // Symbols owned by this set (both enabled and disabled)
                HashSet<string> ownedSymbols = new(StringComparer.Ordinal);
                foreach (ScriptDefinitionSlot slot in set.slots)
                {
                    if (!string.IsNullOrEmpty(slot.defineSymbol))
                    {
                        ownedSymbols.Add(slot.defineSymbol.Trim());
                    }
                }

                // Build the new define set:
                // 1. Keep all existing symbols that are NOT owned by this set
                // 2. Add symbols from this set that are enabled
                HashSet<string> newDefines = new(StringComparer.Ordinal);
                foreach (string define in currentDefines)
                {
                    if (!ownedSymbols.Contains(define))
                    {
                        newDefines.Add(define);
                    }
                }

                foreach (ScriptDefinitionSlot slot in set.slots)
                {
                    if (slot.enabled && !string.IsNullOrEmpty(slot.defineSymbol))
                    {
                        newDefines.Add(slot.defineSymbol.Trim());
                    }
                }

                // Write back
                string newDefinesString = string.Join(";", newDefines.OrderBy(d => d));
                PlayerSettings.SetScriptingDefineSymbols(
                    UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(target),
                    newDefinesString);

                ScriptDefinitionsApplied?.Invoke(set);
            }
            catch (Exception ex)
            {
                FireFailed($"Failed to apply script definitions: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Returns the current set of define symbols for the active build target.
        /// </summary>
        public static HashSet<string> GetCurrentDefines()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(
                UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup));
            return ParseDefines(defines);
        }

        /// <summary>
        /// Validates a single define symbol string.
        /// Returns an error message or null if valid.
        /// </summary>
        public static string ValidateSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return "Define symbol cannot be empty.";
            }

            string trimmed = symbol.Trim();
            if (Regex.IsMatch(trimmed, InvalidCharsPattern))
            {
                return $"Define symbol '{trimmed}' contains invalid characters. " +
                       "Only letters, digits, and underscores are allowed.";
            }

            if (!ValidIdentifierRegex.IsMatch(trimmed))
            {
                return $"Define symbol '{trimmed}' must start with a letter or underscore.";
            }

            return null;
        }

        // ══════════════════════════════════════════════════
        // ── Private Methods
        // ══════════════════════════════════════════════════

        private static string ValidateSet(ScriptDefinitionSet set)
        {
            if (set.slots == null || set.slots.Count == 0)
            {
                return $"Define set '{set.setName}' has no slots.";
            }

            foreach (ScriptDefinitionSlot slot in set.slots)
            {
                string error = ValidateSymbol(slot.defineSymbol);
                if (error != null)
                {
                    return $"Invalid symbol in set '{set.setName}': {error}";
                }
            }

            return null;
        }

        private static HashSet<string> ParseDefines(string definesString)
        {
            HashSet<string> result = new(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(definesString))
            {
                return result;
            }

            string[] parts = definesString.Split(';');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private static void FireFailed(string message)
        {
            Debug.LogError($"[NoBuild] {message}");
            ScriptDefinitionsFailed?.Invoke(message);
        }
    }
}
