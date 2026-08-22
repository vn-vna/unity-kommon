using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.DataSync;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Event-driven persistence bridge. Saves are single-flight and
    /// acknowledge only the exact mutation version captured by the saved
    /// snapshot, so a concurrent mutation can never be marked persisted.
    /// </summary>
    public class ItemDatabaseSyncer
    {
        private const int MaxDirtyDurationMs = 5000;

        private readonly ItemDatabaseEngine _engine;
        private readonly string _saveKey;
        private readonly string _legacyBackupKey;
        private readonly CancellationTokenSource _lifetimeSource
            = new CancellationTokenSource();
        private readonly SemaphoreSlim _saveGate = new SemaphoreSlim(1, 1);
        private readonly object _debounceLock = new object();
        private readonly HashSet<Task> _debounceTasks = new HashSet<Task>();

        private CancellationTokenSource _debounceSource;
        private byte[] _legacySourcePayload;
        private int _debounceMs;
        private int _debounceGeneration;
        private int _consecutiveFailures;
        private long _dirtySinceTimestamp;
        private bool _started;
        private bool _stopped;

        internal ItemDatabaseSyncer(
            ItemDatabaseEngine engine,
            string saveKey,
            string legacyBackupKey)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _saveKey = string.IsNullOrWhiteSpace(saveKey)
                ? throw new ArgumentException("Save key is required.", nameof(saveKey))
                : saveKey;
            _legacyBackupKey = string.IsNullOrWhiteSpace(legacyBackupKey)
                ? throw new ArgumentException(
                    "Legacy backup key is required.",
                    nameof(legacyBackupKey)
                )
                : legacyBackupKey;
            _debounceMs = 500;
        }

        internal void StartAutoSync(int debounceMs = 500)
        {
            if (_started || _stopped) return;

            _debounceMs = Math.Max(50, debounceMs);
            _started = true;
            _engine.MutationCommitted += HandleMutationCommitted;
            if (_engine.IsDirty)
            {
                MarkDirtyStarted();
                ScheduleDebouncedSync(_debounceMs);
            }
        }

        internal void Stop()
        {
            if (_stopped) return;

            _stopped = true;
            _engine.MutationCommitted -= HandleMutationCommitted;
            _lifetimeSource.Cancel();
            lock (_debounceLock)
            {
                _debounceSource?.Cancel();
            }
        }

        internal async Task StopAsync()
        {
            Stop();
            Task[] debounceTasks;
            lock (_debounceLock)
            {
                debounceTasks = new Task[_debounceTasks.Count];
                _debounceTasks.CopyTo(debounceTasks);
            }

            if (debounceTasks.Length == 0) return;
            try
            {
                await Task.WhenAll(debounceTasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        internal async Task<ItemDatabaseState> LoadAsync(
            CancellationToken ct = default)
        {
            DataSyncDirector director = await WaitForDataSyncDirectorAsync(ct);
            bool saveExists = await director.ExistsStrictAsync(_saveKey, ct);
            ct.ThrowIfCancellationRequested();
            if (!saveExists)
            {
                QuickLog.Info<ItemDatabaseSyncer>(
                    "No existing save found — starting with empty inventory"
                );
                return new ItemDatabaseState();
            }

            try
            {
                DataSyncLoadResult<ItemDatabaseState> loadResult = await director
                    .LoadStrictWithPayloadAsync<ItemDatabaseState>(
                        _saveKey,
                        ct
                    );
                ItemDatabaseState state = loadResult.Data;

                state ??= new ItemDatabaseState();
                state.items ??= new System.Collections.Generic.List<InventoryItem>();
                state.tags ??= new System.Collections.Generic.List<InventoryTagEntry>();
                state.quarantine ??=
                    new System.Collections.Generic.List<QuarantinedInventoryItem>();
                if (state.requiresLegacyBackup)
                {
                    _legacySourcePayload = loadResult.Payload;
                    if (_legacySourcePayload == null)
                    {
                        throw new ItemDatabaseException(
                            "DataSync did not expose the exact payload selected for "
                            + "Item Database migration. Initialization was aborted."
                        );
                    }
                }
                else
                {
                    _legacySourcePayload = null;
                }

                QuickLog.Info<ItemDatabaseSyncer>(
                    "Loaded {0} items, {1} tags from persistent storage",
                    state.items.Count,
                    state.tags.Count
                );
                return state;
            }
            catch (SaveNotFoundException exception)
            {
                throw new ItemDatabaseException(
                    $"Item Database save '{_saveKey}' exists but could not be decoded "
                    + $"or migrated. Original data was left untouched. {exception.Message}"
                );
            }
        }

        internal async Task ForceSyncAsync(
            CancellationToken ct = default)
        {
            long targetVersion = _engine.MutationVersion;
            while (_engine.AcknowledgedVersion < targetVersion)
            {
                ct.ThrowIfCancellationRequested();
                await SyncOnceAsync(ct, ct);
            }
        }

        private void HandleMutationCommitted(long version)
        {
            if (_stopped) return;
            MarkDirtyStarted();
            ScheduleDebouncedSync(_debounceMs);
        }

        private void ScheduleDebouncedSync(
            int delayMs,
            bool enforceMaximumDirtyDelay = true)
        {
            lock (_debounceLock)
            {
                if (_stopped) return;

                _debounceSource?.Cancel();
                _debounceSource?.Dispose();
                _debounceSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeSource.Token
                );
                int generation = ++_debounceGeneration;
                Task debounceTask = RunDebouncedSyncAsync(
                    generation,
                    enforceMaximumDirtyDelay
                        ? GetBoundedDelay(delayMs)
                        : Math.Max(0, delayMs),
                    _debounceSource.Token
                );
                _debounceTasks.Add(debounceTask);
                _ = RemoveDebounceTaskWhenCompleteAsync(debounceTask);
            }
        }

        private async Task RemoveDebounceTaskWhenCompleteAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseSyncer>(
                    "Debounced sync task faulted outside its retry loop: {0}",
                    exception
                );
            }
            finally
            {
                lock (_debounceLock)
                {
                    _debounceTasks.Remove(task);
                }
            }
        }

        private async Task RunDebouncedSyncAsync(
            int generation,
            int delayMs,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delayMs, cancellationToken);
                if (!_engine.IsDirty) return;

                await SyncOnceAsync(
                    cancellationToken,
                    _lifetimeSource.Token
                );
                _consecutiveFailures = 0;

                lock (_debounceLock)
                {
                    if (!_engine.IsDirty)
                    {
                        Interlocked.Exchange(ref _dirtySinceTimestamp, 0);
                    }

                    if (generation == _debounceGeneration && _engine.IsDirty)
                    {
                        ScheduleDebouncedSync(_debounceMs);
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested
                    || _lifetimeSource.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 6);
                int retryDelay = Math.Min(
                    _debounceMs * (1 << _consecutiveFailures),
                    30000
                );
                QuickLog.Error<ItemDatabaseSyncer>(
                    "Sync failed; retrying in {0} ms: {1}",
                    retryDelay,
                    exception
                );

                lock (_debounceLock)
                {
                    if (generation == _debounceGeneration && !_stopped)
                    {
                        ScheduleDebouncedSync(
                            retryDelay,
                            enforceMaximumDirtyDelay: false
                        );
                    }
                }
            }
        }

        private async Task SyncOnceAsync(
            CancellationToken waitCancellationToken,
            CancellationToken writeCancellationToken)
        {
            await _saveGate.WaitAsync(waitCancellationToken);
            try
            {
                if (!_engine.IsDirty) return;

                ItemDatabaseSnapshot snapshot = _engine.CaptureSnapshot();
                bool backupCompleted = await EnsureLegacyBackupAsync(
                    snapshot.State,
                    writeCancellationToken
                );
                if (backupCompleted)
                {
                    snapshot.State.requiresLegacyBackup = false;
                }

                QuickLog.Debug<ItemDatabaseSyncer>(
                    "Syncing {0} items, {1} tags → DataSync key '{2}'",
                    snapshot.State.items.Count,
                    snapshot.State.tags.Count,
                    _saveKey
                );

                DataSyncDirector director = await WaitForDataSyncDirectorAsync(
                    writeCancellationToken
                );
                await director.SaveStrictAsync(
                    _saveKey,
                    snapshot.State,
                    writeCancellationToken
                );

                _engine.AcknowledgePersisted(snapshot.Version, backupCompleted);
                if (backupCompleted) _legacySourcePayload = null;
                if (!_engine.IsDirty)
                {
                    Interlocked.Exchange(ref _dirtySinceTimestamp, 0);
                }
                QuickLog.Info<ItemDatabaseSyncer>(
                    "Sync complete — {0} items persisted at revision {1}",
                    snapshot.State.items.Count,
                    snapshot.Version
                );
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private async Task<bool> EnsureLegacyBackupAsync(
            ItemDatabaseState state,
            CancellationToken cancellationToken)
        {
            if (state == null || !state.requiresLegacyBackup) return false;

            DataSyncDirector director = await WaitForDataSyncDirectorAsync(
                cancellationToken
            );
            if (_legacySourcePayload == null)
            {
                throw new ItemDatabaseException(
                    "The exact legacy payload is unavailable. V2 save was aborted."
                );
            }

            byte[] originalBytes = (byte[])_legacySourcePayload.Clone();
            await director.EnsureExactPayloadStrictAsync(
                _legacyBackupKey,
                originalBytes,
                cancellationToken
            );

            QuickLog.Info<ItemDatabaseSyncer>(
                "Verified legacy backup at DataSync key '{0}'",
                _legacyBackupKey
            );
            return true;
        }

        private static async Task<DataSyncDirector> WaitForDataSyncDirectorAsync(
            CancellationToken cancellationToken)
        {
            await ItemDatabaseTaskUtility.WaitAsync(
                DataSyncDirector.ReadyTask,
                cancellationToken
            );

            const int maxYieldCount = 300;
            for (int i = 0; i < maxYieldCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DataSyncDirector.Instance != null) return DataSyncDirector.Instance;
                await Task.Yield();
            }

            throw new ItemDatabaseException(
                "DataSync reported ready but no active DataSyncDirector exists."
            );
        }

        private void MarkDirtyStarted()
        {
            long now = Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref _dirtySinceTimestamp, now, 0);
        }

        private int GetBoundedDelay(int requestedDelayMs)
        {
            long dirtySince = Interlocked.Read(ref _dirtySinceTimestamp);
            if (dirtySince == 0) return Math.Max(0, requestedDelayMs);

            double elapsedMilliseconds = (Stopwatch.GetTimestamp() - dirtySince)
                * 1000d / Stopwatch.Frequency;
            int remainingMilliseconds = Math.Max(
                0,
                MaxDirtyDurationMs - (int)elapsedMilliseconds
            );
            return Math.Min(
                Math.Max(0, requestedDelayMs),
                remainingMilliseconds
            );
        }

    }
}
