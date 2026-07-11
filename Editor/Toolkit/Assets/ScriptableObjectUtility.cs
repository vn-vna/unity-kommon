// ═══════════════════════════════════════════════════════════
// ── ScriptableObjectUtility ───────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Utility methods for finding, creating, and managing ScriptableObject assets.
    /// </summary>
    public static class ScriptableObjectUtility
    {
        #region Public Methods

        /// <summary>
        /// Finds an existing asset of type <typeparamref name="T"/> or creates one
        /// at the specified path. If multiple exist, returns the first.
        /// </summary>
        public static T FindOrCreateAsset<T>(
            string folderPath, string assetName)
            where T : ScriptableObject
        {
            // Search existing assets
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    return asset;
            }

            // Create new
            return CreateAsset<T>(folderPath, assetName);
        }

        /// <summary>
        /// Creates a new ScriptableObject asset at the specified path.
        /// Creates intermediate folders as needed.
        /// </summary>
        public static T CreateAsset<T>(string folderPath, string assetName)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();

            EnsureDirectoryExists(folderPath);

            string fullPath = Path.Combine(folderPath, assetName);
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            AssetDatabase.CreateAsset(asset, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return asset;
        }

        /// <summary>
        /// Creates an asset of the specified concrete type.
        /// Useful when the type is determined at runtime.
        /// </summary>
        public static ScriptableObject CreateAsset(
            Type assetType, string folderPath, string assetName)
        {
            ScriptableObject asset = ScriptableObject.CreateInstance(assetType);

            EnsureDirectoryExists(folderPath);

            string fullPath = Path.Combine(folderPath, assetName);
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            AssetDatabase.CreateAsset(asset, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return asset;
        }

        /// <summary>
        /// Safely deletes an asset by path. Does nothing if the asset doesn't exist.
        /// </summary>
        public static void DeleteAssetSafe(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath);

            if (!File.Exists(absolutePath))
                return;

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Finds all assets of type <typeparamref name="T"/> in the project.
        /// </summary>
        public static List<T> FindAllAssets<T>() where T : ScriptableObject
        {
            List<T> results = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    results.Add(asset);
            }

            return results;
        }

        /// <summary>
        /// Returns the standard Resources path for the given subfolder.
        /// Example: GetResourcesPath("Chrono") → "Assets/Resources/Chrono"
        /// </summary>
        public static string GetResourcesPath(string subfolder = null)
        {
            string path = "Assets/Resources";
            if (!string.IsNullOrEmpty(subfolder))
                path = Path.Combine(path, subfolder);
            return path;
        }

        /// <summary>
        /// Ensures a project-relative directory exists, creating it if needed.
        /// </summary>
        public static void EnsureDirectoryExists(string folderPath)
        {
            string fullPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                folderPath);

            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        #endregion
    }
}
