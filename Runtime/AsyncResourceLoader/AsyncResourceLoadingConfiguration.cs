using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [CreateAssetMenu(
        fileName = "AsyncResourceLoaderConfiguration",
        menuName = "Scheherazade/Async Resource Loader/Configuration"
    )]
    public class AsyncResourceLoadingConfiguration :
        SingletonScriptableObject<AsyncResourceLoadingConfiguration>
    {
        public IReadOnlyList<IResourceManager> Managers { get; private set; }
        public IReadOnlyDictionary<Type, IResourceManager> ManagersByType { get; private set; }

        [SerializeField]
        [Tooltip("Resource manager assets managed by this configuration.")]
        private ScriptableObject[] managerAssets;

        [SerializeField]
        [Tooltip("Name of the ticker GameObject spawned at runtime.")]
        private string tickerGameObjectName = "[AsyncResourceLoader Ticker]";

        [SerializeField]
        [Tooltip("Hide the ticker GameObject in the hierarchy.")]
        private bool hideTickerGameObject = true;

        internal string TickerGameObjectName => tickerGameObjectName;
        internal bool HideTickerGameObject => hideTickerGameObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var config = Instance;
            if (config == null)
            {
                QuickLog.Warning<AsyncResourceLoadingConfiguration>(
                    "No AsyncResourceLoadingConfiguration asset found. "
                    + "Create one via Tools > Async Resource Loader."
                );
                return;
            }

            config.Initialize();
            AsyncResourceLoaderTicker.CreateFromConfig(config);
        }

        private void Initialize()
        {
            Managers = managerAssets
                .Where(a => a != null)
                .Select(a => a as IResourceManager)
                .Where(m => m != null)
                .ToList();

            ManagersByType = Managers
                .ToDictionary(m => m.GetType(), m => m);

            foreach (IResourceManager manager in Managers)
            {
                try
                {
                    if (manager is IHasReset resetManager)
                    {
                        resetManager.Reset();
                    }
                }
                catch (Exception ex)
                {
                    QuickLog.Critical<AsyncResourceLoadingConfiguration>(
                        "Failed to reset manager {0}: {1}",
                        manager.GetType().Name, ex
                    );
                }
            }

            QuickLog.Info<AsyncResourceLoadingConfiguration>(
                "Async Resource Loader initialized with {0} manager(s).",
                Managers.Count
            );
        }

        public IResourceManager GetManager(Type managerType)
        {
            if (ManagersByType != null
                && ManagersByType.TryGetValue(managerType, out var manager))
            {
                return manager;
            }

            QuickLog.Warning<AsyncResourceLoadingConfiguration>(
                "Manager of type {0} not found.", managerType.Name
            );
            return null;
        }
    }

    /// <summary>
    /// Optional interface for managers that need explicit Reset before
    /// initialization. Implemented by AsyncResourceManagerBase.
    /// </summary>
    public interface IHasReset
    {
        void Reset();
    }

    /// <summary>
    /// Runtime MonoBehaviour that drives per-frame updates on
    /// resource managers that implement ITickable.
    /// </summary>
    internal sealed class AsyncResourceLoaderTicker : MonoBehaviour
    {
        private AsyncResourceLoadingConfiguration _config;
        private ITickable[] _tickables;

        internal static void CreateFromConfig(
            AsyncResourceLoadingConfiguration config)
        {
            if (config == null) return;

            var go = new GameObject(config.TickerGameObjectName);
            Object.DontDestroyOnLoad(go);
            if (config.HideTickerGameObject)
            {
                go.hideFlags = HideFlags.HideInHierarchy;
            }

            var ticker = go.AddComponent<AsyncResourceLoaderTicker>();
            ticker._config = config;
            ticker.RefreshTickables();
        }

        internal void Setup(AsyncResourceLoadingConfiguration config)
        {
            _config = config;
            RefreshTickables();
        }

        private void RefreshTickables()
        {
            if (_config?.Managers != null)
            {
                _tickables = _config.Managers.OfType<ITickable>().ToArray();
            }
        }

        private void Update()
        {
            if (_tickables == null) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _tickables.Length; i++)
            {
                _tickables[i].Tick(dt);
            }
        }
    }

    /// <summary>
    /// Implement on resource managers that need per-frame updates.
    /// </summary>
    public interface ITickable
    {
        void Tick(float deltaTime);
    }
}
