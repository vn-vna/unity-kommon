using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor.Tests
{
    public sealed class CatalogBuildUtilityTests
    {
        private string _sourceFilePath;
        private CatalogBuilderConfig _config;

        [SetUp]
        public void SetUp()
        {
            _sourceFilePath = Path.Combine(
                Application.temporaryCachePath,
                "catalog-builder-test-level.json");
            File.WriteAllText(_sourceFilePath, "{\"level\":1}");
            _config = ScriptableObject.CreateInstance<CatalogBuilderConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_sourceFilePath))
            {
                File.Delete(_sourceFilePath);
            }

            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Validate_RejectsDuplicateIdsAndPathTraversal()
        {
            _config.Entries.Add(CreateEntry("catalog_1", "Levels/catalog_1.json"));
            _config.Entries.Add(CreateEntry("catalog_1", "../outside.json"));

            CatalogValidationResult result = CatalogBuildUtility.Validate(_config);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Duplicate level ID"));
            Assert.That(result.Errors, Has.Some.Contains("must not contain"));
        }

        [Test]
        public void Validate_CreatesStableManifestHashForReorderedEntries()
        {
            _config.Entries.Add(CreateEntry("catalog_2", "Levels/catalog_2.json"));
            _config.Entries.Add(CreateEntry("catalog_1", "Levels/catalog_1.json"));

            CatalogValidationResult first = CatalogBuildUtility.Validate(_config);
            _config.Entries.Reverse();
            CatalogValidationResult second = CatalogBuildUtility.Validate(_config);

            Assert.That(first.IsValid, Is.True);
            Assert.That(second.IsValid, Is.True);
            Assert.That(second.ManifestHash, Is.EqualTo(first.ManifestHash));
        }

        [Test]
        public void Validate_RejectsUnknownDataType()
        {
            StagedCatalogEntry entry = CreateEntry(
                "level_1",
                "Levels/level_1.json"
            );
            entry.Type = DataType.Unknown;
            _config.Entries.Add(entry);

            CatalogValidationResult result = CatalogBuildUtility.Validate(_config);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("unsupported data type"));
        }

        [Test]
        public void Validate_RejectsInvalidPuzzleLevelContent()
        {
            File.WriteAllText(_sourceFilePath, "not valid json");
            _config.Entries.Add(CreateEntry("level_0000", "Levels/level_0000.json"));

            CatalogValidationResult result = CatalogBuildUtility.Validate(_config);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors,
                Has.Some.Contains("SANDSIM_LEVEL_DESERIALIZATION_FAILED")
            );
        }

        [Test]
        public void GetStaleRelativePaths_IncludesRemovedEntriesAndOldCatalog()
        {
            CatalogBuildState previous = new CatalogBuildState
            {
                CatalogRelativePath = "catalog.json",
                Entries =
                {
                    new GeneratedCatalogEntry { RelativePath = "Levels/old.json" },
                    new GeneratedCatalogEntry { RelativePath = "Levels/current.json" }
                }
            };
            CatalogBuildState current = new CatalogBuildState
            {
                CatalogRelativePath = "levels-catalog.json",
                Entries =
                {
                    new GeneratedCatalogEntry { RelativePath = "Levels/current.json" }
                }
            };

            var stalePaths = CatalogBuildUtility.GetStaleRelativePaths(
                previous,
                current);

            Assert.That(stalePaths, Does.Contain("Levels/old.json"));
            Assert.That(stalePaths, Does.Contain("catalog.json"));
        }

        private StagedCatalogEntry CreateEntry(string id, string relativePath)
        {
            return new StagedCatalogEntry
            {
                Id = id,
                Type = DataType.Text,
                RelativePath = relativePath,
                SourceFilePath = _sourceFilePath
            };
        }
    }
}
