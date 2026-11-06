using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Com.Scheherazade.Common.VIC.Editor.Tests
{
    public sealed class VersionInfoBuildProcessorTests
    {
        private const string ExistingAssetPath =
            "Assets/Resources/__VersionInfoExistingTest.txt";
        private const string GeneratedAssetPath =
            "Assets/Resources/__VersionInfoGeneratedTest.txt";

        [SetUp]
        public void SetUp()
        {
            VersionInfoBuildProcessor.TryRestoreInjection(
                out _);
            DeleteTestAsset(ExistingAssetPath);
            DeleteTestAsset(GeneratedAssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            VersionInfoBuildProcessor.TryRestoreInjection(
                out _);
            DeleteTestAsset(ExistingAssetPath);
            DeleteTestAsset(GeneratedAssetPath);
        }

        [Test]
        public void Recovery_RestoresPreExistingAssetAndMeta()
        {
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(ExistingAssetPath, "original");
            AssetDatabase.ImportAsset(
                ExistingAssetPath,
                ImportAssetOptions.ForceSynchronousImport);
            string originalMeta = File.ReadAllText(
                ExistingAssetPath + ".meta");

            VersionInfoBuildProcessor.PrepareRecovery(
                ExistingAssetPath);
            File.WriteAllText(ExistingAssetPath, "generated");

            bool restored = VersionInfoBuildProcessor
                .TryRestoreInjection(out string error);

            Assert.That(restored, Is.True, error);
            Assert.That(
                File.ReadAllText(ExistingAssetPath),
                Is.EqualTo("original"));
            Assert.That(
                File.ReadAllText(ExistingAssetPath + ".meta"),
                Is.EqualTo(originalMeta));
        }

        [Test]
        public void Recovery_DeletesOnlyToolCreatedAsset()
        {
            VersionInfoBuildProcessor.PrepareRecovery(
                GeneratedAssetPath);
            File.WriteAllText(GeneratedAssetPath, "generated");

            bool restored = VersionInfoBuildProcessor
                .TryRestoreInjection(out string error);

            Assert.That(restored, Is.True, error);
            Assert.That(File.Exists(GeneratedAssetPath), Is.False);
            Assert.That(
                File.Exists(GeneratedAssetPath + ".meta"),
                Is.False);
        }

        [TestCase("VersionInfo", "Assets/Resources/VersionInfo.txt")]
        [TestCase(
            "Build/VersionInfo",
            "Assets/Resources/Build/VersionInfo.txt")]
        public void GetVersionAssetPath_AllowsResourceSubfolders(
            string resourceName,
            string expectedPath)
        {
            Assert.That(
                VersionInfoBuildProcessor.GetVersionAssetPath(
                    resourceName),
                Is.EqualTo(expectedPath));
        }

        private static void DeleteTestAsset(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    path) != null)
            {
                AssetDatabase.DeleteAsset(path);
                return;
            }

            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".meta"))
            {
                File.Delete(path + ".meta");
            }
        }
    }
}
