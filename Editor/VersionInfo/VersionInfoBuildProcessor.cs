using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Com.Scheherazade.Common.VIC.Editor
{
    [InitializeOnLoad]
    internal class VersionInfoBuildProcessor :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        #region Constants
        private const string SettingsAssetPath =
            "Assets/Resources/VersionInfoConfiguration.asset";

        private const string ResourcesFolder = "Assets/Resources";

        private const string RecoveryFolder = "Library/VersionInfo";

        private const string RecoveryStatePath =
            RecoveryFolder + "/BuildInjectionState.json";

        private const string TextBackupPath =
            RecoveryFolder + "/OriginalText.bin";

        private const string MetaBackupPath =
            RecoveryFolder + "/OriginalMeta.bin";
        #endregion

        #region Interfaces & Properties
        public int callbackOrder => 0;
        #endregion

        #region Constructor
        static VersionInfoBuildProcessor()
        {
            ScheduleRecovery();
        }
        #endregion

        #region IPreprocessBuildWithReport
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TryRestoreInjection(out string recoveryError))
            {
                throw new BuildFailedException(
                    "[VersionInfo] Could not recover a stale version injection. "
                    + recoveryError
                );
            }

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
                .Resolve(
                    pattern,
                    GetProviders(config),
                    report.summary.platform
                );

            string fileName = provider.ResourceFileName;
            string filePath = GetVersionAssetPath(fileName);

            try
            {
                PrepareRecovery(filePath);

                string outputDirectory = Path.GetDirectoryName(
                    filePath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                File.WriteAllText(filePath, version);
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                TryRestoreInjection(out string restoreError);
                throw new BuildFailedException(
                    "[VersionInfo] Failed to inject the version file. "
                    + exception.Message
                    + (string.IsNullOrEmpty(restoreError)
                        ? ""
                        : " Recovery also failed: " + restoreError)
                );
            }

            Debug.Log(
                $"[VersionInfo] Injected version to '{filePath}': {version}");
        }
        #endregion

        #region IPostprocessBuildWithReport
        public void OnPostprocessBuild(BuildReport report)
        {
            if (!TryRestoreInjection(out string error))
            {
                Debug.LogError(
                    "[VersionInfo] Failed to restore the version file after "
                    + "the build. Recovery state was retained for the next "
                    + "idle editor update. " + error
                );
            }
        }
        #endregion

        #region Private Methods
        private static VersionInfoConfiguration FindConfig()
        {
            return AssetDatabase.LoadAssetAtPath<VersionInfoConfiguration>(
                SettingsAssetPath
            );
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

        internal static string GetVersionAssetPath(
            string resourceFileName)
        {
            if (string.IsNullOrWhiteSpace(resourceFileName))
            {
                throw new BuildFailedException(
                    "[VersionInfo] Resource file name cannot be empty.");
            }

            string normalized = resourceFileName
                .Trim()
                .Replace('\\', '/')
                .Trim('/');
            if (Path.IsPathRooted(normalized)
                || normalized == ".."
                || normalized.StartsWith("../")
                || normalized.Contains("/../"))
            {
                throw new BuildFailedException(
                    "[VersionInfo] Resource file name must stay "
                    + "inside Assets/Resources.");
            }

            return $"{ResourcesFolder}/{normalized}.txt";
        }

        internal static void PrepareRecovery(string assetPath)
        {
            Directory.CreateDirectory(RecoveryFolder);

            string metaPath = assetPath + ".meta";
            InjectionRecoveryState state = new InjectionRecoveryState
            {
                AssetPath = assetPath,
                HadTextAsset = File.Exists(assetPath),
                HadMetaFile = File.Exists(metaPath)
            };

            DeleteIfExists(TextBackupPath);
            DeleteIfExists(MetaBackupPath);

            if (state.HadTextAsset)
            {
                File.Copy(assetPath, TextBackupPath, true);
            }

            if (state.HadMetaFile)
            {
                File.Copy(metaPath, MetaBackupPath, true);
            }

            WriteRecoveryState(state);
            ScheduleRecovery();
        }

        private static void WriteRecoveryState(InjectionRecoveryState state)
        {
            string temporaryPath = RecoveryStatePath + ".tmp";
            DeleteIfExists(temporaryPath);
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(state));
            File.Move(temporaryPath, RecoveryStatePath);
        }

        internal static bool TryRestoreInjection(out string error)
        {
            error = null;
            if (!File.Exists(RecoveryStatePath))
            {
                return true;
            }

            try
            {
                InjectionRecoveryState state = JsonUtility.FromJson<
                    InjectionRecoveryState>(File.ReadAllText(RecoveryStatePath));
                if (state == null || string.IsNullOrEmpty(state.AssetPath))
                {
                    throw new InvalidDataException(
                        "The recovery state does not contain an asset path."
                    );
                }

                RestoreFile(
                    state.AssetPath,
                    TextBackupPath,
                    state.HadTextAsset
                );
                RestoreFile(
                    state.AssetPath + ".meta",
                    MetaBackupPath,
                    state.HadMetaFile
                );

                DeleteIfExists(RecoveryStatePath);
                DeleteIfExists(TextBackupPath);
                DeleteIfExists(MetaBackupPath);
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[VersionInfo] Restored version asset '{state.AssetPath}'."
                );
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void RestoreFile(
            string targetPath,
            string backupPath,
            bool existedBeforeInjection)
        {
            if (existedBeforeInjection)
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException(
                        "A required VersionInfo recovery backup is missing.",
                        backupPath
                    );
                }

                File.Copy(backupPath, targetPath, true);
                return;
            }

            DeleteIfExists(targetPath);
        }

        private static void ScheduleRecovery()
        {
            EditorApplication.update -= RecoverStaleInjectionWhenIdle;
            EditorApplication.update += RecoverStaleInjectionWhenIdle;
        }

        private static void RecoverStaleInjectionWhenIdle()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            EditorApplication.update -= RecoverStaleInjectionWhenIdle;

            if (!TryRestoreInjection(out string error))
            {
                Debug.LogError(
                    "[VersionInfo] Failed to recover a stale version injection. "
                    + error
                );
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        #endregion

        #region Nested Types
        [Serializable]
        private sealed class InjectionRecoveryState
        {
            public string AssetPath;

            public bool HadTextAsset;

            public bool HadMetaFile;
        }
        #endregion
    }
}
