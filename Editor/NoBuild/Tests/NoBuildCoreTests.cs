using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace Com.Scheherazade.Common.NoBuild.Editor.Tests
{
    public sealed class NoBuildCoreTests
    {
        private string _bundleVersion;
        private int _androidBundleVersionCode;
        private string _iosBuildNumber;

        [SetUp]
        public void SetUp()
        {
            _bundleVersion = PlayerSettings.bundleVersion;
            _androidBundleVersionCode =
                PlayerSettings.Android.bundleVersionCode;
            _iosBuildNumber = PlayerSettings.iOS.buildNumber;
        }

        [TearDown]
        public void TearDown()
        {
            PlayerSettings.bundleVersion = _bundleVersion;
            PlayerSettings.Android.bundleVersionCode =
                _androidBundleVersionCode;
            PlayerSettings.iOS.buildNumber = _iosBuildNumber;
        }

        [Test]
        public void ResolveAppBundle_UsesProfileBuildTarget()
        {
            PlayerSettings.bundleVersion = "desktop";
            PlayerSettings.Android.bundleVersionCode = 1234;
            PlayerSettings.iOS.buildNumber = "ios";

            var androidProfile = new BuildProfile
            {
                buildConfiguration = new BuildConfiguration
                {
                    platform = BuildTarget.Android
                }
            };
            var iosProfile = new BuildProfile
            {
                buildConfiguration = new BuildConfiguration
                {
                    platform = BuildTarget.iOS
                }
            };

            Assert.That(
                BuildNameResolver.Resolve(
                    "{app-bundle}",
                    androidProfile,
                    null),
                Is.EqualTo("1234"));
            Assert.That(
                BuildNameResolver.Resolve(
                    "{app-bundle}",
                    iosProfile,
                    null),
                Is.EqualTo("ios"));
        }

        [Test]
        public void ToolbarLabel_ResolvesActiveNamesAndCounts()
        {
            var settings = UnityEngine.ScriptableObject
                .CreateInstance<NoBuildSettings>();
            try
            {
                settings.toolbarLabelTemplate =
                    "{scene-set} | {define-set} ({define-count})";
                settings.sceneSets.Add(new SceneSet
                {
                    setName = "Gameplay"
                });
                settings.scriptDefinitionSets.Add(
                    new ScriptDefinitionSet
                    {
                        setName = "Development",
                        slots = new List<ScriptDefinitionSlot>
                        {
                            new()
                            {
                                defineSymbol = "CHEATS",
                                enabled = true
                            },
                            new()
                            {
                                defineSymbol = "PRODUCTION_BUILD",
                                enabled = false
                            }
                        }
                    });
                settings.activeSceneSetIndex = 0;
                settings.activeScriptDefinitionSetIndex = 0;

                Assert.That(
                    NoBuildToolbarLabelResolver.Resolve(settings),
                    Is.EqualTo(
                        "Gameplay | Development (1)"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SceneCombination_StableReferenceSurvivesReorder()
        {
            const string folder =
                "Assets/__NoBuildSceneReferenceTests";
            try
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.DeleteAsset(folder);
                }

                string[] sceneGuids = AssetDatabase.FindAssets(
                    "t:Scene",
                    new[] { "Assets" });
                if (sceneGuids.Length == 0)
                {
                    Assert.Ignore(
                        "A source scene is required for this test.");
                }

                AssetDatabase.CreateFolder(
                    "Assets",
                    "__NoBuildSceneReferenceTests");
                string sourcePath = AssetDatabase.GUIDToAssetPath(
                    sceneGuids[0]);
                string firstPath = folder + "/First.unity";
                string secondPath = folder + "/Second.unity";
                Assert.That(
                    AssetDatabase.CopyAsset(sourcePath, firstPath),
                    Is.True);
                Assert.That(
                    AssetDatabase.CopyAsset(sourcePath, secondPath),
                    Is.True);
                SceneAsset first = AssetDatabase
                    .LoadAssetAtPath<SceneAsset>(firstPath);
                SceneAsset second = AssetDatabase
                    .LoadAssetAtPath<SceneAsset>(secondPath);

                var firstSlot = new SceneSlot { scene = first };
                var secondSlot = new SceneSlot { scene = second };
                var set = new SceneSet
                {
                    scenes = new List<SceneSlot>
                    {
                        firstSlot,
                        secondSlot
                    }
                };
                var combination = new SceneCombination
                {
                    sceneReferences = new List<SceneReference>
                    {
                        new()
                        {
                            enabled = true,
                            scene = second,
                            sceneIndex = 1
                        }
                    }
                };
                set.combinations.Add(combination);

                set.scenes.Reverse();

                Assert.That(
                    combination.ResolveScenes(set),
                    Is.EqualTo(new[] { secondSlot }));

                set.scenes.Remove(secondSlot);
                Assert.That(
                    set.GetEnabledCombinations(),
                    Is.Empty);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SceneReference_LegacyIndexRemainsValidForMigration()
        {
            var reference = new SceneReference
            {
                enabled = true,
                sceneIndex = 1
            };

            Assert.That(reference.IsValid, Is.True);
        }

#if UNITY_EDITOR_WIN
        [Test]
        public void ProcessRunner_DrainsStandardOutputAndError()
        {
            ProcessExecutionResult result =
                NoBuildProcessRunner.Run(
                    "cmd.exe",
                    "/c \"echo stdout & echo stderr 1>&2\"",
                    5000);

            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(
                result.StandardOutput,
                Does.Contain("stdout"));
            Assert.That(
                result.StandardError,
                Does.Contain("stderr"));
        }
        [Test]
        public void ProcessRunner_KillsTimedOutProcess()
        {
            ProcessExecutionResult result =
                NoBuildProcessRunner.Run(
                    "cmd.exe",
                    "/c ping 127.0.0.1 -n 5 > nul",
                    50);

            Assert.That(result.TimedOut, Is.True);
        }
#endif

        [TestCase("VALID_SYMBOL", null)]
        [TestCase("9INVALID", "must start")]
        [TestCase("INVALID-SYMBOL", "invalid characters")]
        public void ValidateSymbol_ReturnsExpectedResult(
            string symbol,
            string expectedMessagePart)
        {
            string result = ScriptDefinitionSwitcher
                .ValidateSymbol(symbol);

            if (expectedMessagePart == null)
            {
                Assert.That(result, Is.Null);
            }
            else
            {
                Assert.That(
                    result,
                    Does.Contain(expectedMessagePart));
            }
        }
    }
}
