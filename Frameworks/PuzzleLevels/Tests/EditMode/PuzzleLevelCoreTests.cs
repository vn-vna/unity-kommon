using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using NUnit.Framework;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Tests
{
    public sealed class PuzzleLevelCoreTests
    {
        private const string SampleContentHash
            = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        [Test]
        public void PuzzleLevelData_ProtectsByteOwnershipAndReportsParseFailure()
        {
            byte[] source = Encoding.UTF8.GetBytes("{\"value\":7}");
            PuzzleLevelData data = new PuzzleLevelData(
                "level_7",
                source,
                DataType.Text);

            source[0] = 0;
            byte[] firstRead = data.GetBytes();
            firstRead[0] = 0;

            Assert.That(data.GetText(), Is.EqualTo("{\"value\":7}"));
            Assert.That(data.GetBytes()[0], Is.EqualTo((byte)'{'));
            Assert.That(
                data.TryGetParsed(out TestPayload payload, out Exception error),
                Is.True);
            Assert.That(error, Is.Null);
            Assert.That(payload.value, Is.EqualTo(7));

            PuzzleLevelData malformed = new PuzzleLevelData(
                "broken",
                Encoding.UTF8.GetBytes(string.Empty),
                DataType.Text);
            Assert.That(
                malformed.TryGetParsed<TestPayload>(out _, out error),
                Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void PuzzleLevelData_SnapshotsTextAssetBytes()
        {
            TextAsset source = new TextAsset("{\"value\":11}");
            PuzzleLevelData data = new PuzzleLevelData(
                "level_11",
                source,
                DataType.Text);

            UnityEngine.Object.DestroyImmediate(source);

            Assert.That(data.IsLoaded, Is.True);
            Assert.That(data.GetText(), Is.EqualTo("{\"value\":11}"));
        }

        [Test]
        public void PuzzleLevelId_ResolvesTagsAndRejectsMissingTags()
        {
            PuzzleLevelResourceFolderProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelResourceFolderProvider>();
            try
            {
                SetField(provider, "_pathFormat", "{branch}/level_{id}");
                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "12",
                    CustomTags = new Dictionary<string, string>
                    {
                        ["branch"] = "beta"
                    }
                };

                string resolved
                    = ((IResourceFolderAsyncResourceId)id)
                        .GetResourcePath(provider);
                Assert.That(resolved, Is.EqualTo("beta/level_12"));

                id.CustomTags = null;
                Assert.Throws<FormatException>(() =>
                    ((IResourceFolderAsyncResourceId)id)
                    .GetResourcePath(provider));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }

        [Test]
        public void ReferenceTableProvider_ResolvesTypedFormattedEntry()
        {
            TextAsset asset = new TextAsset("{\"value\":1}");
            PuzzleLevelReferenceTable table
                = ScriptableObject.CreateInstance<PuzzleLevelReferenceTable>();
            PuzzleLevelReferenceTableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelReferenceTableProvider>();
            try
            {
                SetField(
                    table,
                    "_entries",
                    new List<PuzzleLevelReferenceTable.Entry>
                    {
                        new PuzzleLevelReferenceTable.Entry
                        {
                            Id = "beta_level_1",
                            Asset = asset,
                            DataType = DataType.Text
                        }
                    });
                Invoke(table, "BuildLookup");
                SetField(provider, "_table", table);
                SetField(provider, "_keyFormat", "{branch}_{id}");
                provider.Initialize();

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_1",
                    CustomTags = new Dictionary<string, string>
                    {
                        ["branch"] = "beta"
                    }
                };

                Assert.That(provider.HasResource(id), Is.True);
                Assert.That(
                    ((IAsyncResourceDataTypeResolver)provider)
                    .GetDataType(id),
                    Is.EqualTo(DataType.Text));

                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, handler);
                Assert.That(handler.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(handler.Resouce, Is.SameAs(asset));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
                UnityEngine.Object.DestroyImmediate(table);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TextOnlyProviders_RejectBinaryMetadata()
        {
            PuzzleLevelDownloadableProvider downloadable
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            PuzzleLevelStreamingAssetProvider streaming
                = ScriptableObject.CreateInstance<
                    PuzzleLevelStreamingAssetProvider>();
            try
            {
                Assert.That(
                    ((IAsyncResourceDataTypePolicy)downloadable)
                    .SupportsDataType(DataType.Binary),
                    Is.False);
                Assert.That(
                    ((IAsyncResourceDataTypePolicy)streaming)
                    .SupportsDataType(DataType.Binary),
                    Is.False);
                Assert.That(
                    ((IAsyncResourceDataTypePolicy)downloadable)
                    .SupportsDataType(DataType.Text),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(downloadable);
                UnityEngine.Object.DestroyImmediate(streaming);
            }
        }

        [Test]
        public void ProviderCacheKeys_IncludeCatalogContentHash()
        {
            CatalogData catalog = new CatalogData();
            catalog.LoadFromJson(
                "{\"entries\":[{\"id\":\"level_hash\",\"type\":\"text\","
                + "\"relativePath\":\"levels/level_hash.json\","
                + $"\"contentHash\":\"{SampleContentHash}\"}}]}}"
            );
            PuzzleLevelId id = new PuzzleLevelId
            {
                ResourceId = "level_hash"
            };

            PuzzleLevelStreamingAssetProvider streaming
                = ScriptableObject.CreateInstance<
                    PuzzleLevelStreamingAssetProvider>();
            PuzzleLevelResourceFolderProvider resources
                = ScriptableObject.CreateInstance<
                    PuzzleLevelResourceFolderProvider>();
            PuzzleLevelDownloadableProvider downloadable
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            PuzzleLevelCachedProvider cached
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
#if UNITY_ADDRESSABLES
            PuzzleLevelAddressableProvider addressable
                = ScriptableObject.CreateInstance<
                    PuzzleLevelAddressableProvider>();
#endif
            try
            {
                SetField(streaming, "_catalogData", catalog);
                SetField(resources, "_catalogData", catalog);
                SetField(downloadable, "_catalogData", catalog);
                SetField(downloadable, "_baseUrl", "https://cdn.example.com/");
                SetField(cached, "_wrappedProviderAsset", streaming);
#if UNITY_ADDRESSABLES
                SetField(addressable, "_catalogData", catalog);
#endif

                AssertCacheKeyContainsHash(streaming, id);
                AssertCacheKeyContainsHash(resources, id);
                AssertCacheKeyContainsHash(downloadable, id);

                SetField(streaming, "_catalogData", null);
                SetField(streaming, "_pathFormat", "{id}");
                SetField(cached, "_catalogData", catalog);
                AssertCacheKeyContainsHash(cached, id);
#if UNITY_ADDRESSABLES
                AssertCacheKeyContainsHash(addressable, id);
#endif
            }
            finally
            {
#if UNITY_ADDRESSABLES
                UnityEngine.Object.DestroyImmediate(addressable);
#endif
                UnityEngine.Object.DestroyImmediate(cached);
                UnityEngine.Object.DestroyImmediate(downloadable);
                UnityEngine.Object.DestroyImmediate(resources);
                UnityEngine.Object.DestroyImmediate(streaming);
            }
        }

        [Test]
        public void DownloadableProvider_RejectsContradictoryRequiredCatalog()
        {
            PuzzleLevelDownloadableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            try
            {
                SetField(provider, "_forceRequiredCatalog", true);
                SetField(provider, "_useCatalog", false);

                provider.Initialize();

                IAsyncResourceInitializationStatus status = provider;
                Assert.That(provider.IsInitialized, Is.False);
                Assert.That(status.InitializationException, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }

        [Test]
        public void DownloadableValidation_RejectsUnknownTagsAndSchemes()
        {
            HashSet<string> configuredTags = new HashSet<string>
            {
                "region"
            };

            Assert.That(
                InvokeUrlValidation(
                    "https://{region}.example.com",
                    "levels/{id}.json",
                    configuredTags,
                    out string error),
                Is.True,
                error);
            Assert.That(
                InvokeUrlValidation(
                    "https://{unknown}.example.com",
                    "levels/{id}.json",
                    configuredTags,
                    out _),
                Is.False);
            Assert.That(
                InvokeUrlValidation(
                    string.Empty,
                    "javascript:alert({id})",
                    configuredTags,
                    out _),
                Is.False);
            Assert.That(
                InvokeUrlValidation(
                    string.Empty,
                    "levels/{id}.json",
                    configuredTags,
                    out _),
                Is.False);
        }

#if UNITY_ADDRESSABLES
        [Test]
        public void AddressableProvider_ResolvesConfiguredKey()
        {
            PuzzleLevelAddressableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelAddressableProvider>();
            try
            {
                SetField(provider, "_keyFormat", "levels/{branch}/{id}");
                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_4",
                    CustomTags = new Dictionary<string, string>
                    {
                        ["branch"] = "release"
                    }
                };

                string key = ((IAsyncResourceCacheKeyProvider)provider)
                    .GetCacheKey(id);
                Assert.That(key, Is.EqualTo("levels/release/level_4"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }
#endif

        [Test]
        public void CatalogData_RejectsDuplicateIds()
        {
            CatalogData catalog = new CatalogData();
            catalog.LoadFromJson(
                "{\"Entries\":["
                + "{\"Id\":\"level_1\",\"Type\":1},"
                + "{\"Id\":\"level_1\",\"Type\":1}]}"
            );

            Assert.That(catalog.IsLoaded, Is.False);
            Assert.That(catalog.LastException, Is.Not.Null);
            StringAssert.Contains("duplicate", catalog.LastException.Message.ToLowerInvariant());
        }

        [Test]
        public void CatalogData_ParsesCanonicalAndLegacyPathsWithContentHash()
        {
            const string Hash
                = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            CatalogData catalog = new CatalogData();
            catalog.LoadFromJson(
                "{\"entries\":["
                + "{\"id\":\"canonical\",\"type\":\"text\","
                + "\"relativePath\":\"levels/canonical.json\","
                + $"\"contentHash\":\"{Hash}\"}},"
                + "{\"id\":\"legacy\",\"type\":\"text\","
                + "\"path\":\"levels/legacy.json\"}]}"
            );

            Assert.That(catalog.IsLoaded, Is.True);
            Assert.That(
                catalog.GetRelativePath("canonical"),
                Is.EqualTo("levels/canonical.json"));
            Assert.That(catalog.GetContentHash("canonical"), Is.EqualTo(Hash));
            Assert.That(
                catalog.GetRelativePath("legacy"),
                Is.EqualTo("levels/legacy.json"));
        }

        [Test]
        public void CatalogContentHash_ValidatesSha256()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("abc");
            const string Hash
                = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

            Assert.That(
                CatalogContentHash.TryValidate(
                    "level_1",
                    bytes,
                    Hash,
                    out Exception error),
                Is.True);
            Assert.That(error, Is.Null);
            Assert.That(
                CatalogContentHash.TryValidate(
                    "level_1",
                    bytes,
                    new string('0', 64),
                    out error),
                Is.False);
            Assert.That(error, Is.TypeOf<System.IO.InvalidDataException>());
        }

        [Test]
        public void OverrideRegistry_RejectsMismatchedDataAndCachesSnapshots()
        {
            GameObject gameObject = new GameObject("OverrideRegistryTest");
            PuzzleLevelOverrideRegistry registry
                = gameObject.AddComponent<PuzzleLevelOverrideRegistry>();
            try
            {
                PuzzleLevelData data = new PuzzleLevelData(
                    "level_1",
                    Encoding.UTF8.GetBytes("{}"),
                    DataType.Text);

                Assert.That(registry.SetOverride("other", data), Is.False);
                Assert.That(registry.SetOverride("level_1", data), Is.True);
                Assert.That(registry.GetOverriddenIds(), Is.SameAs(
                    registry.GetOverriddenIds()));
                Assert.That(registry.TryGet("level_1"), Is.SameAs(data));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            Assert.Fail($"Field '{fieldName}' was not found.");
        }

        private static void AssertCacheKeyContainsHash(
            IAsyncResourceCacheKeyProvider provider,
            IAsyncResourceId id)
        {
            Assert.That(
                provider.GetCacheKey(id),
                Does.EndWith("\n" + SampleContentHash));
        }

        private static bool InvokeUrlValidation(
            string baseUrl,
            string relativeTemplate,
            HashSet<string> configuredTags,
            out string error)
        {
            Type validatorType = Type.GetType(
                "Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor."
                + "PuzzleLevelProjectValidator, "
                + "Com.Hapiga.Scheherazade.PuzzleLevels.Editor");
            Assert.That(validatorType, Is.Not.Null);
            MethodInfo method = validatorType.GetMethod(
                "TryResolveAbsoluteUrl",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] arguments =
            {
                baseUrl,
                relativeTemplate,
                configuredTags,
                null
            };
            bool result = (bool)method.Invoke(null, arguments);
            error = arguments[3] as string;
            return result;
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }

        [Serializable]
        private sealed class TestPayload
        {
            public int value;
        }
    }
}
