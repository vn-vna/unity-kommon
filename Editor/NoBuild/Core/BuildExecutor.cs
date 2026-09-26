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

namespace Com.Scheherazade.Common.NoBuild.Editor
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

            string[] blockedDefines =
                GetBlockedDisabledDefines(
                    profile,
                    settings,
                    namedTarget);
            if (blockedDefines.Length > 0)
            {
                bool applyNow = EditorUtility.DisplayDialog(
                    "NoBuild — Defines Need Compilation",
                    $"Profile '{profile.profileName}' disables "
                    + "defines that are currently active:\n\n"
                    + string.Join(", ", blockedDefines)
                    + "\n\nApply its define set now? Unity "
                    + "must finish compiling before you start "
                    + "the build again.",
                    "Apply Defines",
                    "Cancel");
                if (applyNow)
                {
                    ScriptDefinitionSet defineSet =
                        settings.scriptDefinitionSets[
                            profile.scriptDefinitionSetIndex];
                    if (ScriptDefinitionSwitcher.ApplySet(
                            defineSet,
                            targetGroup))
                    {
                        settings.activeScriptDefinitionSetIndex =
                            profile.scriptDefinitionSetIndex;
                        EditorUtility.SetDirty(settings);
                        EditorUtility.DisplayDialog(
                            "NoBuild — Defines Applied",
                            "Wait for Unity to finish compiling, "
                            + "then start the build again.",
                            "OK");
                    }
                }

                return null;
            }

            // ── Snapshot ──────────────────────
            BuildStateSnapshot snapshot =
                CaptureBuildState(namedTarget,
                    targetGroup);
            BuildReport successfulReport = null;
            bool restoreSucceeded = true;

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
                if (!EditorSceneManager
                        .SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.Log(
                        "[NoBuild] Build cancelled while "
                        + "saving modified scenes.");
                    return null;
                }

                // ── Phase 2: Prepare build defines ─
                // Profile-enabled symbols are passed directly to
                // BuildPlayer, avoiding a domain reload and keeping
                // the editor's active define set unchanged.
                string[] buildDefines = GetBuildExtraDefines(
                    profile,
                    settings);

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
                            buildTarget,
                            config));

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
                        options = BuildOptions.None,
                        extraScriptingDefines = buildDefines
                    };

                if (config.developmentBuild)
                {
                    buildOptions.options |=
                        BuildOptions.Development;
                }

                if (config.developmentBuild
                    && config.allowDebugging)
                {
                    buildOptions.options |=
                        BuildOptions.AllowDebugging;
                }

                if (config.developmentBuild
                    && config.connectWithProfiler)
                {
                    buildOptions.options |=
                        BuildOptions.ConnectWithProfiler;
                }

                if (buildTarget == BuildTarget.StandaloneWindows
                    || buildTarget
                    == BuildTarget.StandaloneWindows64)
                {
                    if (config.windowsCreateVSProject)
                    {
                        buildOptions.options |= BuildOptions
                            .AcceptExternalModificationsToPlayer;
                    }
                }

                if (buildTarget == BuildTarget.iOS)
                {
                    if (config.iosSymlinkFramework)
                    {
                        buildOptions.options |=
                            BuildOptions.SymlinkSources;
                    }

                    if (config.iosRunInXcode)
                    {
                        buildOptions.options |=
                            BuildOptions.AutoRunPlayer;
                    }
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
                    successfulReport = report;
                }
                else
                {
                    string errorMsg =
                        $"Build failed with "
                        + $"{summary.totalErrors} error(s). "
                        + $"Result: {summary.result}";
                    Debug.LogError(
                        $"[NoBuild] {errorMsg}");
                    NotifyBuildFailed(profile, errorMsg);
                    EditorUtility.DisplayDialog(
                        "NoBuild — Build Failed",
                        errorMsg, "OK");
                    return null;
                }
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

                restoreSucceeded = RestoreBuildState(
                    snapshot,
                    namedTarget);

                EditorUtility.ClearProgressBar();
            }

            if (!restoreSucceeded)
            {
                NotifyBuildFailed(
                    profile,
                    "Build succeeded, but restoring editor "
                    + "settings failed. Installation was cancelled.");
                return null;
            }

            try
            {
                BuildCompleted?.Invoke(
                    profile,
                    successfulReport);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[NoBuild] A BuildCompleted listener "
                    + "threw an exception.");
                Debug.LogException(exception);
            }

            return successfulReport;
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
            if (profile == null
                || profile.buildConfiguration == null
                || profile.buildConfiguration.platform
                != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog(
                    "NoBuild",
                    "Build & Run currently only supports "
                    + "Android profiles.",
                    "OK");
                return;
            }

            if (profile.buildConfiguration.androidExportProject)
            {
                EditorUtility.DisplayDialog(
                    "NoBuild",
                    "Build & Run cannot install an exported "
                    + "Android Gradle project. Disable "
                    + "'Export Project' for this profile.",
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
                List<AdbDeviceInfo> devices = AdbUtility
                    .GetDevices()
                    .Where(device => device.State == "device")
                    .ToList();
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

                // 4 ── Use the exact path produced by Unity ──
                string outputPath = report.summary.outputPath;
                if (string.IsNullOrEmpty(outputPath)
                    || !File.Exists(outputPath))
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
                    ResolveAndroidPackageName(profile);
                int successCount = 0;
                int failCount = 0;

                for (int targetIndex = 0;
                    targetIndex < targets.Count;
                    targetIndex++)
                {
                    AdbDeviceInfo device = targets[targetIndex];
                    ShowProgress(
                        profile.profileName,
                        $"Installing to "
                        + $"{device.DisplayName} "
                        + $"[{targetIndex + 1}/"
                        + $"{targets.Count}]...",
                        (float)targetIndex
                        / targets.Count);

                    bool installed =
                        DeviceInstaller.InstallWithRecovery(
                            device.Serial,
                            device.DisplayName,
                            packageName,
                            () => isAab
                                ? AabUtility.InstallApks(
                                    tempApksPath,
                                    device.Serial)
                                : AdbUtility.InstallApk(
                                    installPath,
                                    device.Serial));

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

        private static string ResolveAndroidPackageName(
            BuildProfile profile)
        {
            string profileOverride = profile?.buildConfiguration
                ?.bundleIdentifierOverride;
            return !string.IsNullOrWhiteSpace(profileOverride)
                ? profileOverride.Trim()
                : AdbUtility.GetPackageName();
        }

        private static string[] GetBuildExtraDefines(
            BuildProfile profile,
            NoBuildSettings settings)
        {
            if (!profile.HasValidDefineSet(settings))
            {
                return Array.Empty<string>();
            }

            ScriptDefinitionSet set =
                settings.scriptDefinitionSets[
                    profile.scriptDefinitionSetIndex];
            return set.slots
                .Where(slot => slot != null
                    && slot.enabled
                    && !string.IsNullOrWhiteSpace(
                        slot.defineSymbol))
                .Select(slot => slot.defineSymbol.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetBlockedDisabledDefines(
            BuildProfile profile,
            NoBuildSettings settings,
            UnityEditor.Build.NamedBuildTarget namedTarget)
        {
            if (!profile.HasValidDefineSet(settings))
            {
                return Array.Empty<string>();
            }

            HashSet<string> currentDefines =
                ParseDefines(PlayerSettings
                    .GetScriptingDefineSymbols(namedTarget));
            ScriptDefinitionSet set =
                settings.scriptDefinitionSets[
                    profile.scriptDefinitionSetIndex];
            return set.slots
                .Where(slot => slot != null
                    && !slot.enabled
                    && !string.IsNullOrWhiteSpace(
                        slot.defineSymbol))
                .Select(slot => slot.defineSymbol.Trim())
                .Where(currentDefines.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string ValidateBuildDefines(
            BuildProfile profile,
            NoBuildSettings settings)
        {
            if (!profile.HasValidDefineSet(settings))
            {
                return null;
            }

            ScriptDefinitionSet set =
                settings.scriptDefinitionSets[
                    profile.scriptDefinitionSetIndex];
            if (set?.slots == null || set.slots.Count == 0)
            {
                return $"Define set '{set?.setName ?? "(missing)"}' "
                    + "has no symbols.";
            }

            var desiredStates =
                new Dictionary<string, bool>(
                    StringComparer.Ordinal);
            foreach (ScriptDefinitionSlot slot in set.slots)
            {
                string error = ScriptDefinitionSwitcher
                    .ValidateSymbol(slot?.defineSymbol);
                if (error != null)
                {
                    return $"Invalid symbol in define set "
                        + $"'{set.setName}': {error}";
                }

                string symbol = slot.defineSymbol.Trim();
                if (desiredStates.TryGetValue(
                        symbol,
                        out bool enabled)
                    && enabled != slot.enabled)
                {
                    return $"Define set '{set.setName}' contains "
                        + $"conflicting entries for '{symbol}'.";
                }

                desiredStates[symbol] = slot.enabled;
            }

            return null;
        }

        private static HashSet<string> ParseDefines(
            string defineString)
        {
            return new HashSet<string>(
                (defineString ?? string.Empty)
                    .Split(';')
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrEmpty(value)),
                StringComparer.Ordinal);
        }

        private static string ValidateProfile(BuildProfile profile, NoBuildSettings settings)
        {
            if (profile.buildConfiguration == null)
            {
                return $"Build profile '{profile.profileName}' "
                    + "has no build configuration.";
            }

            BuildConfiguration config =
                profile.buildConfiguration;
            if (config.platform == BuildTarget.NoTarget)
            {
                return $"Build profile '{profile.profileName}' "
                    + "has no target platform.";
            }

            if (config.platform == BuildTarget.Android
                && config.androidExportProject
                && config.androidBuildAppBundle)
            {
                return "Android profiles cannot export a Gradle "
                    + "project and build an App Bundle at the "
                    + "same time.";
            }

            string defineError = ValidateBuildDefines(
                profile,
                settings);
            if (defineError != null)
            {
                return defineError;
            }

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
                if (slot != null && slot.enabled && slot.IsValid)
                {
                    validCount++;
                }
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

            bool hasOverride = sceneSet.buildOrderOverride
                != null
                && sceneSet.buildOrderOverride.Count > 0;
            List<SceneSlot> orderedSlots = hasOverride
                ? sceneSet.buildOrderOverride
                : sceneSet.scenes;

            List<string> paths = GetValidScenePaths(
                orderedSlots);
            if (paths.Count == 0 && hasOverride)
            {
                Debug.LogWarning(
                    "[NoBuild] Build order override has no valid "
                    + "scenes. Using the scene set's natural order.");
                paths = GetValidScenePaths(sceneSet.scenes);
            }

            return paths.ToArray();
        }

        private static List<string> GetValidScenePaths(
            IEnumerable<SceneSlot> slots)
        {
            var paths = new List<string>();
            if (slots == null) return paths;

            foreach (SceneSlot slot in slots)
            {
                if (slot != null && slot.enabled && slot.IsValid)
                {
                    paths.Add(slot.ScenePath);
                }
            }

            return paths;
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
                androidArchitecture =
                    PlayerSettings.Android.targetArchitectures,
                androidExportProject = EditorUserBuildSettings
                    .exportAsGoogleAndroidProject,
                androidBuildAppBundle =
                    EditorUserBuildSettings.buildAppBundle,
                androidSplitBinary = PlayerSettings.Android
                    .splitApplicationBinary,
#if UNITY_ANDROID
                debugSymbolLevel = UnityEditor.Android
                    .UserBuildSettings.DebugSymbols.level,
                debugSymbolFormat = UnityEditor.Android
                    .UserBuildSettings.DebugSymbols.format,
#endif
                iosTeamId = PlayerSettings.iOS
                    .appleDeveloperTeamID,
                iosAutomaticSigning = PlayerSettings.iOS
                    .appleEnableAutomaticSigning,
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

            EditorUserBuildSettings.development =
                config.developmentBuild;
            EditorUserBuildSettings.allowDebugging =
                config.developmentBuild
                && config.allowDebugging;
            EditorUserBuildSettings.connectProfiler =
                config.developmentBuild
                && config.connectWithProfiler;

            if (!string.IsNullOrEmpty(config.bundleIdentifierOverride))
            {
                PlayerSettings.SetApplicationIdentifier(namedTarget, config.bundleIdentifierOverride);
            }

            if (!string.IsNullOrEmpty(config.productNameOverride))
            {
                PlayerSettings.productName = config.productNameOverride;
            }

            if (config.platform == BuildTarget.Android)
            {
                PlayerSettings.Android.targetArchitectures =
                    config.androidTargetArchitecture;
                EditorUserBuildSettings
                    .exportAsGoogleAndroidProject =
                    config.androidExportProject;
                EditorUserBuildSettings.buildAppBundle =
                    config.androidBuildAppBundle;
                PlayerSettings.Android.splitApplicationBinary =
                    config.androidSplitBinary;
#if UNITY_ANDROID
                UnityEditor.Android.UserBuildSettings.DebugSymbols
                    .level = config.debugSymbolLevel;
                UnityEditor.Android.UserBuildSettings.DebugSymbols
                    .format = config.debugSymbolFormat;
#endif
            }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE
            // Windows-specific
            // createVSProject is handled via BuildOptions
#endif

            if (config.platform == BuildTarget.iOS)
            {
                PlayerSettings.iOS.appleDeveloperTeamID =
                    config.iosTeamId ?? string.Empty;
                PlayerSettings.iOS.appleEnableAutomaticSigning =
                    config.iosAutomaticSigning;
            }
        }

        private static bool RestoreBuildState(
            BuildStateSnapshot snapshot,
            UnityEditor.Build.NamedBuildTarget namedTarget)
        {
            if (snapshot == null) return true;

            var failures = new List<string>();
            void TryRestore(string setting, Action restore)
            {
                try
                {
                    restore();
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{setting}: {exception.Message}");
                    Debug.LogException(exception);
                }
            }

            TryRestore("Scripting defines", () =>
            {
                string originalDefines =
                    snapshot.scriptDefines ?? string.Empty;
                string currentDefines = PlayerSettings
                    .GetScriptingDefineSymbols(namedTarget);
                if (!string.Equals(
                        currentDefines,
                        originalDefines,
                        StringComparison.Ordinal))
                {
                    PlayerSettings.SetScriptingDefineSymbols(
                        namedTarget,
                        originalDefines);
                }
            });
            TryRestore(
                "Scripting backend",
                () => PlayerSettings.SetScriptingBackend(
                    namedTarget,
                    snapshot.scriptingBackend));
            TryRestore(
                "IL2CPP code generation",
                () => PlayerSettings.SetIl2CppCodeGeneration(
                    namedTarget,
                    snapshot.il2CppCodeGeneration));
            TryRestore(
                "Managed stripping level",
                () => PlayerSettings.SetManagedStrippingLevel(
                    namedTarget,
                    snapshot.strippingLevel));
            TryRestore(
                "Build flags",
                () =>
                {
                    PlayerSettings.stripEngineCode =
                        snapshot.stripEngineCode;
                    EditorUserBuildSettings.development =
                        snapshot.developmentBuild;
                    EditorUserBuildSettings.allowDebugging =
                        snapshot.allowDebugging;
                    EditorUserBuildSettings.connectProfiler =
                        snapshot.connectProfiler;
                });
            TryRestore(
                "Application identifier",
                () => PlayerSettings.SetApplicationIdentifier(
                    namedTarget,
                    snapshot.bundleIdentifier ?? string.Empty));
            TryRestore(
                "Product name",
                () => PlayerSettings.productName =
                    snapshot.productName ?? string.Empty);
            TryRestore(
                "Android settings",
                () =>
                {
                    PlayerSettings.Android.targetArchitectures =
                        snapshot.androidArchitecture;
                    EditorUserBuildSettings
                        .exportAsGoogleAndroidProject =
                        snapshot.androidExportProject;
                    EditorUserBuildSettings.buildAppBundle =
                        snapshot.androidBuildAppBundle;
                    PlayerSettings.Android.splitApplicationBinary =
                        snapshot.androidSplitBinary;
                });
#if UNITY_ANDROID
            TryRestore(
                "Android debug symbols",
                () =>
                {
                    UnityEditor.Android.UserBuildSettings.DebugSymbols
                        .level = snapshot.debugSymbolLevel;
                    UnityEditor.Android.UserBuildSettings.DebugSymbols
                        .format = snapshot.debugSymbolFormat;
                });
#endif
            TryRestore(
                "iOS signing",
                () =>
                {
                    PlayerSettings.iOS.appleDeveloperTeamID =
                        snapshot.iosTeamId ?? string.Empty;
                    PlayerSettings.iOS.appleEnableAutomaticSigning =
                        snapshot.iosAutomaticSigning;
                });

            if (failures.Count == 0)
            {
                return true;
            }

            string message =
                "Failed to restore some editor build settings "
                + "after the build. Installation was cancelled. "
                + "Review Player Settings before starting another "
                + "build.\n\n"
                + string.Join("\n", failures);
            Debug.LogError($"[NoBuild] {message}");
            EditorUtility.DisplayDialog(
                "NoBuild — Restore Failed",
                message,
                "OK");
            return false;
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
                    {
                        if (config.androidExportProject)
                        {
                            return string.Empty;
                        }

                        return config.androidBuildAppBundle
                            ? ".aab" : ".apk";
                    }
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

        private static void FireFailed(
            BuildProfile profile,
            string message)
        {
            Debug.LogError($"[NoBuild] {message}");
            NotifyBuildFailed(profile, message);
        }

        private static void NotifyBuildFailed(
            BuildProfile profile,
            string message)
        {
            try
            {
                BuildFailed?.Invoke(profile, message);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[NoBuild] A BuildFailed listener "
                    + "threw an exception.");
                Debug.LogException(exception);
            }
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
