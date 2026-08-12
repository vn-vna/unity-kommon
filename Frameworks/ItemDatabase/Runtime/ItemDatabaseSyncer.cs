using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.DataSync;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Sync Layer: bridges the dirty engine state to the DataSync
    /// persistent layer. A debounced background loop snapshots the engine
    /// and persists through DataSyncDirector; lifecycle events force an
    /// immediate sync.
    /// </summary>
    public class ItemDatabaseSyncer
    {
        private readonly ItemDatabaseEngine _engine;
        private readonly string _saveKey;
        private readonly CancellationTokenSource _cts
            = new CancellationTokenSource();
        private Task _syncLoop;
        private int _debounceMs;

        internal ItemDatabaseSyncer(ItemDatabaseEngine engine, string saveKey)
        {
            _engine = engine;
            _saveKey = saveKey;
            _debounceMs = 500;
        }

        internal void StartAutoSync(int debounceMs = 500)
        {
            _debounceMs = debounceMs;
            _syncLoop = SyncLoopAsync(_cts.Token);
        }

        internal void Stop() { _cts.Cancel(); }

        internal async Task<ItemDatabaseState> LoadAsync(
            CancellationToken ct = default)
        {
            await DataSyncDirector.ReadyTask;

            try
            {
                var state = await DataSyncDirector.Instance
                    .LoadAsync<ItemDatabaseState>(_saveKey, ct);

                QuickLog.Info<ItemDatabaseSyncer>(
                    "Loaded {0} items, {1} tags from persistent storage",
                    state.items.Count, state.tags.Count);
                return state;
            }
            catch (SaveNotFoundException)
            {
                QuickLog.Info<ItemDatabaseSyncer>(
                    "No existing save found — starting with empty inventory");
                return new ItemDatabaseState();
            }
        }

        internal async Task ForceSyncAsync(
            CancellationToken ct = default)
        {
            await SyncOnceAsync(ct);
        }

        private async Task SyncLoopAsync(
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    while (!_engine.IsDirty && !ct.IsCancellationRequested)
                        await Task.Delay(100, ct);
                    if (ct.IsCancellationRequested) break;

                    await Task.Delay(_debounceMs, ct);
                    if (!_engine.IsDirty || ct.IsCancellationRequested) continue;

                    await SyncOnceAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    QuickLog.Error<ItemDatabaseSyncer>("Sync loop error: {0}", ex);
                }
            }
        }

        private async Task SyncOnceAsync(
            CancellationToken ct)
        {
            try
            {
                var state = _engine.Snapshot();

                QuickLog.Debug<ItemDatabaseSyncer>(
                    "Syncing {0} items, {1} tags → DataSync key '{2}'",
                    state.items.Count, state.tags.Count, _saveKey);

                await DataSyncDirector.Instance
                    .SaveAsync(_saveKey, state, ct);

                _engine.ClearDirty();
                QuickLog.Info<ItemDatabaseSyncer>(
                    "Sync complete — {0} items persisted", state.items.Count);
            }
            catch (Exception ex)
            {
                QuickLog.Error<ItemDatabaseSyncer>("Sync failed: {0}", ex);
                throw;
            }
        }
    }
}
