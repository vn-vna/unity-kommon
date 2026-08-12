using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.NoiseBaker;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker.Editor
{
    /// <summary>
    /// Writes a baked texture to disk as a PNG asset and applies
    /// TextureImporter settings matching the bake's output configuration.
    /// </summary>
    public static class TextureAssetWriter
    {
        /// <summary>
        /// Bakes <paramref name="settings"/> and saves it as a texture asset.
        /// </summary>
        /// <param name="settings">Bake settings (sanitized internally).</param>
        /// <param name="assetPath">Assets-relative path ending in .png, e.g. "Assets/Textures/Noise.png".</param>
        /// <returns>The imported texture asset, or null on failure.</returns>
        public static Texture2D Export(NoiseBakerSettings settings, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                QuickLog.Log("Export aborted: empty asset path.", nameof(TextureAssetWriter), LogLevel.Error, System.Array.Empty<object>());
                return null;
            }

            Texture2D baked = NoiseBaker.Bake(settings);
            byte[] png = baked.EncodeToPNG();
            Object.DestroyImmediate(baked);

            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ApplyImportSettings(settings, assetPath);
            AssetDatabase.Refresh();

            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (asset == null)
            {
                QuickLog.Log("Export failed: asset could not be loaded at {0}.", nameof(TextureAssetWriter), LogLevel.Error, new object[] { assetPath });
                return null;
            }

            EditorGUIUtility.PingObject(asset);
            QuickLog.Log("Baked {0}x{1} texture to {2}.", nameof(TextureAssetWriter), LogLevel.Info, new object[] { settings.width, settings.height, assetPath });
            return asset;
        }

        /// <summary>Applies wrap/filter/mipmap/sRGB/format settings to the importer.</summary>
        private static void ApplyImportSettings(NoiseBakerSettings settings, string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                QuickLog.Log("No TextureImporter at {0}; skipping import settings.", nameof(TextureAssetWriter), LogLevel.Warning, new object[] { assetPath });
                return;
            }

            importer.wrapMode = settings.wrapMode;
            importer.filterMode = settings.filterMode;
            importer.mipmapEnabled = settings.mipmaps;
            importer.sRGBTexture = ShouldBeSrgb(settings);

            if (settings.format == TextureFormat.R8 || settings.format == TextureFormat.R16)
            {
                importer.textureType = TextureImporterType.SingleChannel;
                importer.textureFormat = settings.format == TextureFormat.R16
                    ? TextureImporterFormat.R16
                    : TextureImporterFormat.R8;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureFormat = TextureImporterFormat.Automatic;
            }

            importer.SaveAndReimport();
        }

        private static bool ShouldBeSrgb(NoiseBakerSettings settings)
        {
            if (settings.format == TextureFormat.R8 || settings.format == TextureFormat.R16)
            {
                return false;
            }

            // Gradient/art textures are sRGB; pure data (channel packs) is linear.
            return settings.colorMode == ColorMode.Gradient;
        }
    }
}
