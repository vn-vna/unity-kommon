using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.ItemDatabase.Middlewares;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Singleton director for the Item Database framework.
    /// Owns the in-memory engine, the commit queue, the middleware pipeline
    /// and the DataSync-backed syncer. Only created when a configuration
    /// asset exists; otherwise the module stays disabled.
    /// </summary>
    [AddComponentMenu("Scheherazade/Item Database Director")]
    [DontDestroyOnLoad]
    public class ItemDatabaseDirector : SingletonBehavior<ItemDatabaseDirector>
    {
        #region Constants

        private const string SaveKey = "item_db";

        private const int AutoSyncDebounceMs = 500;

        #endregion

        #region Static Init

        private static readonly TaskCompletionSource<bool> _readySource
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task<bool> ReadyTask => _readySource.Task;

        #endregion

        #region Properties

        public ItemDatabaseEngine Engine => _engine;

        public ItemDatabaseSyncer Syncer => _syncer;

        public InventoryMiddlewarePipeline MiddlewarePipeline => _pipeline;

        #endregion

        #region Private Fields

        private ItemDatabaseConfiguration _config;
        private ItemDatabaseEngine _engine;
        private ItemDatabaseCommitQueue _commitQueue;
        private ItemDatabaseSyncer _syncer;
        private InventoryMiddlewarePipeline _pipeline;
        private Dictionary<string, ItemDefinition> _definitionLookup;

        #endregion

        #region Unity Callbacks

        protected override async void Awake()
        {
            base.Awake();

            try
            {
                _config = ItemDatabaseConfiguration.Instance;
                if (_config == null)
                {
                    QuickLog.Info<ItemDatabaseDirector>(
                        "ItemDatabase disabled — no configuration found.");
                    return;
                }

                _definitionLookup = _config.BuildDefinitionLookup();
                _pipeline = InventoryMiddlewarePipeline.FromConfig(_config);
                _engine = new ItemDatabaseEngine();
                _commitQueue = new ItemDatabaseCommitQueue(_engine);
                _commitQueue.Start();
                _syncer = new ItemDatabaseSyncer(_engine, SaveKey);

                // Wait for DataSync, load, hydrate, then start the auto-sync loop.
                var state = await _syncer.LoadAsync();
                _engine.Hydrate(state);
                _syncer.StartAutoSync(AutoSyncDebounceMs);

                QuickLog.Info<ItemDatabaseDirector>(
                    "ItemDatabase ready — {0} definitions, {1} items loaded.",
                    _definitionLookup.Count, _engine.Count);
            }
            catch (Exception ex)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "ItemDatabase initialization failed: {0}", ex);
            }
            finally
            {
                _readySource.TrySetResult(true);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _commitQueue?.Stop();
            _syncer?.Stop();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause) _ = _syncer?.ForceSyncAsync();
        }

        private void OnApplicationQuit()
        {
            _ = _syncer?.ForceSyncAsync();
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (ItemDatabaseConfiguration.Instance == null)
            {
                QuickLog.Info<ItemDatabaseDirector>(
                    "ItemDatabase disabled — no configuration found.");
                _readySource.TrySetResult(false);
                return;
            }

            var go = new GameObject("[Scheherazade ItemDatabase Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<ItemDatabaseDirector>();
        }

        #endregion

        #region Public Methods

        public ItemDefinition GetDefinition(string itemId)
        {
            if (_definitionLookup == null || string.IsNullOrEmpty(itemId)) return null;
            _definitionLookup.TryGetValue(itemId, out var def);
            return def;
        }

        public async Task AddItemAsync(InventoryItem item, CancellationToken ct = default)
        {
            await ReadyTask;
            if (_engine == null) return;

            // BEFORE pass (caller thread) — may throw ItemAlreadyStackedException
            try { _pipeline.BeforeAdd(item); }
            catch (ItemAlreadyStackedException)
            {
                // AutoStackMiddleware merged into an existing batch — success.
                return;
            }

            await _commitQueue.EnqueueAsync(new AddOperation { Item = item });
            _pipeline.AfterAdd(item);
        }

        public async Task RemoveItemAsync(string key, CancellationToken ct = default)
        {
            await ReadyTask;
            if (_engine == null) return;

            _pipeline.BeforeRemove(key);
            await _commitQueue.EnqueueAsync(new RemoveOperation { Key = key });
            _pipeline.AfterRemove(key);
        }

        public async Task SetTagAsync(string key, Type tagDefType, ITagData data, CancellationToken ct = default)
        {
            await ReadyTask;
            if (_engine == null) return;

            _pipeline.BeforeSetTag(key, tagDefType, data);
            await _commitQueue.EnqueueAsync(new SetTagOperation { Key = key, TagDefType = tagDefType, Data = data });
            _pipeline.AfterSetTag(key, tagDefType, data);
        }

        public async Task RemoveTagAsync(string key, Type tagDefType, CancellationToken ct = default)
        {
            await ReadyTask;
            if (_engine == null) return;

            await _commitQueue.EnqueueAsync(new RemoveTagOperation { Key = key, TagDefType = tagDefType });
        }

        #endregion
    }
}
