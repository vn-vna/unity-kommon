// ═══════════════════════════════════════════════════════════
// ── NoBuildResourceUtility ────────────────────────────
// ═══════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Centralized utility for loading, creating, and caching the
    /// <see cref="NoBuildSettings"/> ScriptableObject.
    /// </summary>
    internal static class NoBuildResourceUtility
    {
        // ══════════════════════════════════════════════════
        // ── Private Fields
        // ══════════════════════════════════════════════════

        private static NoBuildSettings _cachedSettings;
        private static bool _cacheValid;

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Returns the current <see cref="NoBuildSettings"/> or creates it if missing.
        /// Never returns null in normal operation.
        /// </summary>
        public static NoBuildSettings GetOrCreateSettings()
        {
            NoBuildSettings settings = GetSettings();
            if (settings != null)
            {
                return settings;
            }

            // Ensure the Resources/NoBuild folder exists
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string noBuildFolder = NoBuildSettings.AssetFolder;
            if (!AssetDatabase.IsValidFolder(noBuildFolder))
            {
                AssetDatabase.CreateFolder(resourcesPath, "NoBuild");
            }

            // Create the ScriptableObject asset
            settings = ScriptableObject.CreateInstance<NoBuildSettings>();
            AssetDatabase.CreateAsset(settings, NoBuildSettings.AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _cachedSettings = settings;
            _cacheValid = true;

            Debug.Log($"[NoBuild] Created default settings at {NoBuildSettings.AssetPath}");
            return settings;
        }

        /// <summary>
        /// Returns the current <see cref="NoBuildSettings"/> or null if not yet created.
        /// Results are cached for the current domain reload cycle.
        /// </summary>
        public static NoBuildSettings GetSettings()
        {
            if (_cacheValid && _cachedSettings != null)
            {
                return _cachedSettings;
            }

            _cachedSettings = Resources.Load<NoBuildSettings>(NoBuildSettings.ResourcePath);
            _cacheValid = true;
            return _cachedSettings;
        }

        /// <summary>
        /// Invalidates the cache. Call after creating or deleting the asset externally.
        /// </summary>
        public static void InvalidateCache()
        {
            _cacheValid = false;
            _cachedSettings = null;
        }

        /// <summary>
        /// Returns true if the <see cref="NoBuildSettings"/> asset exists and is loaded.
        /// </summary>
        public static bool SettingsExist()
        {
            return GetSettings() != null;
        }
    }
}
