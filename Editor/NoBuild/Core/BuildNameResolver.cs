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

            // ── Platform ──────────────────────
            Resolvers["platform"] = (profile, _) =>
                profile?.buildConfiguration?.platform.ToString() ?? "Unknown";
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
                    case '/':
                    case '\\':
                    case ':':
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
    }
}
