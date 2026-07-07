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
                return cached;

            string iconName = platform switch
            {
                BuildTarget.Android              => "BuildSettings.Android",
                BuildTarget.iOS                  => "BuildSettings.iPhone",
                BuildTarget.StandaloneWindows
                or BuildTarget.StandaloneWindows64
                or BuildTarget.StandaloneOSX
                or BuildTarget.StandaloneLinux64 => "BuildSettings.Standalone",
                BuildTarget.WebGL                => "BuildSettings.WebGL",
                _                                => "BuildSettings.Standalone"
            };

            GUIContent icon = EditorGUIUtility.IconContent(iconName);
            _iconCache[platform] = icon;
            return icon;
        }

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
    }
}
