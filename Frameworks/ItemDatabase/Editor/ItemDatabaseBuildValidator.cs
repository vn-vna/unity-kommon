using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    internal sealed class ItemDatabaseBuildValidator : IPreprocessBuildWithReport
    {
        private const string ConfigAssetPath
            = "Assets/Resources/Integration/Managers/ItemDatabaseConfiguration.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ItemDatabaseConfiguration config = GetBuildConfiguration();
            if (config == null) return;

            var diagnostics = ItemDatabaseConfigurationValidator.Validate(config);
            var errors = diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length == 0) return;

            string summary = string.Join(
                "\n",
                errors.Select(error => $"[{error.Code}] {error.Message}")
            );
            throw new BuildFailedException(
                $"Item Database configuration has {errors.Length} blocking error(s):\n{summary}"
            );
        }

        [MenuItem("Tools/Scheherazade/Item Database/Validate Configuration")]
        private static void ValidateFromMenu()
        {
            ItemDatabaseConfiguration config;
            try
            {
                config = GetBuildConfiguration();
            }
            catch (BuildFailedException exception)
            {
                QuickLog.Error<ItemDatabaseBuildValidator>(
                    "Item Database build validation failed: {0}",
                    exception.Message
                );
                return;
            }

            if (config == null)
            {
                QuickLog.Info<ItemDatabaseBuildValidator>(
                    "Item Database is disabled; no configuration was validated."
                );
                return;
            }

            var diagnostics = ItemDatabaseConfigurationValidator.Validate(config);
            int errors = diagnostics.Count(diagnostic =>
                diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Error);
            int warnings = diagnostics.Count(diagnostic =>
                diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Warning);

            if (errors == 0 && warnings == 0)
            {
                QuickLog.Info<ItemDatabaseBuildValidator>(
                    "Item Database configuration is valid."
                );
                return;
            }

            foreach (ItemDatabaseDiagnostic diagnostic in diagnostics)
            {
                string message = $"[{diagnostic.Code}] {diagnostic.Message}";
                if (diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Error)
                {
                    Debug.LogError(message, diagnostic.Context);
                }
                else if (diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Warning)
                {
                    Debug.LogWarning(message, diagnostic.Context);
                }
            }

            QuickLog.Warning<ItemDatabaseBuildValidator>(
                "Item Database validation found {0} errors and {1} warnings.",
                errors,
                warnings
            );
        }

        private static ItemDatabaseConfiguration GetBuildConfiguration()
        {
            string[] paths = FindProjectConfigurationPaths();
            if (paths.Length == 0) return null;

            ItemDatabaseConfiguration canonical = AssetDatabase
                .LoadAssetAtPath<ItemDatabaseConfiguration>(ConfigAssetPath);
            if (canonical == null)
            {
                throw new BuildFailedException(
                    "Item Database configuration exists outside its required Resources path: "
                    + string.Join(", ", paths)
                );
            }

            if (paths.Length > 1)
            {
                throw new BuildFailedException(
                    "Multiple Item Database configurations were found: "
                    + string.Join(", ", paths)
                );
            }

            return canonical;
        }

        private static string[] FindProjectConfigurationPaths()
        {
            return AssetDatabase
                .FindAssets($"t:{nameof(ItemDatabaseConfiguration)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToArray();
        }

        internal static string[] FindAlternativeConfigurationPaths()
        {
            return FindProjectConfigurationPaths()
                .Where(path => !string.Equals(
                    path,
                    ConfigAssetPath,
                    System.StringComparison.OrdinalIgnoreCase
                ))
                .ToArray();
        }
    }
}
