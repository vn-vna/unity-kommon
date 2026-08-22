// ═══════════════════════════════════════════════════════════
// ── PlatformIconUtility ────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Maps <see cref="BuildTarget"/> to Unity built-in platform icons
    /// and human-readable display names. Results are cached per session.
    /// </summary>
    internal static class PlatformIconUtility
    {
        // ── Private Fields
        private static readonly Dictionary<BuildTarget, GUIContent>
            _iconCache = new();
        private static readonly Dictionary<BuildTarget, string>
            _displayNameCache = new();
        private static GUIContent _buildActionIcon;
        private static GUIContent _buildAndRunActionIcon;
        private static GUIContent _runActionIcon;

        // ── Public Fields
        /// <summary>Platforms available in the icon grid picker.</summary>
        public static readonly BuildTarget[] AvailablePlatforms =
        {
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.WebGL
        };

        // ── Public Methods

        /// <summary>
        /// Returns a cached <see cref="GUIContent"/> with the built-in
        /// platform icon for the given <paramref name="platform"/>.
        /// </summary>
        public static GUIContent GetPlatformIcon(BuildTarget platform)
        {
            if (_iconCache.TryGetValue(platform, out GUIContent cached))
            {
                return cached;
            }

            string[] iconNames = platform switch
            {
                BuildTarget.Android              => new[]
                    { "BuildSettings.Android.Small", "BuildSettings.Android" },
                BuildTarget.iOS                  => new[]
                    { "BuildSettings.iPhone.Small", "BuildSettings.iPhone" },
                BuildTarget.StandaloneWindows
                or BuildTarget.StandaloneWindows64
                or BuildTarget.StandaloneOSX
                or BuildTarget.StandaloneLinux64 => new[]
                    { "BuildSettings.Standalone.Small", "BuildSettings.Standalone" },
                BuildTarget.WebGL                => new[]
                    { "BuildSettings.WebGL.Small", "BuildSettings.WebGL" },
                _                                => new[]
                    { "BuildSettings.Standalone.Small", "BuildSettings.Standalone" }
            };

            GUIContent icon = GetFirstAvailableIcon(
                iconNames,
                GetPlatformDisplayName(platform)
            );
            _iconCache[platform] = icon;
            return icon;
        }

        public static GUIContent BuildActionIcon => _buildActionIcon ??=
            GetActionContent(
                new[] { "BuildSettings.Editor.Small", "BuildSettings.Editor" },
                "Build"
            );

        public static GUIContent BuildAndRunActionIcon =>
            _buildAndRunActionIcon ??= GetActionContent(
                new[] { "PlayButton", "d_PlayButton" },
                "Build & Run"
            );

        public static GUIContent RunActionIcon => _runActionIcon ??=
            GetActionContent(
                new[] { "PlayButton", "d_PlayButton" },
                "Run"
            );

        /// <summary>
        /// Returns a cached human-readable display name for the platform.
        /// </summary>
        public static string GetPlatformDisplayName(BuildTarget platform)
        {
            if (_displayNameCache.TryGetValue(platform, out string cached))
                return cached;

            string name = platform switch
            {
                BuildTarget.StandaloneWindows
                or BuildTarget.StandaloneWindows64 => "Windows (x64)",
                BuildTarget.StandaloneOSX          => "macOS",
                BuildTarget.StandaloneLinux64      => "Linux (x64)",
                BuildTarget.iOS                    => "iOS",
                _                                  => platform.ToString()
            };

            _displayNameCache[platform] = name;
            return name;
        }

        private static GUIContent GetFirstAvailableIcon(
            IEnumerable<string> iconNames,
            string fallbackText)
        {
            foreach (string iconName in iconNames)
            {
                GUIContent content = EditorGUIUtility.IconContent(
                    iconName,
                    fallbackText
                );
                if (content?.image != null)
                {
                    return content;
                }
            }

            return new GUIContent(fallbackText);
        }

        private static GUIContent GetActionContent(
            IEnumerable<string> iconNames,
            string text)
        {
            GUIContent icon = GetFirstAvailableIcon(iconNames, text);
            return icon.image != null
                ? new GUIContent(text, icon.image, text)
                : new GUIContent(text, text);
        }
    }
}
