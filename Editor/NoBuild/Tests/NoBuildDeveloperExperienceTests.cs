using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Com.Scheherazade.Common.NoBuild.Editor.Tests
{
    public sealed class NoBuildDeveloperExperienceTests
    {
        private NoBuildSettings _settings;
        private BuildProfile _profile;
        private string _previousResult;
        private string _previousOutput;
        private bool _previousFailure;

        [SetUp]
        public void SetUp()
        {
            _previousResult = NoBuildEditorState.LastResult;
            _previousOutput = NoBuildEditorState.LastOutput;
            _previousFailure = NoBuildEditorState.LastResultFailed;
            _settings = ScriptableObject.CreateInstance<NoBuildSettings>();
            _profile = new BuildProfile
            {
                buildNameTemplate = new BuildNameTemplate { template = "TestOutput" },
                buildFolder = new BuildNameTemplate { template = "Builds/NoBuildTests" }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
            NoBuildEditorState.SetResult(_previousResult, _previousOutput, _previousFailure);
        }

        [Test]
        public void MissingDefineReference_IsNotTreatedAsNone()
        {
            _profile.scriptDefinitionSetIndex = 0;
            BuildPreflightResult missing = BuildPreflight.Evaluate(_profile, _settings, false);
            Assert.That(missing.Errors, Has.Some.Contains("Define set reference is missing"));

            _profile.scriptDefinitionSetIndex = -1;
            BuildPreflightResult none = BuildPreflight.Evaluate(_profile, _settings, false);
            Assert.That(none.Errors.Any(error => error.Contains("Define set reference")), Is.False);
        }

        [Test]
        public void InvalidDefines_DoNotChangeSelectedSet()
        {
            _settings.scriptDefinitionSets.Add(new ScriptDefinitionSet
            {
                setName = "Invalid",
                slots = new List<ScriptDefinitionSlot> { new() { defineSymbol = "BAD-SYMBOL", enabled = true } }
            });
            LogAssert.Expect(LogType.Error, new Regex(@"\[NoBuild\] Invalid symbol in set 'Invalid'"));
            Assert.That(NoBuildEditorState.ApplyDefines(_settings, 0), Is.False);
            Assert.That(_settings.SelectedDefineSetIndex, Is.EqualTo(-1));
        }

        [Test]
        public void ConflictingSymbols_AreRejectedForApplyAndBuild()
        {
            var set = new ScriptDefinitionSet
            {
                slots = new List<ScriptDefinitionSlot>
                {
                    new() { defineSymbol = "CHEATS", enabled = true },
                    new() { defineSymbol = " CHEATS ", enabled = false }
                }
            };
            _settings.scriptDefinitionSets.Add(set);
            _profile.scriptDefinitionSetIndex = 0;
            Assert.That(ScriptDefinitionSwitcher.ValidateSet(set), Does.Contain("conflicting"));
            Assert.That(BuildPreflight.Evaluate(_profile, _settings, false).Errors, Has.Some.Contains("conflicting"));
        }

        [Test]
        public void DisabledCustomFlag_DoesNotFallBackToEnabledExternalSymbol()
        {
            var set = new ScriptDefinitionSet
            {
                slots = new List<ScriptDefinitionSlot> { new() { defineSymbol = "CHEATS", enabled = false } }
            };
            bool fallbackCalled = false;
            bool state = BuildNameResolver.ResolveCustomFlagState(set, "CHEATS", () =>
            {
                fallbackCalled = true;
                return true;
            });
            Assert.That(state, Is.False);
            Assert.That(fallbackCalled, Is.False);
        }

        [Test]
        public void CustomFlagOwnership_UsesCaseSensitiveTrimmedSymbols()
        {
            var set = new ScriptDefinitionSet
            {
                slots = new List<ScriptDefinitionSlot> { new() { defineSymbol = " CHEATS ", enabled = false } }
            };
            Assert.That(BuildNameResolver.ResolveCustomFlagState(set, "CHEATS", () => true), Is.False);
            Assert.That(BuildNameResolver.ResolveCustomFlagState(set, "cheats", () => true), Is.True);
        }

        [Test]
        public void DuplicateFlagId_IsUniqueAndUnknownTokensAreReported()
        {
            _settings.flagDefinitions.Add(new FlagDefinition { id = "cheats-copy" });
            _settings.flagDefinitions.Add(new FlagDefinition { id = "CHEATS-COPY-2" });
            Assert.That(NoBuildSettingsProvider.GetUniqueFlagId(_settings, "cheats-copy"), Is.EqualTo("cheats-copy-3"));
            Assert.That(BuildNameResolver.GetUnknownPlaceholders("{flag-cheats-copy}-{unknown}", _settings),
                Is.EqualTo(new[] { "{unknown}" }));
        }

        [Test]
        public void DuplicateFlagIds_BlockAmbiguousBuildNames()
        {
            _settings.flagDefinitions.Add(new FlagDefinition { id = "qa" });
            _settings.flagDefinitions.Add(new FlagDefinition { id = "QA" });
            _profile.buildNameTemplate.template = "{flag-qa}";
            Assert.That(BuildPreflight.Evaluate(_profile, _settings, false).Errors, Has.Some.Contains("Duplicate flag ID"));
        }

        [TestCase(BuildTarget.Android, false, false, ".apk")]
        [TestCase(BuildTarget.Android, true, false, ".aab")]
        [TestCase(BuildTarget.Android, false, true, "")]
        [TestCase(BuildTarget.StandaloneWindows64, false, false, ".exe")]
        [TestCase(BuildTarget.StandaloneOSX, false, false, ".app")]
        [TestCase(BuildTarget.WebGL, false, false, "")]
        public void OutputPreview_UsesExactArtifactExtension(BuildTarget target, bool aab, bool export, string extension)
        {
            _profile.buildConfiguration.platform = target;
            _profile.buildConfiguration.androidBuildAppBundle = aab;
            _profile.buildConfiguration.androidExportProject = export;
            string expected = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Builds/NoBuildTests", "TestOutput" + extension));
            Assert.That(BuildPreflight.GetOutputPath(_profile, _settings), Is.EqualTo(expected));
        }

        [Test]
        public void ResolvedFileName_CannotCreateAnAccidentalSubdirectory()
        {
            _profile.profileName = "feature/test:qa";
            Assert.That(BuildNameResolver.ResolveFileName("{profile-name}", _profile, _settings), Is.EqualTo("feature_test_qa"));
        }

        [Test]
        public void ValidBuildOverride_WorksWithoutEditorSceneEntries()
        {
            SceneAsset scene = GetSourceScene();
            _settings.sceneSets.Add(new SceneSet
            {
                buildOrderOverride = new List<SceneSlot> { new() { scene = scene } }
            });
            _profile.sceneSetIndex = 0;
            BuildPreflightResult result = BuildPreflight.Evaluate(_profile, _settings, false);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ScenePaths, Is.EqualTo(new[] { AssetDatabase.GetAssetPath(scene) }));
        }

        [Test]
        public void MissingEnabledAndDuplicateBuildScenes_AreReported()
        {
            SceneAsset scene = GetSourceScene();
            var set = new SceneSet
            {
                scenes = new List<SceneSlot> { new() { scene = scene }, new() { scene = scene }, new() }
            };
            _settings.sceneSets.Add(set);
            _profile.sceneSetIndex = 0;
            BuildPreflightResult result = BuildPreflight.Evaluate(_profile, _settings, false);
            Assert.That(result.Errors, Has.Some.Contains("missing scene"));
            Assert.That(result.Errors, Has.Some.Contains("duplicate scenes"));
            Assert.That(NoBuildEditorState.ScenesMatch(set), Is.False);
        }

        [Test]
        public void AndroidExportAndAabConflict_IsReportedBeforeBuilding()
        {
            _profile.buildConfiguration.androidExportProject = true;
            _profile.buildConfiguration.androidBuildAppBundle = true;
            Assert.That(BuildPreflight.Evaluate(_profile, _settings, false).Errors, Has.Some.Contains("cannot both"));
        }

        [Test]
        public void ContextSelection_DoesNotModifySharedAssetFields()
        {
            string path = "Assets/__NoBuildContextTest_" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                AssetDatabase.CreateAsset(_settings, path);
                _settings.SelectedSceneSetIndex = 3;
                _settings.SelectedDefineSetIndex = 2;
                Assert.That(_settings.SelectedSceneSetIndex, Is.EqualTo(3));
                Assert.That(_settings.SelectedDefineSetIndex, Is.EqualTo(2));
                Assert.That(_settings.activeSceneSetIndex, Is.EqualTo(-1));
                Assert.That(_settings.activeScriptDefinitionSetIndex, Is.EqualTo(-1));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void ContextRemapping_PreservesReferencesAroundInsertionAndRemoval()
        {
            _settings.activeSceneSetIndex = 3;
            NoBuildEditorState.RemapContexts(_settings, 1, true, false);
            Assert.That(_settings.SelectedSceneSetIndex, Is.EqualTo(4));
            NoBuildEditorState.RemapContexts(_settings, 1, false, false);
            Assert.That(_settings.SelectedSceneSetIndex, Is.EqualTo(3));
            NoBuildEditorState.RemapContexts(_settings, 3, false, false);
            Assert.That(_settings.SelectedSceneSetIndex, Is.EqualTo(-1));
        }

        [Test]
        public void DeviceParser_PreservesUnauthorizedAndOfflineStatesWithoutExtraProcesses()
        {
            const string output = "List of devices attached\nABC device product:test model:Pixel_8 device:test\nDEF unauthorized\nGHI offline\n";
            List<AdbDeviceInfo> devices = AdbUtility.ParseDevices(output);
            Assert.That(devices.Select(device => device.State), Is.EqualTo(new[] { "device", "unauthorized", "offline" }));
            Assert.That(devices[0].Model, Is.EqualTo("Pixel 8"));
            Assert.That(devices[1].Serial, Is.EqualTo("DEF"));
        }

        [Test]
        public void DisabledCombination_KeepsFollowingShortcutSlotsStable()
        {
            SceneAsset scene = GetSourceScene();
            var first = new SceneCombination { enabled = false };
            var second = new SceneCombination
            {
                sceneReferences = new List<SceneReference> { new() { scene = scene, enabled = true } }
            };
            var set = new SceneSet
            {
                scenes = new List<SceneSlot> { new() { scene = scene } },
                combinations = new List<SceneCombination> { first, second }
            };
            Assert.That(ShortcutManager.GetCombinationForSlot(set, 0), Is.Null);
            Assert.That(ShortcutManager.GetCombinationForSlot(set, 1), Is.SameAs(second));
            Assert.That(ShortcutManager.GetCombinationForSlot(set, 9), Is.Null);
        }

        [Test]
        public void WirelessState_RecognizesAdbServiceSerialsAndRejectsOfflineDevices()
        {
            var wireless = new WirelessDeviceInfo { Serial = "ABC", IpAddress = "192.168.1.2", Port = 1234 };
            var connected = new AdbDeviceInfo { Serial = "adb-ABC-nonce._adb-tls-connect._tcp", State = "device" };
            Assert.That(AdbUtility.IsWirelessConnected(wireless, new[] { connected }), Is.True);
            connected.State = "offline";
            Assert.That(AdbUtility.IsWirelessConnected(wireless, new[] { connected }), Is.False);
        }

        [TestCase((int)InstallFailureKind.UpdateIncompatible, true)]
        [TestCase((int)InstallFailureKind.VersionDowngrade, true)]
        [TestCase((int)InstallFailureKind.AlreadyExists, true)]
        [TestCase((int)InstallFailureKind.UserCancelled, false)]
        [TestCase((int)InstallFailureKind.Other, false)]
        [TestCase((int)InstallFailureKind.None, false)]
        public void UninstallRecovery_IsRestrictedToRelevantFailures(int failure, bool expected)
        {
            Assert.That(DeviceInstaller.CanUninstall((InstallFailureKind)failure, "com.company.game"), Is.EqualTo(expected));
            Assert.That(DeviceInstaller.CanUninstall((InstallFailureKind)failure, ""), Is.False);
        }

        [Test]
        public void InstallResult_DoesNotMistakeZeroExitFailureOutputForSuccess()
        {
            InstallResult result = AdbUtility.ToInstallResult(new ProcessExecutionResult(0, "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]", "", false));
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(InstallFailureKind.UpdateIncompatible));
        }

#if UNITY_EDITOR_WIN
        [Test]
        public async Task ProcessCancellation_StopsTheOwnedExternalProcess()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var cancellation = new CancellationTokenSource(100);
            try
            {
                await NoBuildProcessRunner.RunAsync("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 10",
                    15000, cancellationToken: cancellation.Token);
                Assert.Fail("The process should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Cancellation must not wait for the ten-second process to finish naturally.");
            }
        }
#endif

        private static SceneAsset GetSourceScene()
        {
            string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            if (scenes.Length == 0) Assert.Ignore("A source scene is required for scene-list validation.");
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(scenes[0]));
        }
    }
}
