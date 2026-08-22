using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Framework.Assets
{
    public sealed class SettingsAssetDefinition<T>
        where T : ScriptableObject
    {
        #region Interfaces & Properties

        public string AssetPath { get; }

        #endregion

        #region Public Methods

        public SettingsAssetDefinition(string assetPath)
        {
            AssetPath = NormalizeAssetPath(assetPath);
        }

        #endregion

        #region Private Methods

        private static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException(
                    "An asset path is required.",
                    nameof(assetPath)
                );
            }

            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!normalizedAssetPath.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The asset path must be project-relative and start with 'Assets/'.",
                    nameof(assetPath)
                );
            }

            if (!normalizedAssetPath.EndsWith(
                    ".asset",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Settings assets must use the '.asset' extension.",
                    nameof(assetPath)
                );
            }

            return normalizedAssetPath;
        }

        #endregion
    }
}
