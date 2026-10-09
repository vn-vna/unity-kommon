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
            BuildPreflightResult preflight = BuildPreflight.Evaluate(profile, settings);
            string validationError = preflight.Errors.FirstOrDefault()
                ?? (preflight.IsRefreshingGit ? "Wait for Git metadata to refresh, then start the build again." : null);
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

            string[] blockedDefines = preflight.DefinesToRemove;
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
                    if (NoBuildEditorState.ApplyDefines(settings,
                            profile.scriptDefinitionSetIndex, targetGroup))
                    {
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
                    NoBuildEditorState.SetResult("Build cancelled while saving scenes.");
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
                string[] scenePaths = preflight.ScenePaths;
                if (scenePaths.Length == 0)
                {
                    throw new InvalidOperationException(
                        "No valid scenes to build.");
                }

                string resolvedOutputPath = outputPath ?? preflight.OutputPath;

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
                else if (summary.result == BuildResult.Cancelled)
                {
                    NoBuildEditorState.SetResult("Build cancelled: " + profile.profileName);
                    return null;
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

            NoBuildDeviceOperations.Start("Detecting build target devices…", async token =>
            {
                List<AdbDeviceInfo> devices = (await AdbUtility.GetDevicesAsync(token))
                    .Where(device => device.State == "device").ToList();
                List<AdbDeviceInfo> targets = deviceOption switch
                {
                    DeviceOption.AllDevices => devices,
                    DeviceOption.FirstDevice => devices.Take(1).ToList(),
                    _ => devices.Where(device => device.Serial == specificSerial).ToList()
                };
                if (targets.Count == 0) throw new InvalidOperationException("The selected Android device is no longer connected or authorized.");
                token.ThrowIfCancellationRequested();
                bool aab = profile.buildConfiguration.androidBuildAppBundle;
                string package = ResolveAndroidPackageName(profile);
                NoBuildDeviceOperations.SetStatus("Building " + profile.profileName + "…");
                BuildReport report = Build(profile);
                if (report == null) return;
                string path = report.summary.outputPath;
                if (!File.Exists(path)) throw new FileNotFoundException("Build artifact not found: " + path);
                string apks = null;
                try
                {
                    if (aab)
                    {
                        NoBuildDeviceOperations.SetStatus("Converting AAB to APKS…");
                        apks = await AabUtility.BuildApksAsync(path, token);
                    }
                    var outcomes = new List<string>();
                    int success = 0;
                    foreach (AdbDeviceInfo device in targets)
                    {
                        token.ThrowIfCancellationRequested();
                        NoBuildDeviceOperations.SetStatus("Installing to " + device.DisplayName + "…");
                        bool installed = await DeviceInstaller.InstallWithRecoveryAsync(device.Serial, device.DisplayName, package,
                            () => aab ? AabUtility.InstallApksAsync(apks, device.Serial, token)
                                : AdbUtility.InstallApkAsync(path, device.Serial, token), token);
                        if (!installed)
                        {
                            outcomes.Add(device.DisplayName + ": install cancelled");
                            continue;
                        }
                        ProcessExecutionResult launch = await AdbUtility.RunAsync(
                            $"-s \"{device.Serial}\" shell monkey -p \"{package}\" -c android.intent.category.LAUNCHER 1", cancellationToken: token);
                        bool launched = !launch.TimedOut && launch.ExitCode == 0 && !launch.CombinedOutput.Contains("Error");
                        outcomes.Add(device.DisplayName + (launched ? ": installed and launched" : ": installed; launch failed"));
                        if (launched)
                        {
                            success++;
                            EditorPrefs.SetString(NoBuildDeviceOperations.RememberedDeviceKey, device.Serial);
                        }
                    }
                    NoBuildEditorState.SetResult($"Deployment: {success}/{targets.Count} succeeded · {profile.profileName}\n"
                        + string.Join("\n", outcomes), path, success != targets.Count);
                }
                finally
                {
                    if (apks != null) AabUtility.Cleanup(apks);
                }
            });
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

            if (!string.IsNullOrWhiteSpace(config.bundleIdentifierOverride))
            {
                PlayerSettings.SetApplicationIdentifier(namedTarget, config.bundleIdentifierOverride.Trim());
            }

            if (!string.IsNullOrWhiteSpace(config.productNameOverride))
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
