using System;
using System.Reflection;
using Com.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.PuzzleLevels.Tests
{
    public sealed class PuzzleLevelBuildGuardTests
    {
        private string _folder;
        private IPreprocessBuildWithReport _validator;

        [SetUp]
        public void SetUp()
        {
            Type type = typeof(Editor.PuzzleLevelSettingsProvider).Assembly.GetType(
                "Com.Scheherazade.Common.Frameworks.PuzzleLevels.Editor.PuzzleLevelProjectValidator", true);
            bool used = (bool)type.GetMethod("HasPuzzleLevelAssets", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assume.That(used, Is.False, "These asset-database fixtures require a project without authored Puzzle Levels assets.");
            _validator = (IPreprocessBuildWithReport)Activator.CreateInstance(type, true);
            string name = "__PuzzleBuildGuard_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
            _folder = null;
        }

        [Test]
        public void InstalledButUnusedFrameworkDoesNotBlockOrLogDuringBuild()
        {
            Assert.DoesNotThrow(() => _validator.OnPreprocessBuild(null));
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [TestCase("manager")]
        [TestCase("table")]
        [TestCase("provider")]
        [TestCase("overrides")]
        public void PartialOrInvalidConfigurationStillBlocksBuild(string kind)
        {
            ScriptableObject asset = kind == "manager" ? ScriptableObject.CreateInstance<PuzzleLevelManager>()
                : kind == "table" ? ScriptableObject.CreateInstance<PuzzleLevelReferenceTable>()
                : kind == "provider" ? ScriptableObject.CreateInstance<PuzzleLevelReferenceTableProvider>()
                : ScriptableObject.CreateInstance<PuzzleLevelOverrideConfig>();
            AssetDatabase.CreateAsset(asset, _folder + "/" + kind + ".asset");
            var exception = Assert.Throws<BuildFailedException>(() => _validator.OnPreprocessBuild(null));
            Assert.That(exception.Message, Does.Contain("Puzzle level configuration validation failed"));
            Assert.That(exception.Message, Does.Contain(kind == "manager" ? "Canonical manager is missing" : "No PuzzleLevelManager"));
        }
    }
}
