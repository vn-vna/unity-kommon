using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelManager",
        menuName = "Scheherazade/Puzzle Levels/Manager"
    )]
    [AsyncResourceManagerHost("Project/Tools/Puzzle Levels")]
    public class PuzzleLevelManager :
        AsyncResourceManagerBase<PuzzleLevelManager, TextAsset>
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Preloader configuration applied at startup.")]
#endif
        [SerializeField]
        private PuzzleLevelPreloaderConfig _preloaderConfig;

#if UNITY_EDITOR
        [Tooltip("Override configuration applied at startup.")]
#endif
        [SerializeField]
        private PuzzleLevelOverrideConfig _overrideConfig;

        #endregion

        #region Private Fields

        private PuzzleLevelOverrideRegistry _overrideRegistry;

        #endregion

        #region Properties

        internal PuzzleLevelPreloaderConfig PreloaderConfig
            => _preloaderConfig;

        internal PuzzleLevelOverrideConfig OverrideConfig
            => _overrideConfig;

        #endregion

        #region Runtime Initialization

        private const string OverrideConfigResourcePath
            = "PuzzleLevelOverrideConfig";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntimeObjects()
        {
            if (Object.FindObjectOfType<PuzzleLevelOverrideRegistry>() != null)
            {
                return;
            }

            var manager = Instance;

            // Create override registry
            var go = new GameObject("[PuzzleLevelOverrideRegistry]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            var registry = go.AddComponent<PuzzleLevelOverrideRegistry>();

            LoadPreconfiguredOverrides(registry, manager);

            // Create preloader from manager's config
            if (manager == null || manager._preloaderConfig == null) return;
            if (!FindObjectOfType<PuzzleLevelPreloaderSetup>()) return;
            PuzzleLevelPreloaderSetup.CreateFromConfig(
                manager._preloaderConfig);
        }

        private static void LoadPreconfiguredOverrides(
            PuzzleLevelOverrideRegistry registry,
            PuzzleLevelManager manager)
        {
            // Prefer manager's bound config, fall back to Resources
            var config = manager?._overrideConfig;
            if (config == null)
            {
                config = Resources.Load<PuzzleLevelOverrideConfig>(
                    OverrideConfigResourcePath);
            }

            if (config == null)
            {
                return;
            }

            foreach (PuzzleLevelOverrideEntry entry in config.Entries)
            {
                if (string.IsNullOrEmpty(entry.LevelId)
                    || entry.OverrideAsset == null)
                {
                    continue;
                }

                var data = new PuzzleLevelData(
                    entry.LevelId,
                    entry.OverrideAsset,
                    entry.DataType
                );
                registry.SetOverride(entry.LevelId, data);
            }
        }

        #endregion

        #region Unity Callbacks

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        #endregion

        #region Public Methods
        public ResourceLoadingHandler<TextAsset> LoadResouceAsync(int v)
        {
            return LoadResouceAsync(new PuzzleLevelId()
            {
                ResourceId = v.ToString()
            });
        }

        public void InitializeManager(float timeout = float.MaxValue)
        {
            Initialize(timeout);
        }

        public IEnumerator InitializeManagerCoroutine(
            float timeout = float.MaxValue)
        {
            yield return InitializeCoroutine(timeout);
        }

        public ResourceLoadingHandler<IPuzzleLevelData> GetLevelAsync(
            string levelId)
        {
            ResourceLoadingHandler<IPuzzleLevelData> handler
                = new ResourceLoadingHandler<IPuzzleLevelData>();
            PerformGetLevelCoroutine(levelId, handler)
                .DispatchOnDispatcher();
            return handler;
        }

        public PuzzleLevelOverrideRegistry GetOverrideRegistry()
        {
            if (_overrideRegistry != null)
            {
                return _overrideRegistry;
            }

            _overrideRegistry
                = Object.FindObjectOfType<PuzzleLevelOverrideRegistry>();
            return _overrideRegistry;
        }

        #endregion

        #region Private Methods — Loading

        private IEnumerator PerformGetLevelCoroutine(
            string levelId,
            ResourceLoadingHandler<IPuzzleLevelData> handler
        )
        {
            handler.LoadingStatus = LoadingStatus.Initiating;
            handler.ResourceStatus = ResourceStatus.Unknown;

            if (string.IsNullOrEmpty(levelId))
            {
                FailHandler(
                    handler, new ArgumentNullException(nameof(levelId)));
                yield break;
            }

            PuzzleLevelOverrideRegistry registry = GetOverrideRegistry();
            if (registry != null)
            {
                IPuzzleLevelData overrideLevel = registry.TryGet(levelId);
                if (overrideLevel != null)
                {
                    CompleteHandler(handler, overrideLevel, "Override");
                    QuickLog.Debug<PuzzleLevelManager>(
                        "Override hit for level '{0}'.", levelId);
                    yield break;
                }
            }

            yield return LoadViaProviderChain(levelId, handler);
        }

        private IEnumerator LoadViaProviderChain(
            string levelId,
            ResourceLoadingHandler<IPuzzleLevelData> handler
        )
        {
            IAsyncResourceId resourceId
                = new PuzzleLevelId { ResourceId = levelId };
            ResourceLoadingHandler<TextAsset> assetHandler
                = LoadResouceAsync(resourceId);

            yield return new WaitUntil(
                () => assetHandler.LoadingStatus == LoadingStatus.Completed);

            if (assetHandler.ResourceStatus != ResourceStatus.Loaded
                || assetHandler.Resouce == null)
            {
                FailHandler(
                    handler,
                    assetHandler.Exception
                    ?? new InvalidOperationException(
                        $"Failed to load level '{levelId}'."));
                yield break;
            }

            DataType dataType = ResolveDataType(levelId);
            PuzzleLevelData levelData = new PuzzleLevelData(
                levelId, assetHandler.Resouce, dataType);

            CompleteHandler(handler, levelData, assetHandler.ProviderSource);
        }

        private DataType ResolveDataType(string levelId)
        {
            foreach (IAsyncResourceProvider<TextAsset> provider in Providers)
            {
                if (provider is ICatalogAwareAsyncResourceProvider catalogProvider
                    && catalogProvider.HasResource(new PuzzleLevelId { ResourceId = levelId }))
                {
                    return catalogProvider.GetDataType(levelId);
                }
            }

            return DataType.Unknown;
        }

        #endregion

        #region Private Methods — Handler Helpers

        private static void CompleteHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            IPuzzleLevelData data,
            string providerSource
        )
        {
            handler.Resouce = data;
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Loaded;
            handler.ProviderSource = providerSource;
        }

        private static void FailHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            Exception exception
        )
        {
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = exception;
        }

        #endregion

        #region Nested Types

        #endregion
    }
}
