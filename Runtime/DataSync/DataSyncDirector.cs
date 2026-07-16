using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [AddComponentMenu("Scheherazade/Data Sync Director")]
    [DontDestroyOnLoad]
    public class DataSyncDirector : SingletonBehavior<DataSyncDirector>
    {
        #region Constants

        private const string ConfigPath =
            "Integration/Managers/DataSyncConfiguration";

        #endregion

        #region Static Init

        /// <summary>
        /// Completes when the director has finished Awake() and is ready
        /// to serve requests. Code that may execute before the bootstrap
        /// finishes can await this to avoid premature calls.
        /// </summary>
        private static readonly TaskCompletionSource<bool> _readySource =
            new TaskCompletionSource<bool>();

        /// <summary>
        /// Task that completes when the DataSyncDirector is fully initialized.
        /// </summary>
        public static Task ReadyTask => _readySource.Task;

        #endregion

        #region Per-Key Write Ordering

        /// <summary>
        /// One semaphore per key ensures that operations on the same key
        /// are serialized (writes never overlap, reads never race with writes).
        /// </summary>
        private static readonly ConcurrentDictionary<
            string, SemaphoreSlim
        > _keySemaphores = new ConcurrentDictionary<
            string, SemaphoreSlim
        >();

        /// <summary>
        /// Runs an async operation under a per-key lock so that
        /// concurrent calls for the same key are serialized.
        /// </summary>
        private static async Task RunWithKeyLockAsync(
            string key,
            Func<Task> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(
                key, _ => new SemaphoreSlim(1, 1)
            );

            await semaphore.WaitAsync();
            try
            {
                await operation();
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Runs an async operation that returns a value under a per-key lock.
        /// </summary>
        private static async Task<T> RunWithKeyLockAsync<T>(
            string key,
            Func<Task<T>> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(
                key, _ => new SemaphoreSlim(1, 1)
            );

            await semaphore.WaitAsync();
            try
            {
                return await operation();
            }
            finally
            {
                semaphore.Release();
            }
        }

        #endregion

        #region Private Fields

        private DataSyncConfiguration _config;
        private ISaveAdapter[][] _saveOrderGroups;
        private ISaveAdapter[][] _loadOrderGroups;
        private List<ISaveTranslator> _translators;
        #endregion

        #region Unity Callbacks

        protected override async void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<DataSyncConfiguration>(
                    ConfigPath
                );

                if (_config != null)
                {
                    _saveOrderGroups =
                        _config.ResolveSaveOrderGroups();
                    _loadOrderGroups =
                        _config.ResolveLoadOrderGroups();
                    _translators = _config.Translators
                                       ?.Where(t => t != null)
                                       .ToList()
                                   ?? new List<ISaveTranslator>();

                    // Initialize and filter adapters
                    await InitializeAndFilterAsync();
                }
                else
                {
                    var localAdapter =
                        ScriptableObject
                            .CreateInstance<LocalSaveAdapter>();
                    _saveOrderGroups = new[]
                        { new ISaveAdapter[] { localAdapter } };
                    _loadOrderGroups = new[]
                        { new ISaveAdapter[] { localAdapter } };
                    _translators =
                        new List<ISaveTranslator>
                        {
                            ScriptableObject
                                .CreateInstance<UnityJsonTranslator>()
                        };
                }
            }
            finally
            {
                // Signal readiness even if init partially failed —
                // the configured values (or fallbacks) are now set.
                _readySource.TrySetResult(true);
            }
        }

        private async Task InitializeAndFilterAsync()
        {
            // Collect unique adapters from both orders
            var allAdapters = new HashSet<ISaveAdapter>();
            foreach (ISaveAdapter[] group in _saveOrderGroups)
            foreach (ISaveAdapter a in group)
                if (a != null) allAdapters.Add(a);
            foreach (ISaveAdapter[] group in _loadOrderGroups)
            foreach (ISaveAdapter a in group)
                if (a != null) allAdapters.Add(a);

            // Initialize each adapter; log failures
            foreach (ISaveAdapter adapter in allAdapters)
            {
                try
                {
                    bool ok = await adapter.InitializeAsync();
                    QuickLog.Info<DataSyncDirector>(
                        "Adapter '{0}' init: {1}",
                        adapter.AdapterId,
                        ok ? "available" : "unavailable"
                    );
                }
                catch (Exception ex)
                {
                    QuickLog.Error<DataSyncDirector>(
                        "Adapter '{0}' init threw: {1}",
                        adapter.AdapterId, ex.Message
                    );
                }
            }

            // Filter groups — remove unavailable adapters,
            // drop empty groups
            _saveOrderGroups = FilterGroups(_saveOrderGroups);
            _loadOrderGroups = FilterGroups(_loadOrderGroups);
        }

        private static ISaveAdapter[][] FilterGroups(
            ISaveAdapter[][] groups)
        {
            var result = new List<ISaveAdapter[]>();
            foreach (ISaveAdapter[] group in groups)
            {
                var available = new List<ISaveAdapter>();
                foreach (ISaveAdapter a in group)
                {
                    if (a != null && a.IsAvailable)
                        available.Add(a);
                }

                if (available.Count > 0)
                    result.Add(available.ToArray());
            }

            return result.ToArray();
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject(
                "[Scheherazade Data Sync Director]"
            );
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<DataSyncDirector>();
        }

        #endregion

        #region Typed API (public — with per-key lock)

        public Task SaveAsync<T>(
            string key,
            T data,
            CancellationToken ct = default
        ) where T : IVersionedData
        {
            return RunWithKeyLockAsync(
                key,
                () => SaveInternalAsync(key, data, ct)
            );
        }

        public Task<T> LoadAsync<T>(
            string key,
            CancellationToken ct = default
        ) where T : IVersionedData, new()
        {
            return RunWithKeyLockAsync(
                key,
                () => LoadInternalAsync<T>(key, ct)
            );
        }

        public Task DeleteAsync(
            string key,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                () => DeleteInternalAsync(key, ct)
            );
        }

        public Task<bool> ExistsAsync(
            string key,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                () => ExistsInternalAsync(key, ct)
            );
        }

        #endregion

        #region Raw Stream API (public — with per-key lock)

        public Task<Stream> OpenReadStreamAsync(
            string key,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                () => OpenReadStreamInternalAsync(key, ct)
            );
        }

        public Task WriteStreamAsync(
            string key,
            Stream data,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                () => WriteStreamInternalAsync(key, data, ct)
            );
        }

        #endregion

        #region Internal Implementations (no locking — called under per-key lock)

        /// <summary>
        /// Saves data using save-order groups.
        /// For each group, writes to the first adapter that succeeds.
        /// </summary>
        private async Task SaveInternalAsync<T>(
            string key,
            T data,
            CancellationToken ct
        ) where T : IVersionedData
        {
            VersionTag currentVersion =
                VersionRegistry.GetCurrentVersion(typeof(T));
            ISaveTranslator translator = ResolveTranslator();

            if (_saveOrderGroups.Length == 0)
            {
                throw new SaveAdapterException(
                    "none",
                    "No save groups configured"
                );
            }

            using (var ms = new MemoryStream())
            {
                await translator.EncodeAsync(
                    data, currentVersion, ms, ct
                );

                var allErrors = new List<Exception>();

                for (int g = 0; g < _saveOrderGroups.Length; g++)
                {
                    ISaveAdapter[] group = _saveOrderGroups[g];
                    bool groupSucceeded = false;

                    foreach (ISaveAdapter adapter in group)
                    {
                        try
                        {
                            ms.Position = 0;
                            await adapter.WriteAsync(key, ms, ct);
                            groupSucceeded = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            QuickLog.Warning<DataSyncDirector>(
                                "Save group[{0}] adapter '{1}' "
                                + "failed for key '{2}': {3}",
                                g, adapter.AdapterId, key,
                                ex.Message
                            );
                        }
                    }

                    if (!groupSucceeded)
                    {
                        allErrors.Add(
                            new SaveAdapterException(
                                $"group[{g}]",
                                $"All adapters in save group {g} "
                                + $"failed for key '{key}'"
                            )
                        );

                        continue;
                    }
                }

                if (allErrors.Count == _saveOrderGroups.Length)
                {
                    throw new AggregateException(
                        $"All save groups failed for key '{key}'",
                        allErrors
                    );
                }
            }
        }

        /// <summary>
        /// Loads data using load-order groups.
        /// Groups are tried in priority order; within a group
        /// adapters are tried until one returns data.
        /// </summary>
        private async Task<T> LoadInternalAsync<T>(
            string key,
            CancellationToken ct
        ) where T : IVersionedData, new()
        {
            if (_loadOrderGroups.Length == 0)
            {
                throw new SaveAdapterException(
                    "none",
                    "No load groups configured"
                );
            }

            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                ISaveAdapter[] group = _loadOrderGroups[g];

                foreach (ISaveAdapter adapter in group)
                {
                    try
                    {
                        Stream stream =
                            await adapter.OpenReadAsync(key, ct);
                        if (stream == null)
                            continue;

                        using (stream)
                        {
                            DecodeResult decoded =
                                await DecodeStream(stream, ct);
                            Type snapshotType =
                                VersionRegistry.GetSnapshotType(
                                    typeof(T), decoded.Version
                                );
                            ISaveTranslator translator =
                                ResolveTranslator();
                            object snapshot =
                                translator.ConvertTo(
                                    decoded.Data, snapshotType
                                );
                            return (T)VersionRegistry
                                .MigrateToCurrent(
                                    snapshot, typeof(T),
                                    decoded.Version
                                );
                        }
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "Load group[{0}] adapter '{1}' "
                            + "failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }

            throw new SaveNotFoundException(key);
        }

        private async Task DeleteInternalAsync(
            string key,
            CancellationToken ct)
        {
            for (int g = 0; g < _saveOrderGroups.Length; g++)
            {
                foreach (ISaveAdapter adapter in _saveOrderGroups[g])
                {
                    try
                    {
                        await adapter.DeleteAsync(key, ct);
                        break;
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "Delete group[{0}] adapter '{1}' "
                            + "failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }
        }

        private async Task<bool> ExistsInternalAsync(
            string key,
            CancellationToken ct)
        {
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                foreach (ISaveAdapter adapter
                         in _loadOrderGroups[g])
                {
                    try
                    {
                        if (await adapter.ExistsAsync(key, ct))
                            return true;
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "Exists group[{0}] adapter '{1}' "
                            + "failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }

            return false;
        }

        private async Task<Stream> OpenReadStreamInternalAsync(
            string key,
            CancellationToken ct)
        {
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                foreach (ISaveAdapter adapter
                         in _loadOrderGroups[g])
                {
                    try
                    {
                        Stream stream =
                            await adapter.OpenReadAsync(key, ct);
                        if (stream != null)
                            return stream;
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "OpenRead group[{0}] adapter '{1}' "
                            + "failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }

            throw new SaveNotFoundException(key);
        }

        private async Task WriteStreamInternalAsync(
            string key,
            Stream data,
            CancellationToken ct)
        {
            for (int g = 0; g < _saveOrderGroups.Length; g++)
            {
                foreach (ISaveAdapter adapter
                         in _saveOrderGroups[g])
                {
                    try
                    {
                        data.Position = 0;
                        await adapter.WriteAsync(key, data, ct);
                        break;
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "WriteStream group[{0}] adapter '{1}' "
                            + "failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }
        }

        #endregion

        #region Adapter / Translator Resolution

        /// <summary>
        /// Returns the first adapter from the first load group.
        /// For backward compatibility — prefer typed API methods.
        /// </summary>
        public ISaveAdapter ResolveAdapter()
        {
            if (_loadOrderGroups.Length > 0
                && _loadOrderGroups[0].Length > 0)
                return _loadOrderGroups[0][0];

            throw new SaveAdapterException(
                "none",
                "No adapters configured"
            );
        }

        /// <summary>
        /// All adapters across all groups (flattened, deduplicated).
        /// </summary>
        public ISaveAdapter[] ActiveAdapters
            => _saveOrderGroups
                .SelectMany(g => g)
                .Union(_loadOrderGroups.SelectMany(g => g))
                .Distinct()
                .ToArray();

        public ISaveTranslator ResolveTranslator()
        {
            return _translators.FirstOrDefault()
                   ?? throw new TranslationException(
                       "No translators configured"
                   );
        }

        #endregion

        #region Decode Pipeline

        private async Task<DecodeResult> DecodeStream(
            Stream stream,
            CancellationToken ct)
        {
            ISaveTranslator translator = ResolveTranslator();

            try
            {
                return await translator.DecodeAsync(stream, ct);
            }
            catch
            {
                // Fall through to signature scanning
            }

            foreach (ISaveTranslator t in _translators)
            {
                stream.Position = 0;
                if (TryMatchSignature(stream, t.Signature))
                {
                    stream.Position = 0;
                    return await t.DecodeAsync(stream, ct);
                }
            }

            throw new TranslationException(
                "No translator matched the data signature"
            );
        }

        private static bool TryMatchSignature(
            Stream stream,
            byte[] signature)
        {
            if (signature == null || signature.Length == 0)
                return false;

            var buffer = new byte[signature.Length];
            int bytesRead = stream.Read(buffer, 0, signature.Length);
            return bytesRead == signature.Length
                   && buffer.SequenceEqual(signature);
        }

        #endregion
    }
}
