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

        private const string LegacyBackupKey = "item_db.v1.backup";

        private const int AutoSyncDebounceMs = 500;

        private const int QuitFlushTimeoutMs = 5000;

        #endregion

        #region Events & Delegates

        public static event Action<ItemDatabaseLifecycleState> LifecycleStateChanged;

        #endregion

        #region Interfaces & Properties

        private static readonly object StaticStateLock = new object();

        private static TaskCompletionSource<bool> _readySource = CreateReadySource();

        private static TaskCompletionSource<ItemDatabaseInitializationResult>
            _initializationSource = CreateInitializationSource();

        private static ItemDatabaseLifecycleState _state
            = ItemDatabaseLifecycleState.Uninitialized;

        private static Exception _initializationException;

        public static Task<bool> ReadyTask => _readySource.Task;

        public static Task<ItemDatabaseInitializationResult> InitializationTask
            => _initializationSource.Task;

        public static ItemDatabaseLifecycleState State => _state;

        public static Exception InitializationException => _initializationException;

        public ItemDatabaseEngine Engine => _engine;

        public ItemDatabaseSyncer Syncer => _syncer;

        public InventoryMiddlewarePipeline MiddlewarePipeline => _pipeline;

        public bool IsReady => _state == ItemDatabaseLifecycleState.Ready
            && _engine != null;

        #endregion

        #region Private Fields

        private ItemDatabaseConfiguration _config;
        private ItemDatabaseEngine _engine;
        private ItemDatabaseCommitQueue _commitQueue;
        private ItemDatabaseSyncer _syncer;
        private InventoryMiddlewarePipeline _pipeline;
        private Dictionary<string, ItemDefinition> _definitionLookup;
        private ItemDatabaseExpiryScheduler _expiryScheduler;
        private CancellationTokenSource _lifetimeSource;
        private Task _initializationTask;
        private Task _shutdownTask;
        private bool _isStopping;
        private bool _quitRequested;
        private bool _allowQuit;

        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();

            if (Instance != this) return;

            _lifetimeSource = new CancellationTokenSource();
            Application.wantsToQuit += HandleWantsToQuit;
            SetState(ItemDatabaseLifecycleState.Initializing);
            _initializationTask = InitializeAsync(_lifetimeSource.Token);
        }

        protected override void OnDestroy()
        {
            Application.wantsToQuit -= HandleWantsToQuit;
            if (Instance == this)
            {
                _isStopping = true;
                _lifetimeSource?.Cancel();
                SetState(ItemDatabaseLifecycleState.Stopping);
                _shutdownTask ??= ShutdownAsync();
            }

            base.OnDestroy();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                _ = FlushAsync();
                return;
            }

            _ = ExpireNowAsync();
        }

        private void OnApplicationQuit()
        {
            _ = FlushAsync();
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            if (ItemDatabaseConfiguration.LoadCanonical() == null)
            {
                QuickLog.Info<ItemDatabaseDirector>(
                    "ItemDatabase disabled — no configuration found.");
                CompleteInitialization(ItemDatabaseLifecycleState.Disabled);
                return;
            }

            var go = new GameObject("[Scheherazade ItemDatabase Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<ItemDatabaseDirector>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lock (StaticStateLock)
            {
                _readySource = CreateReadySource();
                _initializationSource = CreateInitializationSource();
                _state = ItemDatabaseLifecycleState.Uninitialized;
                _initializationException = null;
                LifecycleStateChanged = null;
            }
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
            ItemOperationResult result = await AddItemAsync(
                AddItemRequest.FromInventoryItem(item),
                ct
            );
            result.ThrowIfRejected();
        }

        public async Task<ItemOperationResult> AddItemAsync(
            AddItemRequest request,
            CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await _commitQueue.EnqueueAsync(new AddOperation(
                request,
                _pipeline,
                ct
            ));
        }

        public async Task RemoveItemAsync(string key, CancellationToken ct = default)
        {
            ItemOperationResult result = await TryRemoveItemAsync(key, ct);
            result.ThrowIfRejected();
        }

        public async Task<ItemOperationResult> TryRemoveItemAsync(
            string key,
            CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await _commitQueue.EnqueueAsync(new RemoveOperation(
                key,
                _pipeline,
                ct
            ));
        }

        public async Task SetTagAsync(string key, Type tagDefType, ITagData data, CancellationToken ct = default)
        {
            ItemOperationResult result = await TrySetTagAsync(
                key,
                tagDefType,
                data,
                ct
            );
            result.ThrowIfRejected();
        }

        public async Task<ItemOperationResult> TrySetTagAsync(
            string key,
            Type tagDefinitionType,
            ITagData data,
            CancellationToken ct = default)
        {
            CapturedTagData capturedData = ItemTagDataSerializer.Capture(data);
            if (capturedData.TagDefinitionType != tagDefinitionType)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidTagData,
                    $"Payload '{data.GetType().Name}' does not map to "
                    + $"'{tagDefinitionType?.Name ?? "null"}'."
                );
            }

            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await _commitQueue.EnqueueAsync(new SetTagOperation(
                key,
                tagDefinitionType,
                capturedData,
                _pipeline,
                ct
            ));
        }

        public async Task RemoveTagAsync(string key, Type tagDefType, CancellationToken ct = default)
        {
            ItemOperationResult result = await TryRemoveTagAsync(key, tagDefType, ct);
            result.ThrowIfRejected();
        }

        public async Task<ItemOperationResult> TryRemoveTagAsync(
            string key,
            Type tagDefinitionType,
            CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await _commitQueue.EnqueueAsync(new RemoveTagOperation(
                key,
                tagDefinitionType,
                ct
            ));
        }

        public async Task<ItemOperationResult> RemoveQuantityAsync(
            string key,
            int quantity,
            CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await _commitQueue.EnqueueAsync(new RemoveQuantityOperation(
                key,
                quantity,
                ct
            ));
        }

        public async Task<ItemOperationResult> ExpireNowAsync(
            CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return CreateUnavailableResult();
            return await ExpireNowInternalAsync(ct);
        }

        public async Task ForceSyncAsync(CancellationToken ct = default)
        {
            if (!await WaitUntilReadyAsync(ct)) return;

            ItemOperationResult barrier = await _commitQueue.DrainAsync(ct);
            barrier.ThrowIfRejected();
            await _syncer.ForceSyncAsync(ct);
        }

        #endregion

        #region Private Methods

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            try
            {
                _config = ItemDatabaseConfiguration.LoadCanonical();
                if (_config == null)
                {
                    QuickLog.Info<ItemDatabaseDirector>(
                        "ItemDatabase disabled — no configuration found."
                    );
                    CompleteInitialization(ItemDatabaseLifecycleState.Disabled);
                    return;
                }

                _definitionLookup = _config.BuildDefinitionLookup();
                _pipeline = InventoryMiddlewarePipeline.FromConfig(_config);
                _engine = new ItemDatabaseEngine(_definitionLookup);
                _commitQueue = new ItemDatabaseCommitQueue(_engine);
                _commitQueue.Start();
                _syncer = new ItemDatabaseSyncer(
                    _engine,
                    SaveKey,
                    LegacyBackupKey
                );

                ItemDatabaseState state = await _syncer.LoadAsync(
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                _engine.Hydrate(state);
                cancellationToken.ThrowIfCancellationRequested();

                _syncer.StartAutoSync(AutoSyncDebounceMs);
                _expiryScheduler = new ItemDatabaseExpiryScheduler(
                    _engine,
                    ExpireNowInternalAsync
                );
                _expiryScheduler.Start();
                cancellationToken.ThrowIfCancellationRequested();
                if (_isStopping) return;

                CompleteInitialization(ItemDatabaseLifecycleState.Ready);
                QuickLog.Info<ItemDatabaseDirector>(
                    "ItemDatabase ready — {0} definitions, {1} items loaded.",
                    _definitionLookup.Count,
                    _engine.Count
                );
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await StopServicesAfterFailedInitializationAsync();
            }
            catch (Exception exception)
            {
                await StopServicesAfterFailedInitializationAsync();
                if (_isStopping) return;

                QuickLog.Error<ItemDatabaseDirector>(
                    "ItemDatabase initialization failed: {0}",
                    exception
                );
                CompleteInitialization(
                    ItemDatabaseLifecycleState.Failed,
                    exception
                );
            }
        }

        private async Task StopServicesAfterFailedInitializationAsync()
        {
            _expiryScheduler?.Stop();
            if (_syncer != null) await _syncer.StopAsync();
            if (_commitQueue != null) await _commitQueue.StopAsync();
        }

        private static TaskCompletionSource<bool> CreateReadySource()
        {
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        private static TaskCompletionSource<ItemDatabaseInitializationResult>
            CreateInitializationSource()
        {
            return new TaskCompletionSource<ItemDatabaseInitializationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        private static void CompleteInitialization(
            ItemDatabaseLifecycleState state,
            Exception exception = null)
        {
            SetState(state, exception);
            _initializationSource.TrySetResult(
                new ItemDatabaseInitializationResult(state, exception)
            );

            switch (state)
            {
                case ItemDatabaseLifecycleState.Ready:
                    _readySource.TrySetResult(true);
                    break;
                case ItemDatabaseLifecycleState.Disabled:
                case ItemDatabaseLifecycleState.Stopped:
                    _readySource.TrySetResult(false);
                    break;
                case ItemDatabaseLifecycleState.Failed:
                    _readySource.TrySetException(
                        exception ?? new ItemDatabaseException(
                            "Item Database initialization failed."
                        )
                    );
                    break;
            }
        }

        private static void SetState(
            ItemDatabaseLifecycleState state,
            Exception exception = null)
        {
            lock (StaticStateLock)
            {
                _state = state;
                _initializationException = exception;
            }

            Action<ItemDatabaseLifecycleState> handlers = LifecycleStateChanged;
            if (handlers == null) return;

            foreach (Action<ItemDatabaseLifecycleState> handler
                     in handlers.GetInvocationList())
            {
                try
                {
                    handler(state);
                }
                catch (Exception callbackException)
                {
                    QuickLog.Error<ItemDatabaseDirector>(
                        "Lifecycle state callback failed for state {0}: {1}",
                        state,
                        callbackException
                    );
                }
            }
        }

        private static async Task<bool> WaitUntilReadyAsync(
            CancellationToken cancellationToken)
        {
            bool ready = await ItemDatabaseTaskUtility.WaitAsync(
                ReadyTask,
                cancellationToken
            );
            return ready
                && State == ItemDatabaseLifecycleState.Ready
                && Instance != null
                && Instance.IsReady;
        }

        private ItemOperationResult CreateUnavailableResult()
        {
            return _config == null
                ? ItemOperationResult.Disabled()
                : ItemOperationResult.Unavailable(
                    $"Item Database is {_state} and cannot accept commands."
                );
        }

        private Task<ItemOperationResult> ExpireNowInternalAsync(
            CancellationToken cancellationToken)
        {
            return _commitQueue.EnqueueAsync(new ExpireItemsOperation(
                DateTime.UtcNow,
                cancellationToken
            ));
        }

        private async Task FlushAsync()
        {
            try
            {
                if (!IsReady) return;
                await ForceSyncAsync();
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "Lifecycle flush failed: {0}",
                    exception
                );
            }
        }

        private bool HandleWantsToQuit()
        {
#if UNITY_EDITOR
            return true;
#else
            if (_allowQuit) return true;
            if (_quitRequested) return false;
            if (!IsReady) return true;

            _quitRequested = true;
            _isStopping = true;
            SetState(ItemDatabaseLifecycleState.Stopping);
            _ = FlushAndQuitAsync();
            return false;
#endif
        }

        private async Task FlushAndQuitAsync()
        {
            using var timeoutSource = new CancellationTokenSource(
                QuitFlushTimeoutMs
            );
            try
            {
                await SealAndSyncAsync(timeoutSource.Token);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "Final quit flush failed: {0}",
                    exception
                );
            }
            finally
            {
                _allowQuit = true;
                Application.Quit();
            }
        }

        private async Task ShutdownAsync()
        {
            try
            {
                if (_initializationTask != null)
                {
                    await _initializationTask;
                }

                _expiryScheduler?.Stop();
                await SealAndSyncAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "Item Database shutdown failed: {0}",
                    exception
                );
            }
            finally
            {
                await StopServicesAsync();
                _lifetimeSource?.Dispose();
                CompleteInitialization(ItemDatabaseLifecycleState.Stopped);
            }
        }

        private async Task SealAndSyncAsync(
            CancellationToken cancellationToken)
        {
            _expiryScheduler?.Stop();
            if (_commitQueue != null)
            {
                ItemOperationResult barrier = await ItemDatabaseTaskUtility
                    .WaitAsync(
                        _commitQueue.SealAndDrainAsync(),
                        cancellationToken
                    );
                barrier.ThrowIfRejected();
            }

            if (_syncer != null)
            {
                await ItemDatabaseTaskUtility.WaitAsync(
                    _syncer.ForceSyncAsync(cancellationToken),
                    cancellationToken
                );
            }
        }

        private async Task StopServicesAsync()
        {
            if (_syncer != null)
            {
                try
                {
                    await _syncer.StopAsync();
                }
                catch (Exception exception)
                {
                    QuickLog.Error<ItemDatabaseDirector>(
                        "Item Database syncer stop failed: {0}",
                        exception
                    );
                }
            }

            if (_commitQueue == null) return;
            try
            {
                await _commitQueue.StopAsync();
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "Item Database commit queue stop failed: {0}",
                    exception
                );
            }
        }

        #endregion
    }
}
