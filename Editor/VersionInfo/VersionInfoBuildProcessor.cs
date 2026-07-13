using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Editor
{
    internal class VersionInfoBuildProcessor :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        #region Constants
        private const string ConfigSearchFilter = "t:VersionInfoConfiguration";
        #endregion

        #region Interfaces & Properties
        public int callbackOrder => 0;
        #endregion

        #region IPreprocessBuildWithReport
        public void OnPreprocessBuild(BuildReport report)
        {
            VersionInfoConfiguration config = FindConfig();
            if (config == null)
            {
                return;
            }

            Providers.ResourceTextAssetProvider provider =
                config.Provider as Providers.ResourceTextAssetProvider;
            if (provider == null)
            {
                Debug.LogWarning(
                    "[VersionInfo] No ResourceTextAssetProvider configured. "
                    + "Skipping version file injection."
                );
                return;
            }

            string pattern = config.VersionPattern;
            string version = VersionInfoSettingsProvider.VersionNameResolver
                .Resolve(pattern, GetProviders(config));

            string fileName = provider.ResourceFileName;
            string directory = "Assets/Resources";
            string filePath = Path.Combine(directory, $"{fileName}.txt");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, version);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[VersionInfo] Injected version to '{filePath}': {version}");
        }
        #endregion

        #region IPostprocessBuildWithReport
        public void OnPostprocessBuild(BuildReport report)
        {
            VersionInfoConfiguration config = FindConfig();
            if (config == null)
            {
                return;
            }

            Providers.ResourceTextAssetProvider provider =
                config.Provider as Providers.ResourceTextAssetProvider;
            if (provider == null)
            {
                return;
            }

            string fileName = provider.ResourceFileName;
            string filePath = Path.Combine("Assets/Resources", $"{fileName}.txt");

            if (!File.Exists(filePath))
            {
                return;
            }

            File.Delete(filePath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[VersionInfo] Removed injected version file '{filePath}'.");
        }
        #endregion

        #region Private Methods
        private static VersionInfoConfiguration FindConfig()
        {
            return AssetDatabase
                .FindAssets(ConfigSearchFilter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<VersionInfoConfiguration>)
                .FirstOrDefault();
        }

        private static IEnumerable<IVersionNamePlaceholderProvider>
            GetProviders(VersionInfoConfiguration config)
        {
            if (config?.PlaceholderProviderAssets == null)
            {
                yield break;
            }

            foreach (ScriptableObject asset in config.PlaceholderProviderAssets)
            {
                if (asset is IVersionNamePlaceholderProvider provider)
                {
                    yield return provider;
                }
            }
        }
        #endregion
    }
}
