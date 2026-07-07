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
                        flag, settings));
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
                                flag, settings));
                    return match.Value;
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
            FlagDefinition flag, NoBuildSettings settings)
        {
            if (flag == null || settings == null)
                return "";

            HashSet<string> currentDefines =
                ScriptDefinitionSwitcher.GetCurrentDefines();
            bool isActive = false;

            if (flag.type == FlagDefinitionType.Template)
            {
                if (flag.scriptDefinitionSetIndex >= 0
                    && flag.scriptDefinitionSetIndex
                    < settings.scriptDefinitionSets.Count)
                {
                    ScriptDefinitionSet set =
                        settings.scriptDefinitionSets[
                            flag.scriptDefinitionSetIndex];
                    if (set.slots != null
                        && flag.scriptDefinitionSlotIndex >= 0
                        && flag.scriptDefinitionSlotIndex
                        < set.slots.Count)
                    {
                        ScriptDefinitionSlot slot =
                            set.slots[
                                flag.scriptDefinitionSlotIndex];
                        isActive = slot.enabled
                            && !string.IsNullOrEmpty(
                                slot.defineSymbol)
                            && currentDefines.Contains(
                                slot.defineSymbol);
                    }
                }
            }
            else // Custom
            {
                isActive = !string.IsNullOrEmpty(
                    flag.customDefineSymbol)
                    && currentDefines.Contains(
                        flag.customDefineSymbol);
            }

            return isActive
                ? flag.trueFlag
                : flag.falseFlag;
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
    }
}
