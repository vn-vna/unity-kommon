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
        private const string ConfigPath = "Integration/Managers/DataSyncConfiguration";
        #endregion

        #region Static Init
        private static readonly TaskCompletionSource<bool> _readySource = new TaskCompletionSource<bool>();
        public static Task ReadyTask => _readySource.Task;
        #endregion

        #region Per-Key Write Ordering
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _keySemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        private static async Task RunWithKeyLockAsync(string key, Func<Task> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try { await operation(); }
            finally { semaphore.Release(); }
        }

        private static async Task<T> RunWithKeyLockAsync<T>(string key, Func<Task<T>> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try { return await operation(); }
            finally { semaphore.Release(); }
        }

        #endregion

        #region Private Fields

        private DataSyncConfiguration _config;
        private ISaveAdapter[][] _saveOrderGroups;
        private ISaveAdapter[][] _loadOrderGroups;
        private List<ISaveTranslator> _translators;
        private int _maxSignatureLength;
        #endregion

        #region Unity Callbacks

        protected override async void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<DataSyncConfiguration>(ConfigPath);

                if (_config != null)
                {
                    _saveOrderGroups = _config.ResolveSaveOrderGroups();
                    _loadOrderGroups = _config.ResolveLoadOrderGroups();
                    _translators = _config.Translators
                        ?.Where(t => t != null).ToList()
                        ?? new List<ISaveTranslator>();

                    _maxSignatureLength = _translators.Max(t => t.Signature?.Length ?? 0);

                    await InitializeAndFilterAsync();
                }
                else
                {
                    var localAdapter = ScriptableObject.CreateInstance<LocalSaveAdapter>();
                    _saveOrderGroups = new[] { new ISaveAdapter[] { localAdapter } };
                    _loadOrderGroups = new[] { new ISaveAdapter[] { localAdapter } };
                    _translators = new List<ISaveTranslator> { ScriptableObject.CreateInstance<UnityJsonTranslator>() };
                    _maxSignatureLength = _translators.Max(t => t.Signature?.Length ?? 0);
                }
            }
            finally
            {
                _readySource.TrySetResult(true);
            }
        }

        private async Task InitializeAndFilterAsync()
        {
            var allAdapters = new HashSet<ISaveAdapter>();
            foreach (ISaveAdapter[] group in _saveOrderGroups)
            {
                foreach (ISaveAdapter a in group)
                {
                    if (a != null) allAdapters.Add(a);
                }
            }

            foreach (ISaveAdapter[] group in _loadOrderGroups)
            {
                foreach (ISaveAdapter a in group)
                {
                    if (a != null) allAdapters.Add(a);
                }
            }

            foreach (ISaveAdapter adapter in allAdapters)
            {
                try
                {
                    bool ok = await adapter.InitializeAsync();
                    QuickLog.Info<DataSyncDirector>("Adapter '{0}' init: {1}",
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
                    {
                        available.Add(a);
                    }
                }

                if (available.Count > 0)
                {
                    result.Add(available.ToArray());
                }
            }

            return result.ToArray();
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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

        public Task SaveAsync<T>(string key, T data, CancellationToken ct = default)
            => RunWithKeyLockAsync(key, () => SaveInternalAsync(key, data, ct));

        public Task<T> LoadAsync<T>(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(key, () => LoadInternalAsync<T>(key, ct));

        public Task DeleteAsync(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(key, () => DeleteInternalAsync(key, ct));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(key, () => ExistsInternalAsync(key, ct));

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

        private async Task SaveInternalAsync<T>(
            string key,
            T data,
            CancellationToken ct
        )
        {
            VersionTag currentVersion = VersionRegistry.GetCurrentVersion(typeof(T));
            ISaveTranslator translator = ResolveTranslator();

            QuickLog.Debug<DataSyncDirector>(
                "Save<{0}>('{1}'): version={2}, translator={3}",
                typeof(T).Name, key, currentVersion, translator.FormatId);

            if (_saveOrderGroups.Length == 0)
                throw new SaveAdapterException("none", "No save groups configured");

            using var encodeStream = new MemoryStream();
            await translator.EncodeAsync(
                data, currentVersion, encodeStream, ct
            );
            byte[] encodedBytes = encodeStream.ToArray();

            QuickLog.Debug<DataSyncDirector>(
                "Save<{0}>('{1}'): encoded {2} bytes",
                typeof(T).Name, key, encodedBytes.Length);

            int groupCount = _saveOrderGroups.Length;
            var groupTasks = new Task<(bool success, ISaveAdapter adapter, int groupIndex)>[groupCount];

            for (int g = 0; g < groupCount; g++)
            {
                int groupIndex = g;
                ISaveAdapter[] group = _saveOrderGroups[g];
                groupTasks[g] = TryWriteToGroupAsync(key, encodedBytes, groupIndex, group, ct);
            }

            (bool success, ISaveAdapter adapter, int groupIndex)[] results = await Task.WhenAll(groupTasks);

            var successes = results.Where(r => r.success).ToArray();
            var failures = results.Where(r => !r.success).ToArray();

            if (successes.Length > 0)
            {
                QuickLog.Info<DataSyncDirector>(
                    "Saved key '{0}' → {1}/{2} group(s): [{3}]",
                    key,
                    successes.Length,
                    groupCount,
                    string.Join(", ", successes.Select(s => s.adapter.AdapterId))
                );
            }

            if (failures.Length == groupCount)
            {
                throw new AggregateException(
                    $"All save groups failed for key '{key}'",
                    failures.Select(f =>
                        new SaveAdapterException(
                            $"group[{f.groupIndex}]",
                            $"All adapters in save group {f.groupIndex} failed for key '{key}'"
                        )
                    )
                );
            }

            if (failures.Length > 0)
            {
                QuickLog.Warning<DataSyncDirector>(
                    "Save key '{0}': {1}/{2} group(s) failed — [{3}]",
                    key,
                    failures.Length,
                    groupCount,
                    string.Join(", ", failures.Select(f => $"group[{f.groupIndex}]"))
                );
            }
        }

        private static async Task<(bool success, ISaveAdapter adapter, int groupIndex)> TryWriteToGroupAsync(
            string key, byte[] encodedBytes, int groupIndex,
            ISaveAdapter[] group, CancellationToken ct
        )
        {
            foreach (ISaveAdapter adapter in group)
            {
                try
                {
                    using MemoryStream ms = new MemoryStream(encodedBytes);
                    await adapter.WriteAsync(key, ms, ct);
                    return (true, adapter, groupIndex);
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Save group[{0}] adapter '{1}' failed for key '{2}': {3}",
                        groupIndex, adapter.AdapterId, key,
                        ex.Message
                    );
                }
            }

            return (false, null, groupIndex);
        }

        private async Task<T> LoadInternalAsync<T>(string key, CancellationToken ct)
        {
            if (_loadOrderGroups.Length == 0)
                throw new SaveAdapterException("none", "No load groups configured");

            bool parallel = _config != null && _config.ParallelLoadEnabled;
            QuickLog.Debug<DataSyncDirector>(
                "Load<{0}>('{1}'): groups={2}, parallel={3}, translators={4}",
                typeof(T).Name, key, _loadOrderGroups.Length, parallel, _translators.Count);

            if (!parallel)
                return await LoadInternalSequentialAsync<T>(key, ct);

            return await LoadInternalParallelAsync<T>(key, ct);
        }

        private async Task<T> LoadInternalSequentialAsync<T>(string key, CancellationToken ct)
        {
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                ISaveAdapter[] group = _loadOrderGroups[g];

                for (int a = 0; a < group.Length; a++)
                {
                    ISaveAdapter adapter = group[a];
                    try
                    {
                        QuickLog.Debug<DataSyncDirector>(
                            "Load seq: trying group[{0}] adapter[{1}] '{2}' for key '{3}'",
                            g, a, adapter.AdapterId, key);

                        Stream stream = await adapter.OpenReadAsync(key, ct);
                        if (stream == null)
                        {
                            QuickLog.Debug<DataSyncDirector>(
                                "Load seq: group[{0}] adapter '{1}' returned null stream for key '{2}'",
                                g, adapter.AdapterId, key);
                            continue;
                        }

                        QuickLog.Debug<DataSyncDirector>(
                            "Load seq: group[{0}] adapter '{1}' returned {2} bytes for key '{3}'",
                            g, adapter.AdapterId, stream.Length, key);

                        using (stream)
                        {
                            QuickLog.Debug<DataSyncDirector>(
                                "Load seq: decoding stream for key '{0}'", key);

                            DecodeResult decoded = await DecodeStream(stream, ct);

                            QuickLog.Debug<DataSyncDirector>(
                                "Load seq: decoded version={0}, dataType={1} for key '{2}'",
                                decoded.Version, decoded.DataType.Name, key);

                            Type snapshotType = VersionRegistry.GetSnapshotType(typeof(T), decoded.Version);
                            QuickLog.Debug<DataSyncDirector>(
                                "Load seq: snapshotType={0} for key '{1}'",
                                snapshotType.Name, key);

                            ISaveTranslator translator = ResolveTranslator();
                            object snapshot = translator.ConvertTo(decoded.Data, snapshotType);

                            QuickLog.Debug<DataSyncDirector>(
                                "Load seq: converted to {0}, migrating for key '{1}'",
                                snapshot?.GetType().Name ?? "null", key);

                            T result = (T)VersionRegistry.MigrateToCurrent(snapshot, typeof(T), decoded.Version);

                            QuickLog.Info<DataSyncDirector>(
                                "Loaded key '{0}' from adapter '{1}' in group [{2}]",
                                key, adapter.AdapterId, g);
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        QuickLog.Warning<DataSyncDirector>(
                            "Load group[{0}] adapter '{1}' failed for key '{2}': {3}",
                            g, adapter.AdapterId, key,
                            ex.Message
                        );
                    }
                }
            }

            throw new SaveNotFoundException(key);
        }

        private async Task<T> LoadInternalParallelAsync<T>(string key, CancellationToken ct)
        {
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                ISaveAdapter[] group = _loadOrderGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            QuickLog.Debug<DataSyncDirector>(
                "Load parallel: launching {0} fetches for key '{1}'",
                entries.Count, key);

            var fetchTasks = entries.Select(e =>
                TryFetchAsync(e.adapter, key, e.adapter.ReadTimeout, ct));
            var results = await Task.WhenAll(fetchTasks);

            var candidates = new List<(int groupIndex, int adapterIndex, string adapterId, byte[] data)>();
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].data != null)
                {
                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: adapter '{0}' returned {1} bytes for key '{2}'",
                        entries[i].adapter.AdapterId, results[i].data.Length, key);
                    candidates.Add((entries[i].groupIndex, entries[i].adapterIndex, entries[i].adapter.AdapterId, results[i].data));
                }
                else if (results[i].timedOut)
                    QuickLog.Warning<DataSyncDirector>(
                        "Parallel load: adapter '{0}' timed out for key '{1}'",
                        entries[i].adapter.AdapterId, key
                    );
                else
                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: adapter '{0}' returned no data for key '{1}'",
                        entries[i].adapter.AdapterId, key);
            }

            candidates.Sort((a, b) =>
            {
                int cmp = a.groupIndex.CompareTo(b.groupIndex);
                if (cmp != 0) return cmp;
                return a.adapterIndex.CompareTo(b.adapterIndex);
            });

            QuickLog.Debug<DataSyncDirector>(
                "Load parallel: {0} candidates to try for key '{1}'",
                candidates.Count, key);

            foreach (var candidate in candidates)
            {
                try
                {
                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: decoding candidate group[{0}] adapter '{1}' ({2} bytes) for key '{3}'",
                        candidate.groupIndex, candidate.adapterId, candidate.data.Length, key);

                    using var ms = new MemoryStream(candidate.data);
                    DecodeResult decoded = await DecodeStream(ms, ct);

                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: decoded version={0}, dataType={1}",
                        decoded.Version, decoded.DataType.Name);

                    Type snapshotType = VersionRegistry.GetSnapshotType(typeof(T), decoded.Version);
                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: snapshotType={0}", snapshotType.Name);

                    ISaveTranslator translator = ResolveTranslator();
                    object snapshot = translator.ConvertTo(decoded.Data, snapshotType);

                    QuickLog.Debug<DataSyncDirector>(
                        "Load parallel: converted to {0}, migrating",
                        snapshot?.GetType().Name ?? "null");

                    T result = (T)VersionRegistry.MigrateToCurrent(snapshot, typeof(T), decoded.Version);

                    QuickLog.Info<DataSyncDirector>(
                        "Loaded key '{0}' from adapter '{1}' (parallel)",
                        key, candidate.adapterId);
                    return result;
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Parallel load: decode failed for group[{0}] adapter '{1}' key '{2}': {3}",
                        candidate.groupIndex,
                        candidate.adapterId,
                        key, ex.Message
                    );
                }
            }

            throw new SaveNotFoundException(key);
        }

        private static async Task<(byte[] data, bool timedOut)> TryFetchAsync(
            ISaveAdapter adapter, string key, TimeSpan timeout, CancellationToken ct)
        {
            var readTask = adapter.OpenReadAsync(key, ct);
            if (await Task.WhenAny(readTask, Task.Delay(timeout, ct)) != readTask)
            {
                QuickLog.Debug<DataSyncDirector>(
                    "TryFetch: adapter '{0}' timed out ({1}ms) for key '{2}'",
                    adapter.AdapterId, timeout.TotalMilliseconds, key);
                return (null, true);
            }

            Stream stream = await readTask;
            if (stream == null)
            {
                QuickLog.Debug<DataSyncDirector>(
                    "TryFetch: adapter '{0}' returned null for key '{1}'",
                    adapter.AdapterId, key);
                return (null, false);
            }

            using (stream)
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms, ct);
                QuickLog.Debug<DataSyncDirector>(
                    "TryFetch: adapter '{0}' read {1} bytes for key '{2}'",
                    adapter.AdapterId, ms.Length, key);
                return (ms.ToArray(), false);
            }
        }

        private async Task DeleteInternalAsync(string key, CancellationToken ct)
        {
            for (int g = 0; g < _saveOrderGroups.Length; g++)
            {
                await DeleteDataInInSingleGroupInternalAsync(key, g, ct);
            }
        }

        private async Task DeleteDataInInSingleGroupInternalAsync(string key, int g, CancellationToken ct)
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
                        "Delete group[{0}] adapter '{1}' failed for key '{2}': {3}",
                        g, adapter.AdapterId, key,
                        ex.Message
                    );
                }
            }
        }

        private async Task<bool> ExistsInternalAsync(string key, CancellationToken ct)
        {
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                ISaveAdapter[] group = _loadOrderGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            var tasks = entries.Select(async e =>
            {
                try
                {
                    var task = e.adapter.ExistsAsync(key, ct);
                    if (await Task.WhenAny(task, Task.Delay(e.adapter.ReadTimeout, ct)) != task)
                        return false;
                    return await task;
                }
                catch { return false; }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }

        private async Task<Stream> OpenReadStreamInternalAsync(string key, CancellationToken ct)
        {
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < _loadOrderGroups.Length; g++)
            {
                ISaveAdapter[] group = _loadOrderGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            var tasks = entries.Select(async e =>
            {
                try
                {
                    var readTask = e.adapter.OpenReadAsync(key, ct);
                    if (await Task.WhenAny(readTask, Task.Delay(e.adapter.ReadTimeout, ct)) != readTask)
                        return null;
                    return await readTask;
                }
                catch { return null; }
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            Stream best = null;
            int bestGroup = int.MaxValue;
            int bestIndex = int.MaxValue;

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] == null) continue;

                if (entries[i].groupIndex < bestGroup
                    || (entries[i].groupIndex == bestGroup && entries[i].adapterIndex < bestIndex))
                {
                    best?.Dispose();
                    best = results[i];
                    bestGroup = entries[i].groupIndex;
                    bestIndex = entries[i].adapterIndex;
                }
                else
                {
                    results[i].Dispose();
                }
            }

            if (best == null)
                throw new SaveNotFoundException(key);

            return best;
        }

        private async Task WriteStreamInternalAsync(string key, Stream data, CancellationToken ct)
        {
            for (int g = 0; g < _saveOrderGroups.Length; g++)
            {
                foreach (ISaveAdapter adapter in _saveOrderGroups[g])
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

        public ISaveAdapter ResolveAdapter()
        {
            if (_loadOrderGroups.Length > 0 && _loadOrderGroups[0].Length > 0)
            {
                return _loadOrderGroups[0][0];
            }

            throw new SaveAdapterException(
                "none",
                "No adapters configured"
            );
        }

        public ISaveAdapter[] ActiveAdapters
            => _saveOrderGroups
                .SelectMany(g => g)
                .Union(_loadOrderGroups.SelectMany(g => g))
                .Distinct()
                .ToArray();

        public ISaveTranslator ResolveTranslator()
            => _translators.FirstOrDefault()
                ?? throw new TranslationException("No translators configured");

        #endregion

        #region Decode Pipeline

        private async Task<DecodeResult> DecodeStream(Stream stream, CancellationToken ct)
        {
            ISaveTranslator translator = ResolveTranslator();

            QuickLog.Debug<DataSyncDirector>(
                "DecodeStream: trying primary translator '{0}', stream length={1}",
                translator.FormatId, stream.Length);

            try
            {
                var result = await translator.DecodeAsync(stream, ct);
                QuickLog.Debug<DataSyncDirector>(
                    "DecodeStream: primary translator '{0}' succeeded, version={1}",
                    translator.FormatId, result.Version);
                return result;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<DataSyncDirector>(
                    "Primary translator decode failed: {0}",
                    ex.Message
                );
            }

            QuickLog.Debug<DataSyncDirector>(
                "DecodeStream: scanning {0} translators for signature match",
                _translators.Count);

            foreach (ISaveTranslator t in _translators)
            {
                stream.Position = 0;
                if (!TryMatchSignature(stream, t.Signature))
                {
                    QuickLog.Debug<DataSyncDirector>(
                        "DecodeStream: signature mismatch for '{0}'", t.FormatId);
                    continue;
                }

                QuickLog.Debug<DataSyncDirector>(
                    "DecodeStream: signature matched '{0}', attempting decode", t.FormatId);

                stream.Position = 0;
                return await t.DecodeAsync(stream, ct);
            }

            throw new TranslationException("No translator matched the data signature");
        }

        private static bool TryMatchSignature(Stream stream, byte[] signature)
        {
            if (signature == null || signature.Length == 0) return false;

            var buffer = new byte[signature.Length];
            int bytesRead = stream.Read(buffer, 0, signature.Length);
            return bytesRead == signature.Length && buffer.SequenceEqual(signature);
        }

        #endregion
    }
}
