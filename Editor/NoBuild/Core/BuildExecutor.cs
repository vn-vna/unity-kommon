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
        /// Returns the <see cref="BuildReport"/> on success,
        /// or <c>null</c> on validation error, exception, or
        /// build pipeline failure.
        /// </summary>
        public static BuildReport? Build(
            BuildProfile profile,
            string outputPath = null)
        {
            if (profile == null)
            {
                FireFailed(null, "Build profile is null.");
                return null;
            }

            NoBuildSettings settings =
                NoBuildResourceUtility.GetSettings();
            if (settings == null)
            {
                FireFailed(
                    profile,
                    "NoBuild settings not found. "
                    + "Create them in Project Settings.");
                return null;
            }

            // ── Validate ──────────────────────
            string validationError =
                ValidateProfile(profile, settings);
            if (validationError != null)
            {
                FireFailed(profile, validationError);
                EditorUtility.DisplayDialog(
                    "NoBuild — Build Validation Failed",
                    validationError,
                    "OK");
                return null;
            }

            BuildConfiguration config =
                profile.buildConfiguration;
            BuildTarget buildTarget = config.platform;
            BuildTargetGroup targetGroup =
                BuildPipeline.GetBuildTargetGroup(
                    buildTarget);
            UnityEditor.Build.NamedBuildTarget
                namedTarget =
                    UnityEditor.Build
                        .NamedBuildTarget
                        .FromBuildTargetGroup(targetGroup);

            // ── Snapshot ──────────────────────
            BuildStateSnapshot snapshot =
                CaptureBuildState(namedTarget,
                    targetGroup);

            try
            {
                // Wire up the current profile so that
                // name resolvers can use its designated
                // define set as authority
                BuildProfile.CurrentBuildProfile =
                    profile;

                // ── Phase 1: Save scenes ──────
                ShowProgress(
                    profile.profileName,
                    "Saving scenes...",
                    0.05f);
                EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo();

                // ── Phase 2: Apply defines ─────
                if (profile.HasValidDefineSet(settings))
                {
                    ShowProgress(
                        profile.profileName,
                        "Applying script defines...",
                        0.10f);
                    ScriptDefinitionSet defineSet =
                        settings.scriptDefinitionSets[
                            profile
                                .scriptDefinitionSetIndex];
                    ScriptDefinitionSwitcher.ApplySet(
                        defineSet, targetGroup);
                }

                // ── Phase 3: Build config ─────
                ShowProgress(
                    profile.profileName,
                    "Applying build overrides...",
                    0.15f);
                ApplyBuildConfiguration(
                    config, namedTarget, targetGroup);

                // ── Phase 4: Resolve paths ─────
                ShowProgress(
                    profile.profileName,
                    "Resolving build output...",
                    0.20f);
                string[] scenePaths =
                    GetScenePaths(profile, settings);
                if (scenePaths.Length == 0)
                {
                    throw new InvalidOperationException(
                        "No valid scenes to build.");
                }

                string resolvedFolder =
                    BuildNameResolver.Resolve(
                        profile.buildFolder?.template
                            ?? "{project-root}/Builds",
                        profile, settings);
                string resolvedName =
                    BuildNameResolver.Resolve(
                        profile.buildNameTemplate
                            ?.template
                            ?? "{app-version}",
                        profile, settings);
                string resolvedOutputPath = outputPath
                    ?? Path.Combine(
                        resolvedFolder,
                        resolvedName
                        + GetPlatformExtension(
                            buildTarget));

                // Ensure output directory exists
                string outputDir =
                    Path.GetDirectoryName(
                        resolvedOutputPath);
                if (!string.IsNullOrEmpty(outputDir)
                    && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // ── Phase 5: Build ────────────
                // Clear our bar so Unity's native
                // build progress bar takes over
                EditorUtility.ClearProgressBar();

                BuildPlayerOptions buildOptions =
                    new BuildPlayerOptions
                    {
                        scenes = scenePaths,
                        locationPathName =
                            resolvedOutputPath,
                        target = buildTarget,
                        options = BuildOptions.None
                    };

                if (config.developmentBuild)
                {
                    buildOptions.options |=
                        BuildOptions.Development;
                }

                if (config.allowDebugging)
                {
                    buildOptions.options |=
                        BuildOptions.AllowDebugging;
                }

                if (config.connectWithProfiler)
                {
                    buildOptions.options |=
                        BuildOptions.ConnectWithProfiler;
                }

                Debug.Log(
                    $"[NoBuild] Starting build: "
                    + $"{profile.profileName} → "
                    + $"{resolvedOutputPath}");

                BuildReport report =
                    BuildPipeline.BuildPlayer(
                        buildOptions);
                BuildSummary summary = report.summary;

                if (summary.result
                    == BuildResult.Succeeded)
                {
                    Debug.Log(
                        $"[NoBuild] Build succeeded. "
                        + $"Platform: "
                        + $"{summary.platform}, "
                        + $"Size: "
                        + $"{summary.totalSize / 1024 / 1024}"
                        + " MB, "
                        + $"Time: {summary.totalTime}");
                    BuildCompleted?.Invoke(
                        profile, report);
                    return report;
                }

                string errorMsg =
                    $"Build failed with "
                    + $"{summary.totalErrors} error(s). "
                    + $"Result: {summary.result}";
                Debug.LogError(
                    $"[NoBuild] {errorMsg}");
                BuildFailed?.Invoke(profile, errorMsg);
                EditorUtility.DisplayDialog(
                    "NoBuild — Build Failed",
                    errorMsg, "OK");
                return null;
            }
            catch (Exception ex)
            {
                FireFailed(
                    profile,
                    $"Build exception: {ex.Message}");
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "NoBuild — Build Failed",
                    "An error occurred during the "
                    + $"build:\n{ex.Message}",
                    "OK");
                return null;
            }
            finally
            {
                BuildProfile.CurrentBuildProfile = null;

                ShowProgress(
                    profile.profileName,
                    "Restoring editor state...",
                    0.80f);

                RestoreBuildState(
                    snapshot, namedTarget,
                    targetGroup);

                EditorUtility.ClearProgressBar();
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

            string tempApksPath = null;
            bool isAab =
                profile.buildConfiguration
                    .androidBuildAppBundle;

            try
            {
                // 1 ── Build ─────────────────
                BuildReport? report = Build(profile);
                if (report == null
                    || report.summary.result
                    != BuildResult.Succeeded)
                {
                    // Build() already showed a failure
                    // dialog — just abort the run
                    return;
                }

                // 2 ── Get devices ───────────
                ShowProgress(
                    profile.profileName,
                    "Detecting devices...",
                    0f);
                var devices = AdbUtility.GetDevices();
                if (devices.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "NoBuild",
                        "No Android device connected "
                        + "via ADB.",
                        "OK");
                    return;
                }

                // 3 ── Select target devices ──
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
                            d => d.Serial
                                == specificSerial);
                        if (match.Serial == null)
                        {
                            EditorUtility.DisplayDialog(
                                "NoBuild",
                                $"Device "
                                + $"'{specificSerial}' "
                                + "not found.",
                                "OK");
                            return;
                        }

                        targets.Add(match);
                        break;
                }

                // 4 ── Resolve output path ───
                ShowProgress(
                    profile.profileName,
                    "Resolving build output path...",
                    0f);
                NoBuildSettings s =
                    NoBuildResourceUtility.GetSettings();
                string folder =
                    BuildNameResolver.Resolve(
                        profile.buildFolder?.template
                            ?? "{project-root}/Build",
                        profile, s);
                string name =
                    BuildNameResolver.Resolve(
                        profile.buildNameTemplate
                            ?.template
                            ?? "{app-version}",
                        profile, s);

                string extension =
                    GetPlatformExtension(
                        profile.buildConfiguration
                            .platform,
                        profile.buildConfiguration);
                string outputPath = Path.Combine(
                    folder, name + extension);

                if (!File.Exists(outputPath))
                {
                    EditorUtility.DisplayDialog(
                        "NoBuild",
                        "Build output not found at:\n"
                        + $"{outputPath}",
                        "OK");
                    return;
                }

                string installPath = outputPath;

                // 5 ── AAB → APKS conversion ─
                if (isAab)
                {
                    // BuildApks() manages its own
                    // progress bar internally
                    tempApksPath =
                        AabUtility.BuildApks(
                            outputPath);
                    installPath = tempApksPath;
                }

                // 6 ── Install + Launch ──────
                string packageName =
                    AdbUtility.GetPackageName();
                int successCount = 0;
                int failCount = 0;

                foreach (var device in targets)
                {
                    ShowProgress(
                        profile.profileName,
                        $"Installing to "
                        + $"{device.DisplayName} "
                        + $"[{successCount + 1}/"
                        + $"{targets.Count}]...",
                        (float)successCount
                        / targets.Count);

                    bool installed;
                    if (isAab)
                    {
                        installed =
                            AabUtility.InstallApks(
                                tempApksPath,
                                device.Serial);
                    }
                    else
                    {
                        installed =
                            AdbUtility.InstallApk(
                                installPath,
                                device.Serial);
                    }

                    if (!installed)
                    {
                        failCount++;
                        Debug.LogError(
                            $"[NoBuild] Install failed "
                            + "on "
                            + $"{device.DisplayName}");
                        continue;
                    }

                    bool launched =
                        AdbUtility.LaunchApp(
                            device.Serial,
                            packageName);
                    if (launched)
                    {
                        successCount++;
                        Debug.Log(
                            $"[NoBuild] Launched on "
                            + $"{device.DisplayName} "
                            + $"({device.Serial})");
                        EditorPrefs.SetString(
                            "NoBuild_LastAdbDevice",
                            device.Serial);
                    }
                    else
                    {
                        failCount++;
                        Debug.LogError(
                            $"[NoBuild] Launch failed "
                            + "on "
                            + $"{device.DisplayName}");
                    }
                }

                // 7 ── Summary ───────────────
                if (failCount > 0
                    && successCount == 0)
                {
                    EditorUtility.DisplayDialog(
                        "NoBuild",
                        "Failed to install/launch "
                        + "on all "
                        + $"{targets.Count} "
                        + "device(s).",
                        "OK");
                }
                else if (failCount > 0)
                {
                    EditorUtility.DisplayDialog(
                        "NoBuild",
                        $"{successCount} device(s) "
                        + "succeeded, "
                        + $"{failCount} failed.",
                        "OK");
                }
                // All succeeded — silent (log only)
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "NoBuild — Error",
                    "An error occurred during "
                    + $"Build & Run:\n{ex.Message}",
                    "OK");
                Debug.LogException(ex);
            }
            finally
            {
                // ── Cleanup is inevitable ────
                ShowProgress(
                    profile.profileName,
                    "Cleaning up temporary files...",
                    0f);

                if (isAab
                    && !string.IsNullOrEmpty(
                        tempApksPath))
                {
                    AabUtility.Cleanup(tempApksPath);
                }

                EditorUtility.ClearProgressBar();
            }
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
                scriptDefines = PlayerSettings.GetScriptingDefineSymbols(namedTarget),
                scriptingBackend = PlayerSettings.GetScriptingBackend(namedTarget),
                il2CppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(namedTarget),
                strippingLevel = PlayerSettings.GetManagedStrippingLevel(namedTarget),
                stripEngineCode = PlayerSettings.stripEngineCode,
                developmentBuild = EditorUserBuildSettings.development,
                allowDebugging = EditorUserBuildSettings.allowDebugging,
                connectProfiler = EditorUserBuildSettings.connectProfiler,
                bundleIdentifier = PlayerSettings.GetApplicationIdentifier(namedTarget),
                productName = PlayerSettings.productName,
#if UNITY_ANDROID
                androidArchitecture = PlayerSettings.Android.targetArchitectures,
                androidExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject,
                androidBuildAppBundle = EditorUserBuildSettings.buildAppBundle,
                androidSplitBinary = PlayerSettings.Android.splitApplicationBinary,
                debugSymbolLevel = UnityEditor.Android.UserBuildSettings.DebugSymbols.level,
                debugSymbolFormat = UnityEditor.Android.UserBuildSettings.DebugSymbols.format,
#endif
#if UNITY_IOS
                iosTeamId = PlayerSettings.iOS.appleDeveloperTeamID,
                iosAutomaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning,
#endif
            };
        }

        private static void ApplyBuildConfiguration(
            BuildConfiguration config,
            UnityEditor.Build.NamedBuildTarget namedTarget,
            BuildTargetGroup targetGroup
        )
        {
            PlayerSettings.SetScriptingBackend(namedTarget, config.scriptingBackend);
            PlayerSettings.SetIl2CppCodeGeneration(namedTarget, config.il2CppCodeGeneration);
            PlayerSettings.SetManagedStrippingLevel(namedTarget, config.strippingLevel);
            PlayerSettings.stripEngineCode = config.stripEngineCode;

            EditorUserBuildSettings.development = config.developmentBuild;
            EditorUserBuildSettings.allowDebugging = config.allowDebugging;
            EditorUserBuildSettings.connectProfiler = config.connectWithProfiler;

            if (!string.IsNullOrEmpty(config.bundleIdentifierOverride))
            {
                PlayerSettings.SetApplicationIdentifier(namedTarget, config.bundleIdentifierOverride);
            }

            if (!string.IsNullOrEmpty(config.productNameOverride))
            {
                PlayerSettings.productName = config.productNameOverride;
            }

#if UNITY_ANDROID
            PlayerSettings.Android.targetArchitectures = config.androidTargetArchitecture;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = config.androidExportProject;
            EditorUserBuildSettings.buildAppBundle = config.androidBuildAppBundle;
            PlayerSettings.Android.splitApplicationBinary = config.androidSplitBinary;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = config.debugSymbolLevel;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.format = config.debugSymbolFormat;
#endif

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE
            // Windows-specific
            // createVSProject is handled via BuildOptions
#endif

#if UNITY_IOS
            if (!string.IsNullOrEmpty(config.iosTeamId)) PlayerSettings.iOS.appleDeveloperTeamID = config.iosTeamId;
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
                UnityEditor.Android.UserBuildSettings.DebugSymbols.level = snapshot.debugSymbolLevel;
                UnityEditor.Android.UserBuildSettings.DebugSymbols.format = snapshot.debugSymbolFormat;
#endif
#if UNITY_IOS
                if (!string.IsNullOrEmpty(snapshot.iosTeamId)) 
                {
                    PlayerSettings.iOS.appleDeveloperTeamID = snapshot.iosTeamId;
                }

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
            return GetPlatformExtension(target, null);
        }

        /// <summary>
        /// Returns the platform file extension. When <paramref name="config"/>
        /// is provided, uses the profile's AAB flag instead of the (possibly
        /// restored) global EditorUserBuildSettings value.
        /// </summary>
        private static string GetPlatformExtension(
            BuildTarget target, BuildConfiguration config
        )
        {
            switch (target)
            {
                case BuildTarget.Android:
                    if (config != null)
                        return config.androidBuildAppBundle
                            ? ".aab" : ".apk";
                    return EditorUserBuildSettings.buildAppBundle
                        ? ".aab" : ".apk";
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

        /// <summary>
        /// Wraps <see cref="EditorUtility.DisplayProgressBar"/> with a
        /// standard NoBuild title prefix.
        /// </summary>
        private static void ShowProgress(
            string context, string info, float progress)
        {
            EditorUtility.DisplayProgressBar(
                "NoBuild",
                $"{context}: {info}",
                progress);
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
