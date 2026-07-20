// ═══════════════════════════════════════════════════════════
// ── BuildNameResolver ────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Registry of placeholder → resolver functions for build name templates.
    /// New placeholders can be registered by any code at any time via the
    /// static <see cref="Resolvers"/> dictionary.
    /// </summary>
    internal static class BuildNameResolver
    {
        // ══════════════════════════════════════════════════
        // ── Constants
        // ══════════════════════════════════════════════════

        private static readonly Regex PlaceholderRegex =
            new(@"\{([^{}]+)\}", RegexOptions.Compiled);

        // ══════════════════════════════════════════════════
        // ── Resolver Registry
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Maps placeholder key (without braces) to a resolver function.
        /// Built-in resolvers are registered in the static constructor.
        /// External code can add custom resolvers at any time.
        /// </summary>
        public static readonly Dictionary<string, Func<BuildProfile, NoBuildSettings, string>>
            Resolvers = new(StringComparer.OrdinalIgnoreCase);

        // ══════════════════════════════════════════════════
        // ── Static Constructor
        // ══════════════════════════════════════════════════

        static BuildNameResolver()
        {
            // ── Git ────────────────────────────
            Resolvers["git-commit"]      = (_, _) => GitUtility.ShortCommitHash;
            Resolvers["git-commit-full"] = (_, _) => GitUtility.FullCommitHash;
            Resolvers["git-branch"]      = (_, _) => GitUtility.BranchName;

            // ── App Version ───────────────────
            Resolvers["app-version"] = (_, _) => Application.version;
            Resolvers["app-bundle"]  = (_, _) =>
            {
#if UNITY_ANDROID
                return PlayerSettings.Android.bundleVersionCode.ToString();
#elif UNITY_IOS
                return PlayerSettings.iOS.buildNumber;
#else
                return PlayerSettings.bundleVersion;
#endif
            };

            // ── Profile Names ─────────────────
            Resolvers["profile-name"] = (profile, _) =>
                profile?.profileName ?? "UnknownProfile";

            Resolvers["scene-set"] = (profile, settings) =>
            {
                if (profile == null || settings == null)
                {
                    return "NoSet";
                }

                if (profile.sceneSetIndex >= 0
                    && profile.sceneSetIndex < settings.sceneSets.Count)
                {
                    return Sanitize(settings.sceneSets[profile.sceneSetIndex].setName);
                }

                return "NoSet";
            };

            Resolvers["script-defines"] = (profile, settings) =>
            {
                if (profile == null || settings == null)
                {
                    return "";
                }

                if (profile.scriptDefinitionSetIndex >= 0
                    && profile.scriptDefinitionSetIndex < settings.scriptDefinitionSets.Count)
                {
                    ScriptDefinitionSet set =
                        settings.scriptDefinitionSets[profile.scriptDefinitionSetIndex];
                    List<string> enabled = new();
                    if (set.slots != null)
                    {
                        foreach (ScriptDefinitionSlot slot in set.slots)
                        {
                            if (slot.enabled && !string.IsNullOrEmpty(slot.defineSymbol))
                            {
                                enabled.Add(slot.defineSymbol);
                            }
                        }
                    }

                    return string.Join(",", enabled);
                }

                return "";
            };

            // ── Date / Time ───────────────────
            Resolvers["date"]     = (_, _) => DateTime.Now.ToString("yyyy-MM-dd");
            Resolvers["time"]     = (_, _) => DateTime.Now.ToString("HHmmss");
            Resolvers["datetime"] = (_, _) => DateTime.Now.ToString("yyyy-MM-dd_HHmmss");

            // ── Folder paths ──────────────────
            Resolvers["project-root"] = (_, _) =>
                NormalizeSlashes(
                    System.IO.Path.GetDirectoryName(Application.dataPath) ?? "");
            Resolvers["project-root-norm"] = (_, _) =>
            {
                string raw = System.IO.Path.GetDirectoryName(
                    Application.dataPath) ?? "";
                return raw.Replace('/', '_').Replace('\\', '_');
            };
            Resolvers["asset-folder"] = (_, _) =>
                NormalizeSlashes(Application.dataPath);
            Resolvers["project-name"] = (_, _) =>
                System.IO.Path.GetFileName(
                    System.IO.Path.GetDirectoryName(Application.dataPath)) ?? "";

            // ── Platform ──────────────────────
            Resolvers["platform"] = (profile, _) =>
                profile?.buildConfiguration?.platform.ToString() ?? "Unknown";

            // ── Flags ─────────────────────────
            Resolvers["flags"] = (profile, settings) =>
            {
                if (settings == null
                    || settings.flagDefinitions == null)
                    return "";

                StringBuilder sb = new();
                foreach (FlagDefinition flag in
                    settings.flagDefinitions)
                {
                    sb.Append(ResolveSingleFlag(
                        flag, settings, profile));
                }
                return sb.ToString();
            };
        }

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Replaces all {placeholder} tokens in the template with resolved values.
        /// Unrecognized tokens are left unchanged.
        /// </summary>
        public static string Resolve(
            string template,
            BuildProfile profile,
            NoBuildSettings settings)
        {
            if (string.IsNullOrEmpty(template))
            {
                return "Build";
            }

            return PlaceholderRegex.Replace(template, match =>
            {
                string key = match.Groups[1].Value.Trim();

                // ── {flag-X} dynamic resolver ──
                if (key.StartsWith("flag-",
                        StringComparison.OrdinalIgnoreCase)
                    && key.Length > 5)
                {
                    string id = key.Substring(5).Trim();
                    FlagDefinition flag =
                        FindFlagById(id, settings);
                    if (flag != null)
                        return Sanitize(
                            ResolveSingleFlag(
                                flag, settings, profile));
                    return match.Value;
                }

                // ── {git-commit:<submodule>} dynamic ──
                if (TryParseGitCommit(key, out string submoduleName))
                {
                    return GitUtility.GetSubmoduleCommit(
                        submoduleName);
                }

                // ── {git-commit-full:<submodule>} dynamic ──
                if (TryParseGitCommitFull(key, out submoduleName))
                {
                    return GitUtility.GetSubmoduleFullCommit(
                        submoduleName);
                }

                if (Resolvers.TryGetValue(
                        key,
                        out Func<BuildProfile, NoBuildSettings, string> resolver))
                {
                    string result = resolver(profile, settings);
                    return Sanitize(result ?? "");
                }

                return match.Value;
            });
        }

        // ══════════════════════════════════════════════════
        // ── Flag Helpers
        // ══════════════════════════════════════════════════

        private static FlagDefinition FindFlagById(
            string id, NoBuildSettings settings)
        {
            if (settings?.flagDefinitions == null)
                return null;

            // Try by index
            if (int.TryParse(id, out int index)
                && index >= 0
                && index < settings.flagDefinitions.Count)
                return settings.flagDefinitions[index];

            // Try by id (case-insensitive)
            foreach (FlagDefinition f in
                settings.flagDefinitions)
            {
                if (string.Equals(f.id, id,
                        StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            return null;
        }

        private static string ResolveSingleFlag(
            FlagDefinition flag,
            NoBuildSettings settings,
            BuildProfile profile = null)
        {
            if (flag == null || settings == null)
                return "";

            bool isActive = false;

            if (flag.type == FlagDefinitionType.Template)
            {
                // Resolve the flag's own slot to extract
                // target symbol and its default enabled state
                ScriptDefinitionSlot flagSlot =
                    GetFlagSlot(flag, settings);
                if (flagSlot == null
                    || string.IsNullOrEmpty(
                        flagSlot.defineSymbol))
                {
                    // Cannot determine target — remain inactive
                }
                else if (profile != null
                    && profile.HasValidDefineSet(settings))
                {
                    // Look up the target symbol in the
                    // PROFILE's set (authority)
                    ScriptDefinitionSet profileSet =
                        settings.scriptDefinitionSets[
                            profile.scriptDefinitionSetIndex];
                    isActive = LookupSymbolInSet(
                        profileSet,
                        flagSlot.defineSymbol,
                        flagSlot.enabled);
                }
                else
                {
                    // No profile — use flag's own slot
                    isActive = flagSlot.enabled;
                }
            }
            else // Custom
            {
                // Resolve against profile's set when available
                if (profile != null
                    && profile.HasValidDefineSet(settings))
                {
                    ScriptDefinitionSet profileSet =
                        settings.scriptDefinitionSets[
                            profile.scriptDefinitionSetIndex];
                    isActive = LookupSymbolInSet(
                        profileSet,
                        flag.customDefineSymbol,
                        fallbackEnabled: false);
                    if (!isActive)
                    {
                        // Not found in profile's set — fall
                        // back to current PlayerSettings
                        HashSet<string> currentDefines =
                            ScriptDefinitionSwitcher
                                .GetCurrentDefines();
                        isActive = !string.IsNullOrEmpty(
                            flag.customDefineSymbol)
                            && currentDefines.Contains(
                                flag.customDefineSymbol);
                    }
                }
                else
                {
                    HashSet<string> currentDefines =
                        ScriptDefinitionSwitcher
                            .GetCurrentDefines();
                    isActive = !string.IsNullOrEmpty(
                        flag.customDefineSymbol)
                        && currentDefines.Contains(
                            flag.customDefineSymbol);
                }
            }

            return isActive
                ? flag.trueFlag
                : flag.falseFlag;
        }

        // ══════════════════════════════════════════════════
        // ── Flag Helpers
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Resolves the <see cref="ScriptDefinitionSlot"/> referenced
        /// by a Template-type flag.  Returns null when the reference
        /// is invalid.
        /// </summary>
        private static ScriptDefinitionSlot GetFlagSlot(
            FlagDefinition flag, NoBuildSettings settings)
        {
            if (flag == null || settings == null)
                return null;
            if (flag.scriptDefinitionSetIndex < 0
                || flag.scriptDefinitionSetIndex
                >= settings.scriptDefinitionSets.Count)
                return null;
            ScriptDefinitionSet set =
                settings.scriptDefinitionSets[
                    flag.scriptDefinitionSetIndex];
            if (set.slots == null
                || flag.scriptDefinitionSlotIndex < 0
                || flag.scriptDefinitionSlotIndex
                >= set.slots.Count)
                return null;
            return set.slots[
                flag.scriptDefinitionSlotIndex];
        }

        /// <summary>
        /// Looks up a symbol in a <see cref="ScriptDefinitionSet"/>.
        /// </summary>
        /// <param name="set">The set to search.</param>
        /// <param name="symbol">The define symbol to look for.</param>
        /// <param name="fallbackEnabled">
        /// Value to return when the symbol is NOT found in
        /// <paramref name="set"/>.
        /// </param>
        /// <returns>
        /// <c>true</c> when the symbol is found and its slot is
        /// enabled; <c>false</c> when found but disabled;
        /// <paramref name="fallbackEnabled"/> when not found.
        /// </returns>
        private static bool LookupSymbolInSet(
            ScriptDefinitionSet set,
            string symbol,
            bool fallbackEnabled)
        {
            if (set?.slots == null
                || string.IsNullOrEmpty(symbol))
                return fallbackEnabled;

            foreach (ScriptDefinitionSlot slot in set.slots)
            {
                if (string.Equals(
                    slot.defineSymbol,
                    symbol,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return slot.enabled
                        && !string.IsNullOrEmpty(
                            slot.defineSymbol);
                }
            }

            return fallbackEnabled;
        }

        // ══════════════════════════════════════════════════
        // ── Helper Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Replaces characters unsafe for file paths with underscores.
        /// </summary>
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            StringBuilder sb = new(input.Length);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '*':
                    case '?':
                    case '"':
                    case '<':
                    case '>':
                    case '|':
                        sb.Append('_');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalizes all path separators to forward slashes for cross-platform builds.
        /// </summary>
        private static string NormalizeSlashes(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace('\\', '/');
        }

        // ══════════════════════════════════════════════════
        // ── Git Submodule Helpers
        // ══════════════════════════════════════════════════

        private const string GitCommitPrefix =
            "git-commit:";
        private const string GitCommitFullPrefix =
            "git-commit-full:";

        private static bool TryParseGitCommit(
            string key, out string submoduleName)
        {
            if (key.StartsWith(
                    GitCommitPrefix,
                    StringComparison.OrdinalIgnoreCase)
                && key.Length > GitCommitPrefix.Length)
            {
                submoduleName = key.Substring(
                    GitCommitPrefix.Length).Trim();
                return true;
            }

            submoduleName = null;
            return false;
        }

        private static bool TryParseGitCommitFull(
            string key, out string submoduleName)
        {
            if (key.StartsWith(
                    GitCommitFullPrefix,
                    StringComparison.OrdinalIgnoreCase)
                && key.Length > GitCommitFullPrefix.Length)
            {
                submoduleName = key.Substring(
                    GitCommitFullPrefix.Length).Trim();
                return true;
            }

            submoduleName = null;
            return false;
        }
    }
}
