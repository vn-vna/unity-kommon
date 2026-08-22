using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using Com.Hapiga.Scheherazade.Common.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Tests
{
    public sealed class PuzzleLevelRuntimeTests
    {
        private PuzzleLevelManager _manager;
        private ControlledTextProvider _provider;
        private GameObject _ownedDispatcher;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _ = PuzzleLevelManager.Instance;
            if (!Dispatcher.IsAvailable)
            {
                _ownedDispatcher = new GameObject("Test Dispatcher");
                _ownedDispatcher.AddComponent<Dispatcher>();
                yield return null;
            }

            _provider = ScriptableObject.CreateInstance<ControlledTextProvider>();
            _manager = ScriptableObject.CreateInstance<PuzzleLevelManager>();
            SetInitialProviders(_manager, _provider);
            _manager.Reset();

            IEnumerator initialize = _manager.InitializeManagerCoroutine(2f);
            while (initialize.MoveNext())
            {
                yield return initialize.Current;
            }

            Assert.That(
                _manager.Status,
                Is.EqualTo(ResourceManagerStatus.Initialized));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _manager?.ClearCache();
            if (_manager != null)
            {
                UnityEngine.Object.DestroyImmediate(_manager);
            }

            if (_provider != null)
            {
                _provider.DisposeResources();
                UnityEngine.Object.DestroyImmediate(_provider);
            }

            if (_ownedDispatcher != null)
            {
                UnityEngine.Object.Destroy(_ownedDispatcher);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator DeduplicatedLoad_CancelsOnlyReleasedSubscriber()
        {
            ResourceLoadingHandler<IPuzzleLevelData> first
                = _manager.GetLevelAsync("level_1");
            ResourceLoadingHandler<IPuzzleLevelData> second
                = _manager.GetLevelAsync("level_1");
            yield return null;

            Assert.That(_provider.RequestCount, Is.EqualTo(1));

            first.Cancel();
            yield return null;
            _provider.CompleteLatest("{\"id\":1}");
            yield return WaitForCompletion(second);

            Assert.That(first.ResourceStatus, Is.EqualTo(ResourceStatus.Canceled));
            Assert.That(second.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
            Assert.That(second.Resouce.Type, Is.EqualTo(DataType.Text));
            Assert.That(_manager.CachedLevelIds, Does.Contain("level_1"));
        }

        [UnityTest]
        public IEnumerator TagChange_CancelsOldGenerationAndPartitionsNextRequest()
        {
            ResourceLoadingHandler<IPuzzleLevelData> oldRequest
                = _manager.GetLevelAsync("level_2");
            yield return null;

            _manager.SetCustomTag("branch", "beta");
            yield return null;

            Assert.That(
                oldRequest.ResourceStatus,
                Is.EqualTo(ResourceStatus.Canceled));

            ResourceLoadingHandler<IPuzzleLevelData> newRequest
                = _manager.GetLevelAsync("level_2");
            yield return null;
            Assert.That(_provider.RequestCount, Is.EqualTo(2));
            Assert.That(
                _provider.LatestTags["branch"],
                Is.EqualTo("beta"));

            _provider.CompleteLatest("{\"id\":2}");
            yield return WaitForCompletion(newRequest);

            Assert.That(
                newRequest.ResourceStatus,
                Is.EqualTo(ResourceStatus.Loaded));
            Assert.That(_manager.CachedLevelIds, Does.Contain("level_2"));
        }

        [UnityTest]
        public IEnumerator Reset_CancelsOldLoadAndStartsFreshProviderRequest()
        {
            ResourceLoadingHandler<IPuzzleLevelData> oldRequest
                = _manager.GetLevelAsync("level_reset");
            yield return null;
            Assert.That(_provider.RequestCount, Is.EqualTo(1));

            _manager.Reset();
            IEnumerator initialize = _manager.InitializeManagerCoroutine(2f);
            while (initialize.MoveNext())
            {
                yield return initialize.Current;
            }

            Assert.That(
                oldRequest.ResourceStatus,
                Is.EqualTo(ResourceStatus.Canceled));

            ResourceLoadingHandler<IPuzzleLevelData> freshRequest
                = _manager.GetLevelAsync("level_reset");
            yield return null;
            Assert.That(_provider.RequestCount, Is.EqualTo(2));

            _provider.CompleteLatest("{\"source\":\"fresh\"}");
            yield return WaitForCompletion(freshRequest);

            Assert.That(
                freshRequest.ResourceStatus,
                Is.EqualTo(ResourceStatus.Loaded));
            StringAssert.Contains("fresh", freshRequest.Resouce.GetText());
        }

        [UnityTest]
        public IEnumerator AggressiveRefresh_CompletesAfterCachedLevelsReload()
        {
            ResourceLoadingHandler<IPuzzleLevelData> initial
                = _manager.GetLevelAsync("level_3");
            yield return null;
            _provider.CompleteLatest("{\"id\":3}");
            yield return WaitForCompletion(initial);

            IEnumerator refresh = _manager.RefreshCatalogsCoroutine(
                CatalogInvalidationMode.Aggressive);
            while (refresh.MoveNext())
            {
                if (_provider.RequestCount > 1
                    && !_provider.LatestHandler.IsCompleted)
                {
                    _provider.CompleteLatest("{\"id\":3}");
                }

                yield return refresh.Current;
            }

            Assert.That(_provider.RequestCount, Is.EqualTo(2));
            Assert.That(_manager.CachedLevelIds, Does.Contain("level_3"));
        }

        [Test]
        public void AggressiveRefresh_PropagatesNestedCatalogFailure()
        {
            _provider.CatalogInvalidationException
                = new InvalidOperationException("Nested catalog failure.");

            AggregateException exception = Assert.Throws<AggregateException>(
                () => DrainCoroutine(
                    _manager.RefreshCatalogsCoroutine(
                        CatalogInvalidationMode.Aggressive)
                )
            );
            Assert.That(_provider.InvalidationCount, Is.EqualTo(1));
            StringAssert.Contains(
                "Nested catalog failure.",
                exception.ToString());
        }

        [Test]
        public void Dispatcher_ReportsSynchronousCoroutineDispatch()
        {
            bool completed = false;

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CompleteImmediately(() => completed = true),
                out _);

            Assert.That(dispatched, Is.True);
            Assert.That(completed, Is.True);
        }

        private static IEnumerator WaitForCompletion<T>(
            ResourceLoadingHandler<T> handler)
            where T : class
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!handler.IsCompleted
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(handler.IsCompleted, Is.True);
        }

        private static void SetInitialProviders(
            PuzzleLevelManager manager,
            ScriptableObject provider)
        {
            Type baseType = typeof(AsyncResourceManagerBase<
                PuzzleLevelManager,
                TextAsset>);
            FieldInfo field = baseType.GetField(
                "initialProviders",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(manager, new[] { provider });
        }

        private static IEnumerator CompleteImmediately(Action completion)
        {
            completion?.Invoke();
            yield break;
        }

        private static void DrainCoroutine(IEnumerator coroutine)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(coroutine);
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                if (current.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
            }
        }
    }

    public sealed class PuzzleLevelProviderIntegrationTests
    {
        private GameObject _ownedDispatcher;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Dispatcher.IsAvailable)
            {
                _ownedDispatcher = new GameObject("Provider Test Dispatcher");
                _ownedDispatcher.AddComponent<Dispatcher>();
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_ownedDispatcher != null)
            {
                UnityEngine.Object.Destroy(_ownedDispatcher);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ActiveReferenceTableProvider_LoadsConfiguredLevel()
        {
            PuzzleLevelReferenceTableProvider provider
                = Resources.Load<PuzzleLevelReferenceTableProvider>(
                    "PuzzleLevelReferenceTableProvider");
            Assert.That(provider, Is.Not.Null);

            provider.Initialize();
            yield return WaitUntilInitialized(provider);

            PuzzleLevelId id = new PuzzleLevelId
            {
                ResourceId = "level_1"
            };
            ResourceLoadingHandler<TextAsset> handler
                = new ResourceLoadingHandler<TextAsset>();
            provider.TryLoadResource(id, handler);
            yield return WaitForCompletion(handler);

            Assert.That(handler.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
            Assert.That(handler.Resouce.text, Is.Not.Empty);
            Assert.That(provider.GetDataType(id), Is.EqualTo(DataType.Text));
        }

        [UnityTest]
        public IEnumerator ResourcesProvider_LoadsConfiguredTextAsset()
        {
            PuzzleLevelResourceFolderProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelResourceFolderProvider>();
            try
            {
                SetField(provider, "folderName", "Levels/Files");
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "level_0002" },
                    handler);
                yield return WaitForCompletion(handler);

                Assert.That(
                    handler.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(handler.Resouce.text, Is.Not.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }

        [UnityTest]
        public IEnumerator StreamingProvider_LoadsUtf8Text()
        {
            string folderName = "PuzzleLevelProviderTests_"
                + Guid.NewGuid().ToString("N");
            string folderPath = Path.Combine(
                Application.streamingAssetsPath,
                folderName);
            string filePath = Path.Combine(folderPath, "sample.json");

            PuzzleLevelStreamingAssetProvider provider = null;
            try
            {
                Directory.CreateDirectory(folderPath);
                File.WriteAllText(
                    filePath,
                    "{\"source\":\"streaming\"}",
                    Encoding.UTF8);

                provider = ScriptableObject.CreateInstance<
                    PuzzleLevelStreamingAssetProvider>();
                SetField(provider, "subFolder", folderName);
                SetField(provider, "_pathFormat", "{id}");
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "sample.json" },
                    handler);
                yield return WaitForCompletion(handler);

                Assert.That(
                    handler.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                StringAssert.Contains("streaming", handler.Resouce.text);
                provider.ReleaseResource(handler.Resouce);
            }
            finally
            {
                if (provider != null)
                {
                    UnityEngine.Object.DestroyImmediate(provider);
                }

                DeleteDirectoryAndMeta(folderPath);
            }
        }

        [UnityTest]
        public IEnumerator StreamingProvider_ReinitializeCancelsActiveRequest()
        {
            string folderName = "PuzzleLevelStreamingResetTests_"
                + Guid.NewGuid().ToString("N");
            string folderPath = Path.Combine(
                Application.streamingAssetsPath,
                folderName);
            string filePath = Path.Combine(folderPath, "sample.json");
            PuzzleLevelStreamingAssetProvider provider = null;
            try
            {
                Directory.CreateDirectory(folderPath);
                File.WriteAllText(filePath, "{\"source\":\"fresh\"}");
                provider = ScriptableObject.CreateInstance<
                    PuzzleLevelStreamingAssetProvider>();
                SetField(provider, "subFolder", folderName);
                SetField(provider, "_pathFormat", "{id}");
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "sample.json"
                };
                ResourceLoadingHandler<TextAsset> oldRequest
                    = new ResourceLoadingHandler<TextAsset>();
                oldRequest.LoadingStatus = LoadingStatus.Loading;
                Type providerBaseType
                    = typeof(StreamingAssetProvider<TextAsset>);
                FieldInfo activeHandlersField = providerBaseType.GetField(
                    "_activeHandlers",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(activeHandlersField, Is.Not.Null);
                HashSet<ResourceLoadingHandler<TextAsset>> activeHandlers
                    = activeHandlersField.GetValue(provider)
                        as HashSet<ResourceLoadingHandler<TextAsset>>;
                Assert.That(activeHandlers, Is.Not.Null);
                activeHandlers.Add(oldRequest);
                provider.Initialize();

                Assert.That(
                    oldRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Canceled));
                yield return WaitUntilInitialized(provider);

                ResourceLoadingHandler<TextAsset> freshRequest
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, freshRequest);
                yield return WaitForCompletion(freshRequest);

                Assert.That(
                    freshRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                StringAssert.Contains("fresh", freshRequest.Resouce.text);
                provider.ReleaseResource(freshRequest.Resouce);
            }
            finally
            {
                if (provider != null)
                {
                    UnityEngine.Object.DestroyImmediate(provider);
                }

                DeleteDirectoryAndMeta(folderPath);
            }
        }

        [UnityTest]
        public IEnumerator DownloadableProvider_LoadsLocalUtf8TextAndCachesIt()
        {
            string folderPath = Path.Combine(
                Application.temporaryCachePath,
                "PuzzleLevelDownloadTests_" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(folderPath, "sample.json");
            string otherFilePath = Path.Combine(folderPath, "other.json");
            string cacheSubFolder = "PuzzleLevelDownloadCache_"
                + Guid.NewGuid().ToString("N");
            string cacheFolderPath = Path.Combine(
                Application.temporaryCachePath,
                cacheSubFolder);

            PuzzleLevelDownloadableProvider provider = null;
            try
            {
                Directory.CreateDirectory(folderPath);
                File.WriteAllText(
                    filePath,
                    "{\"source\":\"download\"}",
                    Encoding.UTF8);
                File.WriteAllText(
                    otherFilePath,
                    "{\"source\":\"survivor\"}",
                    Encoding.UTF8);

                provider = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
                string baseUrl = new Uri(folderPath + Path.DirectorySeparatorChar)
                    .AbsoluteUri;
                SetField(provider, "_baseUrl", baseUrl);
                SetField(provider, "_urlFormat", "{0}");
                SetField(
                    provider,
                    "cacheBasePath",
                    CacheBasePathType.TemporaryCachePath);
                SetField(provider, "cacheSubFolder", cacheSubFolder);
                provider.Initialize();

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "sample.json"
                };
                ResourceLoadingHandler<TextAsset> first
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, first);
                yield return WaitForCompletion(first);

                ResourceLoadingHandler<TextAsset> second
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, second);

                Assert.That(first.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(second.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                StringAssert.Contains("download", second.Resouce.text);

                ResourceLoadingHandler<TextAsset> other
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "other.json" },
                    other);
                yield return WaitForCompletion(other);
                Assert.That(other.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(
                    Directory.GetFiles(cacheFolderPath, "*.id"),
                    Has.Length.EqualTo(2));

                provider.ReleaseResource(first.Resouce);
                provider.ReleaseResource(second.Resouce);
                provider.ReleaseResource(other.Resouce);

                UnityEngine.Object.DestroyImmediate(provider);
                Directory.Delete(folderPath, true);
                provider = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
                SetField(provider, "_baseUrl", baseUrl);
                SetField(provider, "_urlFormat", "{0}");
                SetField(
                    provider,
                    "cacheBasePath",
                    CacheBasePathType.TemporaryCachePath);
                SetField(provider, "cacheSubFolder", cacheSubFolder);
                provider.Initialize();
                provider.ClearCache("sample.json");

                Assert.That(
                    Directory.GetFiles(cacheFolderPath, "*.id"),
                    Has.Length.EqualTo(1));

                ResourceLoadingHandler<TextAsset> surviving
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "other.json" },
                    surviving);
                yield return WaitForCompletion(surviving);

                Assert.That(
                    surviving.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                StringAssert.Contains("survivor", surviving.Resouce.text);
                provider.ReleaseResource(surviving.Resouce);
            }
            finally
            {
                if (provider != null)
                {
                    provider.ClearCache();
                    UnityEngine.Object.DestroyImmediate(provider);
                }

                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }

                if (Directory.Exists(cacheFolderPath))
                {
                    Directory.Delete(cacheFolderPath, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator DownloadableProvider_CoroutineExceptionFailsHandler()
        {
            string folderPath = Path.Combine(
                Application.temporaryCachePath,
                "PuzzleLevelHeaderTests_" + Guid.NewGuid().ToString("N"));
            PuzzleLevelDownloadableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            try
            {
                Directory.CreateDirectory(folderPath);
                string baseUrl = new Uri(
                    folderPath + Path.DirectorySeparatorChar).AbsoluteUri;
                SetField(provider, "_baseUrl", baseUrl);
                SetField(provider, "_urlFormat", "{0}");
                SetField(
                    provider,
                    "headers",
                    new[]
                    {
                        new CustomDownloadHeader
                        {
                            key = null,
                            value = "invalid"
                        }
                    });
                provider.Initialize();

                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "missing.json" },
                    handler);
                yield return WaitForCompletion(handler);

                Assert.That(
                    handler.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Failed));
                Assert.That(handler.Exception, Is.Not.Null);
            }
            finally
            {
                provider.ClearCache();
                UnityEngine.Object.DestroyImmediate(provider);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator DownloadableProvider_DeduplicatedResourceUsesLeases()
        {
            PuzzleLevelDownloadableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            TextAsset resource = new TextAsset("shared");
            try
            {
                ResourceLoadingHandler<TextAsset> first
                    = new ResourceLoadingHandler<TextAsset>();
                ResourceLoadingHandler<TextAsset> second
                    = new ResourceLoadingHandler<TextAsset>();
                Type providerBaseType
                    = typeof(DownloadableResourceProvider<TextAsset>);
                Type activeDownloadType = providerBaseType.GetNestedType(
                    "ActiveDownload",
                    BindingFlags.NonPublic);
                Assert.That(activeDownloadType, Is.Not.Null);
                if (activeDownloadType.ContainsGenericParameters)
                {
                    activeDownloadType = activeDownloadType.MakeGenericType(
                        typeof(TextAsset));
                }

                object activeDownload = Activator.CreateInstance(
                    activeDownloadType,
                    true);
                FieldInfo handlersField = activeDownloadType.GetField(
                    "Handlers",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(handlersField, Is.Not.Null);
                handlersField.SetValue(
                    activeDownload,
                    new List<ResourceLoadingHandler<TextAsset>>
                    {
                        first,
                        second
                    });
                MethodInfo completeHandlers = providerBaseType.GetMethod(
                    "CompleteHandlers",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(completeHandlers, Is.Not.Null);
                completeHandlers.Invoke(
                    provider,
                    new[] { activeDownload, resource });

                Assert.That(
                    ReferenceEquals(first.Resouce, second.Resouce),
                    Is.True);
                provider.ReleaseResource(first.Resouce);
                yield return null;
                Assert.That(second.Resouce, Is.Not.Null);

                provider.ReleaseResource(second.Resouce);
                yield return null;
                Assert.That(second.Resouce == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
                if (resource != null)
                {
                    UnityEngine.Object.DestroyImmediate(resource);
                }
            }
        }

        [Test]
        public void DownloadableProvider_ReinitializeCancelsActiveRequests()
        {
            PuzzleLevelDownloadableProvider provider
                = ScriptableObject.CreateInstance<
                    PuzzleLevelDownloadableProvider>();
            try
            {
                Type providerBaseType
                    = typeof(DownloadableResourceProvider<TextAsset>);
                Type activeDownloadType = providerBaseType.GetNestedType(
                    "ActiveDownload",
                    BindingFlags.NonPublic);
                Assert.That(activeDownloadType, Is.Not.Null);
                if (activeDownloadType.ContainsGenericParameters)
                {
                    activeDownloadType = activeDownloadType.MakeGenericType(
                        typeof(TextAsset));
                }

                object activeDownload = Activator.CreateInstance(
                    activeDownloadType,
                    true);
                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>
                    {
                        LoadingStatus = LoadingStatus.Loading
                    };
                activeDownloadType.GetField("Handlers")?.SetValue(
                    activeDownload,
                    new List<ResourceLoadingHandler<TextAsset>> { handler });
                activeDownloadType.GetField("CacheKey")?.SetValue(
                    activeDownload,
                    "active");

                FieldInfo activeDownloadsField = providerBaseType.GetField(
                    "_activeDownloads",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(activeDownloadsField, Is.Not.Null);
                IDictionary activeDownloads
                    = activeDownloadsField.GetValue(provider) as IDictionary;
                Assert.That(activeDownloads, Is.Not.Null);
                activeDownloads.Add("active", activeDownload);

                provider.Initialize();

                Assert.That(
                    handler.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Canceled));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_SelectiveDiskClearSurvivesRestart()
        {
            string cacheSubFolder = "PuzzleLevelCachedProviderTests_"
                + Guid.NewGuid().ToString("N");
            string cacheFolderPath = Path.Combine(
                Application.temporaryCachePath,
                cacheSubFolder);
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider = null;
            try
            {
                provider = CreateCachedProvider(
                    wrapped,
                    cacheSubFolder);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_persisted"
                };
                ResourceLoadingHandler<TextAsset> handler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, handler);
                yield return null;
                wrapped.CompleteLatest("persisted");
                yield return WaitForCompletion(handler);

                PuzzleLevelId survivingId = new PuzzleLevelId
                {
                    ResourceId = "level_surviving"
                };
                ResourceLoadingHandler<TextAsset> secondHandler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(survivingId, secondHandler);
                yield return null;
                wrapped.CompleteLatest("surviving");
                yield return WaitForCompletion(secondHandler);

                Assert.That(
                    Directory.GetFiles(cacheFolderPath, "*.id"),
                    Has.Length.EqualTo(2));

                UnityEngine.Object.DestroyImmediate(provider);
                provider = CreateCachedProvider(
                    wrapped,
                    cacheSubFolder);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);
                provider.ClearCache("level_persisted");

                Assert.That(
                    Directory.GetFiles(cacheFolderPath, "*.id"),
                    Has.Length.EqualTo(1));

                int wrappedRequestCount = wrapped.RequestCount;
                ResourceLoadingHandler<TextAsset> survivingHandler
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(survivingId, survivingHandler);
                yield return WaitForCompletion(survivingHandler);

                Assert.That(
                    survivingHandler.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(survivingHandler.Resouce.text, Is.EqualTo("surviving"));
                Assert.That(wrapped.RequestCount, Is.EqualTo(wrappedRequestCount));
            }
            finally
            {
                if (provider != null)
                {
                    provider.ClearCache();
                    UnityEngine.Object.DestroyImmediate(provider);
                }

                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
                if (Directory.Exists(cacheFolderPath))
                {
                    Directory.Delete(cacheFolderPath, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_DeduplicatesAndServesMemoryHit()
        {
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            try
            {
                SetField(provider, "_wrappedProviderAsset", wrapped);
                SetField(provider, "_cacheTTL", 0f);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_cached"
                };
                ResourceLoadingHandler<TextAsset> first
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, first);
                yield return null;
                wrapped.CompleteLatest("cached");
                yield return WaitForCompletion(first);

                ResourceLoadingHandler<TextAsset> second
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, second);

                Assert.That(wrapped.RequestCount, Is.EqualTo(1));
                Assert.That(second.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(second.Resouce.text, Is.EqualTo("cached"));
            }
            finally
            {
                provider.ClearCache();
                UnityEngine.Object.DestroyImmediate(provider);
                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_EvictionWaitsForCallerRelease()
        {
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            try
            {
                SetField(provider, "_wrappedProviderAsset", wrapped);
                SetField(provider, "_maxCacheEntries", 1);
                SetField(provider, "_cacheTTL", 0f);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                ResourceLoadingHandler<TextAsset> first
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "level_first" },
                    first);
                yield return null;
                wrapped.CompleteLatest("first");
                yield return WaitForCompletion(first);

                ResourceLoadingHandler<TextAsset> second
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(
                    new PuzzleLevelId { ResourceId = "level_second" },
                    second);
                yield return null;
                wrapped.CompleteLatest("second");
                yield return WaitForCompletion(second);

                Assert.That(wrapped.ReleaseCount, Is.EqualTo(0));
                Assert.That(first.Resouce.text, Is.EqualTo("first"));

                provider.ReleaseResource(first.Resouce);
                Assert.That(wrapped.ReleaseCount, Is.EqualTo(1));
                Assert.That(first.Resouce == null, Is.True);
                Assert.That(second.Resouce.text, Is.EqualTo("second"));

                provider.ReleaseResource(second.Resouce);
                provider.ClearCache();
                Assert.That(wrapped.ReleaseCount, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_ClearRetiresOldRequestGeneration()
        {
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            try
            {
                SetField(provider, "_wrappedProviderAsset", wrapped);
                SetField(provider, "_cacheTTL", 0f);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_generation"
                };
                ResourceLoadingHandler<TextAsset> oldRequest
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, oldRequest);
                yield return null;

                provider.ClearCache(id.ResourceId);
                ResourceLoadingHandler<TextAsset> newRequest
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, newRequest);

                Assert.That(
                    oldRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Canceled));
                Assert.That(wrapped.RequestCount, Is.EqualTo(2));

                wrapped.CompleteRequest(0, "stale");
                yield return null;

                Assert.That(newRequest.IsCompleted, Is.False);
                Assert.That(wrapped.ReleaseCount, Is.EqualTo(1));

                wrapped.CompleteRequest(1, "fresh");
                yield return WaitForCompletion(newRequest);

                Assert.That(
                    newRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(newRequest.Resouce.text, Is.EqualTo("fresh"));
            }
            finally
            {
                provider.ClearCache();
                UnityEngine.Object.DestroyImmediate(provider);
                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_SelectiveClearPreservesOtherRequest()
        {
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            try
            {
                SetField(provider, "_wrappedProviderAsset", wrapped);
                SetField(provider, "_cacheTTL", 0f);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId firstId = new PuzzleLevelId
                {
                    ResourceId = "level_first"
                };
                PuzzleLevelId secondId = new PuzzleLevelId
                {
                    ResourceId = "level_second"
                };
                ResourceLoadingHandler<TextAsset> first
                    = new ResourceLoadingHandler<TextAsset>();
                ResourceLoadingHandler<TextAsset> second
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(firstId, first);
                provider.TryLoadResource(secondId, second);
                yield return null;

                provider.ClearCache(firstId.ResourceId);
                provider.ClearCache("unknown_level");
                wrapped.CompleteRequest(1, "survivor");
                yield return WaitForCompletion(second);

                Assert.That(first.ResourceStatus, Is.EqualTo(ResourceStatus.Canceled));
                Assert.That(second.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(second.Resouce.text, Is.EqualTo("survivor"));
                Assert.That(wrapped.ReleaseCount, Is.EqualTo(0));

                ResourceLoadingHandler<TextAsset> memoryHit
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(secondId, memoryHit);
                Assert.That(memoryHit.ResourceStatus, Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(wrapped.RequestCount, Is.EqualTo(2));
            }
            finally
            {
                provider.ClearCache();
                UnityEngine.Object.DestroyImmediate(provider);
                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
            }
        }

        [UnityTest]
        public IEnumerator CachedProvider_ReinitializeRejectsOldCompletion()
        {
            ControlledTextProvider wrapped
                = ScriptableObject.CreateInstance<ControlledTextProvider>();
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            try
            {
                SetField(provider, "_wrappedProviderAsset", wrapped);
                SetField(provider, "_cacheTTL", 0f);
                provider.Initialize();
                yield return WaitUntilInitialized(provider);

                PuzzleLevelId id = new PuzzleLevelId
                {
                    ResourceId = "level_reinitialized"
                };
                ResourceLoadingHandler<TextAsset> oldRequest
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, oldRequest);
                yield return null;

                provider.Initialize();
                wrapped.CompleteRequest(0, "stale");
                yield return null;
                yield return WaitUntilInitialized(provider);

                Assert.That(
                    oldRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Canceled));
                Assert.That(wrapped.ReleaseCount, Is.EqualTo(1));

                ResourceLoadingHandler<TextAsset> freshRequest
                    = new ResourceLoadingHandler<TextAsset>();
                provider.TryLoadResource(id, freshRequest);
                yield return null;
                wrapped.CompleteRequest(1, "fresh");
                yield return WaitForCompletion(freshRequest);

                Assert.That(
                    freshRequest.ResourceStatus,
                    Is.EqualTo(ResourceStatus.Loaded));
                Assert.That(freshRequest.Resouce.text, Is.EqualTo("fresh"));
            }
            finally
            {
                provider.ClearCache();
                UnityEngine.Object.DestroyImmediate(provider);
                wrapped.DisposeResources();
                UnityEngine.Object.DestroyImmediate(wrapped);
            }
        }

        private static IEnumerator WaitUntilInitialized(
            IAsyncResourceProvider provider)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!provider.IsInitialized
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(provider.IsInitialized, Is.True);
        }

        private static PuzzleLevelCachedProvider CreateCachedProvider(
            ControlledTextProvider wrapped,
            string cacheSubFolder)
        {
            PuzzleLevelCachedProvider provider
                = ScriptableObject.CreateInstance<PuzzleLevelCachedProvider>();
            SetField(provider, "_wrappedProviderAsset", wrapped);
            SetField(provider, "_cacheTTL", 60f);
            SetField(
                provider,
                "_cacheBasePath",
                CacheBasePathType.TemporaryCachePath);
            SetField(provider, "_cacheSubFolder", cacheSubFolder);
            return provider;
        }

        private static IEnumerator WaitForCompletion<T>(
            ResourceLoadingHandler<T> handler)
            where T : UnityEngine.Object
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!handler.IsCompleted
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(handler.IsCompleted, Is.True);
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

        private static void DeleteDirectoryAndMeta(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }

            string metaPath = directoryPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }

    public sealed class ControlledTextProvider :
        ScriptableObject,
        IAsyncResourceProvider<TextAsset>,
        IInvalidatableCatalog,
        IAsyncResourceReleaseProvider<TextAsset>
    {
        private readonly List<ResourceLoadingHandler<TextAsset>> _handlers
            = new List<ResourceLoadingHandler<TextAsset>>();
        private readonly List<TextAsset> _resources = new List<TextAsset>();

        public int Priority => 0;
        public bool IsInitialized { get; private set; }
        public float ResourceLoadingTimeout => 5f;
        public int RequestCount => _handlers.Count;
        public int ReleaseCount { get; private set; }
        public ResourceLoadingHandler<TextAsset> LatestHandler =>
            _handlers.Count == 0 ? null : _handlers[_handlers.Count - 1];
        public IReadOnlyDictionary<string, string> LatestTags { get; private set; }
        public Exception CatalogInvalidationException { get; set; }
        public int InvalidationCount { get; private set; }

        public void Initialize()
        {
            IsInitialized = true;
        }

        public void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<TextAsset> handler)
        {
            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;
            _handlers.Add(handler);

            if (id is PuzzleLevelId puzzleLevelId
                && puzzleLevelId.CustomTags != null)
            {
                LatestTags = new Dictionary<string, string>(
                    puzzleLevelId.CustomTags);
            }
            else
            {
                LatestTags = new Dictionary<string, string>();
            }
        }

        public void CompleteLatest(string text)
        {
            CompleteRequest(_handlers.Count - 1, text);
        }

        public void CompleteRequest(int requestIndex, string text)
        {
            Assert.That(requestIndex, Is.InRange(0, _handlers.Count - 1));
            ResourceLoadingHandler<TextAsset> handler = _handlers[requestIndex];
            Assert.That(handler, Is.Not.Null);
            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return;
            }

            TextAsset resource = new TextAsset(text);
            _resources.Add(resource);
            handler.Resouce = resource;
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Loaded;
        }

        public void ReleaseResource(TextAsset resource)
        {
            if (resource == null)
            {
                return;
            }

            ReleaseCount++;
            UnityEngine.Object.DestroyImmediate(resource);
        }

        public void DisposeResources()
        {
            for (int i = 0; i < _resources.Count; i++)
            {
                if (_resources[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_resources[i]);
                }
            }

            _resources.Clear();
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (!_handlers[i].IsCompleted)
                {
                    _handlers[i].Cancel();
                }
            }
        }

        public void InvalidateCatalog(CatalogInvalidationMode mode)
        {
            InvalidationCount++;
            if (CatalogInvalidationException != null)
            {
                throw CatalogInvalidationException;
            }
        }

        public IEnumerator InvalidateCatalogCoroutine(
            CatalogInvalidationMode mode)
        {
            InvalidationCount++;
            if (CatalogInvalidationException != null)
            {
                yield return ThrowNestedCatalogException();
            }
        }

        private IEnumerator ThrowNestedCatalogException()
        {
            yield return ThrowCatalogException();
        }

        private IEnumerator ThrowCatalogException()
        {
            if (CatalogInvalidationException != null)
            {
                throw CatalogInvalidationException;
            }

            yield break;
        }
    }
}
