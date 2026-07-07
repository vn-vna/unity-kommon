// ═══════════════════════════════════════════════════════════
// ── BuildExecutor ─────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Executes a build based on a <see cref="BuildProfile"/>.
    /// Follows a transaction pattern: snapshot state → apply profile →
    /// build → restore original state (even on failure).
    /// </summary>
    internal static class BuildExecutor
    {
        // ══════════════════════════════════════════════════
        // ── Constants
        // ══════════════════════════════════════════════════

        private const string DefaultBuildDir = "Builds";

        // ══════════════════════════════════════════════════
        // ── Events & Delegates
        // ══════════════════════════════════════════════════

        public static event Action<BuildProfile, BuildReport> BuildCompleted;
        public static event Action<BuildProfile, string> BuildFailed;

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Executes a full build based on the given profile.
        /// </summary>
        public static void Build(BuildProfile profile, string outputPath = null)
        {
            if (profile == null)
            {
                FireFailed(null, "Build profile is null.");
                return;
            }

            NoBuildSettings settings = NoBuildResourceUtility.GetSettings();
            if (settings == null)
            {
                FireFailed(profile, "NoBuild settings not found. Create them in Project Settings.");
                return;
            }

            // Validate
            string validationError = ValidateProfile(profile, settings);
            if (validationError != null)
            {
                FireFailed(profile, validationError);
                EditorUtility.DisplayDialog(
                    "NoBuild — Build Validation Failed",
                    validationError,
                    "OK"
                );
                return;
            }

            BuildConfiguration config = profile.buildConfiguration;
            BuildTarget buildTarget = config.platform;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            UnityEditor.Build.NamedBuildTarget namedTarget =
                UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);

            // Snapshot original state
            BuildStateSnapshot snapshot = CaptureBuildState(namedTarget, targetGroup);

            try
            {
                // ── Pre-Build Phase ────────────────
                // Save current scene
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                // Apply script defines
                if (profile.HasValidDefineSet(settings))
                {
                    ScriptDefinitionSet defineSet =
                        settings.scriptDefinitionSets[profile.scriptDefinitionSetIndex];
                    ScriptDefinitionSwitcher.ApplySet(defineSet, targetGroup);
                }

                // Apply build configuration overrides
                ApplyBuildConfiguration(config, namedTarget, targetGroup);

                // Build scene list from the scene set
                string[] scenePaths = GetScenePaths(profile, settings);
                if (scenePaths.Length == 0)
                {
                    throw new InvalidOperationException("No valid scenes to build.");
                }

                // Resolve output path: folder + name template + platform extension
                string resolvedFolder =
                    BuildNameResolver.Resolve(
                        profile.buildFolder?.template ?? "{project-root}/Builds",
                        profile, settings);
                string resolvedName =
                    BuildNameResolver.Resolve(
                        profile.buildNameTemplate?.template ?? "{app-version}",
                        profile, settings);
                string resolvedOutputPath = outputPath
                    ?? Path.Combine(resolvedFolder,
                        resolvedName + GetPlatformExtension(buildTarget));

                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(resolvedOutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // ── Build Phase ────────────────────
                BuildPlayerOptions buildOptions = new BuildPlayerOptions
                {
                    scenes = scenePaths,
                    locationPathName = resolvedOutputPath,
                    target = buildTarget,
                    options = BuildOptions.None
                };

                if (config.developmentBuild)
                {
                    buildOptions.options |= BuildOptions.Development;
                }

                if (config.allowDebugging)
                {
                    buildOptions.options |= BuildOptions.AllowDebugging;
                }

                if (config.connectWithProfiler)
                {
                    buildOptions.options |= BuildOptions.ConnectWithProfiler;
                }

                Debug.Log(
                    $"[NoBuild] Starting build: {profile.profileName} → {resolvedOutputPath}"
                );

                BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(
                        $"[NoBuild] Build succeeded. " +
                        $"Platform: {summary.platform}, " +
                        $"Size: {summary.totalSize / 1024 / 1024} MB, " +
                        $"Time: {summary.totalTime}"
                    );
                    BuildCompleted?.Invoke(profile, report);
                }
                else
                {
                    string errorMsg =
                        $"Build failed with {summary.totalErrors} error(s). " +
                        $"Result: {summary.result}";
                    Debug.LogError($"[NoBuild] {errorMsg}");
                    BuildFailed?.Invoke(profile, errorMsg);
                    EditorUtility.DisplayDialog("NoBuild — Build Failed", errorMsg, "OK");
                }
            }
            catch (Exception ex)
            {
                FireFailed(profile, $"Build exception: {ex.Message}");
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "NoBuild — Build Failed",
                    $"An error occurred during the build:\n{ex.Message}",
                    "OK"
                );
            }
            finally
            {
                // ── Restore Phase ──────────────────
                RestoreBuildState(snapshot, namedTarget, targetGroup);
            }
        }

        /// <summary>Device selection mode for Build &amp; Run.</summary>
        public enum DeviceOption
        {
            FirstDevice,
            AllDevices,
            SpecificDevice
        }

        /// <summary>
        /// Build, install, and run on one or more Android devices.
        /// </summary>
        public static void BuildAndRunWithOptions(
            BuildProfile profile,
            DeviceOption deviceOption,
            string specificSerial = null)
        {
            if (profile.buildConfiguration.platform
                != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("NoBuild",
                    "Build & Run currently only supports Android.",
                    "OK");
                return;
            }

            // 1 ── Build ─────────────────────
            bool wasAab =
                EditorUserBuildSettings.buildAppBundle;
            try
            {
                // Force APK for install
                EditorUserBuildSettings.buildAppBundle =
                    false;

                Build(profile);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle =
                    wasAab;
            }

            // 2 ── Get devices ──────────────
            var devices = AdbUtility.GetDevices();
            if (devices.Count == 0)
            {
                EditorUtility.DisplayDialog("NoBuild",
                    "No Android device connected via ADB.",
                    "OK");
                return;
            }

            // 3 ── Select target devices ────
            List<AdbDeviceInfo> targets = new();
            switch (deviceOption)
            {
                case DeviceOption.FirstDevice:
                    targets.Add(devices[0]);
                    break;
                case DeviceOption.AllDevices:
                    targets.AddRange(devices);
                    break;
                case DeviceOption.SpecificDevice:
                    var match = devices.Find(
                        d => d.Serial == specificSerial);
                    if (match.Serial == null)
                    {
                        EditorUtility.DisplayDialog(
                            "NoBuild",
                            $"Device '{specificSerial}' " +
                            "not found.",
                            "OK");
                        return;
                    }
                    targets.Add(match);
                    break;
            }

            // 4 ── Resolve APK path ─────────
            NoBuildSettings s =
                NoBuildResourceUtility.GetSettings();
            string folder = BuildNameResolver.Resolve(
                profile.buildFolder?.template
                    ?? "{project-root}/Build",
                profile, s);
            string name = BuildNameResolver.Resolve(
                profile.buildNameTemplate?.template
                    ?? "{app-version}", profile, s);
            string apkPath = Path.Combine(
                folder,
                name + GetPlatformExtension(
                    profile.buildConfiguration.platform));

            if (!File.Exists(apkPath))
            {
                EditorUtility.DisplayDialog("NoBuild",
                    $"APK not found at:\n{apkPath}", "OK");
                return;
            }

            string packageName = AdbUtility.GetPackageName();

            // 5 ── Install + Launch per device
            int successCount = 0;
            int failCount = 0;

            foreach (var device in targets)
            {
                EditorUtility.DisplayProgressBar(
                    "NoBuild",
                    $"Installing to {device.DisplayName}...",
                    (float)successCount / targets.Count);

                bool installed = AdbUtility.InstallApk(
                    apkPath, device.Serial);
                if (!installed)
                {
                    failCount++;
                    Debug.LogError(
                        $"[NoBuild] Install failed on " +
                        $"{device.DisplayName}");
                    continue;
                }

                bool launched = AdbUtility.LaunchApp(
                    device.Serial, packageName);
                if (launched)
                {
                    successCount++;
                    Debug.Log(
                        $"[NoBuild] Launched on " +
                        $"{device.DisplayName} " +
                        $"({device.Serial})");
                    EditorPrefs.SetString(
                        "NoBuild_LastAdbDevice",
                        device.Serial);
                }
                else
                {
                    failCount++;
                    Debug.LogError(
                        $"[NoBuild] Launch failed on " +
                        $"{device.DisplayName}");
                }
            }

            EditorUtility.ClearProgressBar();

            // 6 ── Summary ─────────────────
            if (failCount > 0 && successCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "NoBuild",
                    $"Failed to install/launch on all " +
                    $"{targets.Count} device(s).",
                    "OK");
            }
            else if (failCount > 0)
            {
                EditorUtility.DisplayDialog(
                    "NoBuild",
                    $"{successCount} device(s) succeeded, " +
                    $"{failCount} failed.",
                    "OK");
            }
            // All succeeded — silent (log only)
        }

        // ══════════════════════════════════════════════════
        // ── Private Methods
        // ══════════════════════════════════════════════════

        private static string ValidateProfile(BuildProfile profile, NoBuildSettings settings)
        {
            if (!profile.HasValidSceneSet(settings))
            {
                return $"Build profile '{profile.profileName}' has an invalid scene set reference.";
            }

            SceneSet sceneSet = settings.sceneSets[profile.sceneSetIndex];
            if (!sceneSet.HasContent)
            {
                return $"Scene set '{sceneSet.setName}' has no scenes configured.";
            }

            int validCount = 0;
            foreach (SceneSlot slot in sceneSet.scenes)
            {
                if (slot.enabled && slot.IsValid) validCount++;
            }

            if (validCount == 0)
            {
                return $"No valid, enabled scenes in set '{sceneSet.setName}'.";
            }

            return null;
        }

        private static string[] GetScenePaths(BuildProfile profile, NoBuildSettings settings)
        {
            SceneSet sceneSet = settings.sceneSets[profile.sceneSetIndex];

            // Use buildOrderOverride if available, otherwise use scenes in natural order
            List<SceneSlot> orderedSlots = sceneSet.buildOrderOverride.Count > 0
                ? sceneSet.buildOrderOverride
                : sceneSet.scenes;

            List<string> paths = new();
            foreach (SceneSlot slot in orderedSlots)
            {
                if (slot.enabled && slot.IsValid)
                {
                    paths.Add(slot.ScenePath);
                }
            }

            if (paths.Count == 0)
            {
                // Fallback: use currently open scenes
                Debug.LogWarning(
                    "[NoBuild] No valid scenes in set — falling back to currently open scenes."
                );
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    string path =
                        UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            return paths.ToArray();
        }

        private static BuildStateSnapshot CaptureBuildState(
            UnityEditor.Build.NamedBuildTarget namedTarget,
            BuildTargetGroup targetGroup)
        {
            return new BuildStateSnapshot
            {
                scriptDefines =
                    PlayerSettings.GetScriptingDefineSymbols(namedTarget),
                scriptingBackend =
                    PlayerSettings.GetScriptingBackend(namedTarget),
                il2CppCodeGeneration =
                    PlayerSettings.GetIl2CppCodeGeneration(namedTarget),
                strippingLevel =
                    PlayerSettings.GetManagedStrippingLevel(namedTarget),
                stripEngineCode =
                    PlayerSettings.stripEngineCode,
                developmentBuild =
                    EditorUserBuildSettings.development,
                allowDebugging =
                    EditorUserBuildSettings.allowDebugging,
                connectProfiler =
                    EditorUserBuildSettings.connectProfiler,
                bundleIdentifier =
                    PlayerSettings.GetApplicationIdentifier(namedTarget),
                productName =
                    PlayerSettings.productName,
#if UNITY_ANDROID
                androidArchitecture =
                    PlayerSettings.Android.targetArchitectures,
                androidExportProject =
                    EditorUserBuildSettings.exportAsGoogleAndroidProject,
                androidBuildAppBundle =
                    EditorUserBuildSettings.buildAppBundle,
                androidSplitBinary =
                    PlayerSettings.Android.splitApplicationBinary,
#if UNITY_ANDROID
                debugSymbolLevel =
                    UnityEditor.Android.UserBuildSettings
                        .DebugSymbols.level,
                debugSymbolFormat =
                    UnityEditor.Android.UserBuildSettings
                        .DebugSymbols.format,
#endif
#endif
#if UNITY_IOS
                iosSymlinkFramework =
                    PlayerSettings.iOS.symlinkUnityLibraries,
                iosTeamId =
                    PlayerSettings.iOS.appleDeveloperTeamID,
                iosAutomaticSigning =
                    PlayerSettings.iOS.appleEnableAutomaticSigning,
#endif
            };
        }

        private static void ApplyBuildConfiguration(
            BuildConfiguration config,
            UnityEditor.Build.NamedBuildTarget namedTarget,
            BuildTargetGroup targetGroup)
        {
            PlayerSettings.SetScriptingBackend(namedTarget, config.scriptingBackend);
            PlayerSettings.SetIl2CppCodeGeneration(namedTarget, config.il2CppCodeGeneration);
            PlayerSettings.SetManagedStrippingLevel(namedTarget, config.strippingLevel);
            PlayerSettings.stripEngineCode = config.stripEngineCode;

            EditorUserBuildSettings.development = config.developmentBuild;
            EditorUserBuildSettings.allowDebugging = config.allowDebugging;
            EditorUserBuildSettings.connectProfiler = config.connectWithProfiler;

            if (!string.IsNullOrEmpty(config.bundleIdentifierOverride))
                PlayerSettings.SetApplicationIdentifier(namedTarget, config.bundleIdentifierOverride);
            if (!string.IsNullOrEmpty(config.productNameOverride))
                PlayerSettings.productName = config.productNameOverride;

#if UNITY_ANDROID
            PlayerSettings.Android.targetArchitectures = config.androidTargetArchitecture;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = config.androidExportProject;
            EditorUserBuildSettings.buildAppBundle = config.androidBuildAppBundle;
            PlayerSettings.Android.splitApplicationBinary = config.androidSplitBinary;
#if UNITY_ANDROID
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level =
                config.debugSymbolLevel;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.format =
                config.debugSymbolFormat;
#endif
#endif

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE
            // Windows-specific
            // createVSProject is handled via BuildOptions
#endif

#if UNITY_IOS
            PlayerSettings.iOS.symlinkUnityLibraries = config.iosSymlinkFramework;
            if (!string.IsNullOrEmpty(config.iosTeamId))
                PlayerSettings.iOS.appleDeveloperTeamID = config.iosTeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = config.iosAutomaticSigning;
#endif
        }

        private static void RestoreBuildState(
            BuildStateSnapshot snapshot,
            UnityEditor.Build.NamedBuildTarget namedTarget,
            BuildTargetGroup targetGroup)
        {
            if (snapshot == null) return;

            try
            {
                PlayerSettings.SetScriptingDefineSymbols(
                    namedTarget,
                    snapshot.scriptDefines ?? ""
                );
                PlayerSettings.SetScriptingBackend(namedTarget, snapshot.scriptingBackend);
                PlayerSettings.SetIl2CppCodeGeneration(namedTarget, snapshot.il2CppCodeGeneration);
                PlayerSettings.SetManagedStrippingLevel(namedTarget, snapshot.strippingLevel);
                PlayerSettings.stripEngineCode = snapshot.stripEngineCode;
                EditorUserBuildSettings.development = snapshot.developmentBuild;
                EditorUserBuildSettings.allowDebugging = snapshot.allowDebugging;
                EditorUserBuildSettings.connectProfiler = snapshot.connectProfiler;

                if (!string.IsNullOrEmpty(snapshot.bundleIdentifier))
                    PlayerSettings.SetApplicationIdentifier(namedTarget, snapshot.bundleIdentifier);
                if (!string.IsNullOrEmpty(snapshot.productName))
                    PlayerSettings.productName = snapshot.productName;

#if UNITY_ANDROID
                PlayerSettings.Android.targetArchitectures = snapshot.androidArchitecture;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = snapshot.androidExportProject;
                EditorUserBuildSettings.buildAppBundle = snapshot.androidBuildAppBundle;
                PlayerSettings.Android.splitApplicationBinary = snapshot.androidSplitBinary;
#if UNITY_ANDROID
                UnityEditor.Android.UserBuildSettings.DebugSymbols.level =
                    snapshot.debugSymbolLevel;
                UnityEditor.Android.UserBuildSettings.DebugSymbols.format =
                    snapshot.debugSymbolFormat;
#endif
#endif
#if UNITY_IOS
                PlayerSettings.iOS.symlinkUnityLibraries = snapshot.iosSymlinkFramework;
                if (!string.IsNullOrEmpty(snapshot.iosTeamId))
                    PlayerSettings.iOS.appleDeveloperTeamID = snapshot.iosTeamId;
                PlayerSettings.iOS.appleEnableAutomaticSigning = snapshot.iosAutomaticSigning;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[NoBuild] Failed to restore build state: {ex.Message}"
                );
            }
        }

        private static string GetPlatformExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return ".apk";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.iOS:
                    return ""; // iOS builds to a folder
                case BuildTarget.WebGL:
                    return ""; // WebGL builds to a folder
                default:
                    return "";
            }
        }

        private static void FireFailed(BuildProfile profile, string message)
        {
            Debug.LogError($"[NoBuild] {message}");
            BuildFailed?.Invoke(profile, message);
        }

        // ══════════════════════════════════════════════════
        // ── Nested Types
        // ══════════════════════════════════════════════════

        private sealed class BuildStateSnapshot
        {
            public string scriptDefines;
            public ScriptingImplementation scriptingBackend;
            public Il2CppCodeGeneration il2CppCodeGeneration;
            public ManagedStrippingLevel strippingLevel;
            public bool stripEngineCode;
            public bool developmentBuild;
            public bool allowDebugging;
            public bool connectProfiler;
            public string bundleIdentifier;
            public string productName;
            public AndroidArchitecture androidArchitecture;
            public bool androidExportProject;
            public bool androidBuildAppBundle;
            public bool androidSplitBinary;
#if UNITY_ANDROID
            public Unity.Android.Types.DebugSymbolLevel
                debugSymbolLevel;
            public Unity.Android.Types.DebugSymbolFormat
                debugSymbolFormat;
#endif
            public bool iosSymlinkFramework;
            public string iosTeamId;
            public bool iosAutomaticSigning;
        }
    }
}
