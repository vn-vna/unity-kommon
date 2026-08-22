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
    public readonly struct DataSyncLoadResult<T>
    {
        private readonly byte[] _payload;

        public T Data { get; }

        public byte[] Payload => _payload != null
            ? (byte[])_payload.Clone()
            : null;

        internal DataSyncLoadResult(T data, byte[] payload)
        {
            Data = data;
            _payload = payload != null ? (byte[])payload.Clone() : null;
        }
    }

    [AddComponentMenu("Scheherazade/Data Sync Director")]
    [DontDestroyOnLoad]
    public class DataSyncDirector : SingletonBehavior<DataSyncDirector>
    {
        #region Constants
        private const string ConfigPath = "Integration/Managers/DataSyncConfiguration";
        #endregion

        #region Static Init
        private static TaskCompletionSource<bool> _readySource
            = CreateReadySource();
        public static Task ReadyTask => _readySource.Task;
        #endregion

        #region Per-Key Write Ordering
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _keySemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _readySource = CreateReadySource();
            _keySemaphores.Clear();
        }

        private static TaskCompletionSource<bool> CreateReadySource()
        {
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        private static async Task RunWithKeyLockAsync(
            string key,
            CancellationToken cancellationToken,
            Func<Task> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            try { await operation(); }
            finally { semaphore.Release(); }
        }

        private static async Task<T> RunWithKeyLockAsync<T>(
            string key,
            CancellationToken cancellationToken,
            Func<Task<T>> operation)
        {
            SemaphoreSlim semaphore = _keySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            try { return await operation(); }
            finally { semaphore.Release(); }
        }

        #endregion

        #region Private Fields

        private DataSyncConfiguration _config;
        private ISaveAdapter[][] _saveOrderGroups;
        private ISaveAdapter[][] _loadOrderGroups;
        private ISaveAdapter[][] _configuredSaveOrder;
        private ISaveAdapter[][] _configuredLoadOrder;
        private HashSet<ISaveAdapter> _configuredAdapters;
        private List<ISaveTranslator> _translators;
        private readonly ConcurrentDictionary<string, byte[]> _lastLoadedPayloads
            = new ConcurrentDictionary<string, byte[]>();
        private readonly SemaphoreSlim _availabilityGate
            = new SemaphoreSlim(1, 1);
        private readonly Dictionary<ISaveAdapter, Task<bool>>
            _pendingInitializationTasks
                = new Dictionary<ISaveAdapter, Task<bool>>(
                    SaveAdapterReferenceComparer.Instance
                );
        private int _maxSignatureLength;
        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                _config = Resources.Load<DataSyncConfiguration>(ConfigPath);
                DataSyncLogging.Verbose = _config != null
                    && _config.VerboseLogging;

                if (_config != null)
                {
                    _configuredSaveOrder = _config.ResolveSaveOrderGroups();
                    _configuredLoadOrder = _config.ResolveLoadOrderGroups();
                    _saveOrderGroups = _configuredSaveOrder;
                    _loadOrderGroups = _configuredLoadOrder;
                    _translators = _config.Translators
                        ?.Where(t => t != null).ToList()
                        ?? new List<ISaveTranslator>();

                    _maxSignatureLength = _translators.Max(t => t.Signature?.Length ?? 0);

                    await InitializeAndFilterAsync();
                }
                else
                {
                    var localAdapter = ScriptableObject.CreateInstance<LocalSaveAdapter>();
                    await localAdapter.InitializeAsync();
                    _saveOrderGroups = new[] { new ISaveAdapter[] { localAdapter } };
                    _loadOrderGroups = new[] { new ISaveAdapter[] { localAdapter } };
                    _translators = new List<ISaveTranslator> { ScriptableObject.CreateInstance<UnityJsonTranslator>() };
                    _maxSignatureLength = _translators.Max(t => t.Signature?.Length ?? 0);
                }

                _readySource.TrySetResult(true);
            }
            catch (Exception exception)
            {
                QuickLog.Error<DataSyncDirector>(
                    "Data Sync initialization failed: {0}",
                    exception
                );
                _readySource.TrySetException(exception);
            }
        }

        private async Task InitializeAndFilterAsync()
        {
            _configuredAdapters = CollectConfiguredAdapters();

            foreach (ISaveAdapter adapter in _configuredAdapters)
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

            _saveOrderGroups = FilterGroups(_configuredSaveOrder);
            _loadOrderGroups = FilterGroups(_configuredLoadOrder);
        }

        private HashSet<ISaveAdapter> CollectConfiguredAdapters()
        {
            var result = new HashSet<ISaveAdapter>();

            if (_configuredSaveOrder != null)
            {
                foreach (ISaveAdapter[] group in _configuredSaveOrder)
                {
                    foreach (ISaveAdapter adapter in group)
                    {
                        if (adapter != null) result.Add(adapter);
                    }
                }
            }

            if (_configuredLoadOrder != null)
            {
                foreach (ISaveAdapter[] group in _configuredLoadOrder)
                {
                    foreach (ISaveAdapter adapter in group)
                    {
                        if (adapter != null) result.Add(adapter);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Re-initializes adapters that reported unavailable at startup and
        /// recomputes the active groups. Available adapters are skipped, so
        /// this is cheap. Lets adapters rejoin after their provider becomes
        /// ready (e.g. Play Games auth completing after the initial wait).
        /// </summary>
        private async Task RefreshAdapterAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            if (_configuredAdapters == null) return;

            await _availabilityGate.WaitAsync(cancellationToken);
            try
            {
                await RefreshUnavailableAdaptersAsync(cancellationToken);
                _saveOrderGroups = FilterGroups(_configuredSaveOrder);
                _loadOrderGroups = FilterGroups(_configuredLoadOrder);
            }
            finally
            {
                _availabilityGate.Release();
            }
        }

        private async Task RefreshUnavailableAdaptersAsync(
            CancellationToken cancellationToken)
        {
            foreach (ISaveAdapter adapter in _configuredAdapters)
            {
                if (adapter.IsAvailable) continue;

                try
                {
                    await WaitForAdapterInitializationAsync(
                        adapter,
                        cancellationToken
                    );
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Adapter '{0}' refresh threw: {1}",
                        adapter.AdapterId,
                        exception.Message
                    );
                }
            }
        }

        private async Task WaitForAdapterInitializationAsync(
            ISaveAdapter adapter,
            CancellationToken cancellationToken)
        {
            if (!_pendingInitializationTasks.TryGetValue(
                    adapter,
                    out Task<bool> initializationTask))
            {
                initializationTask = adapter.InitializeAsync();
                _pendingInitializationTasks[adapter] = initializationTask;
            }

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    Task cancelledTask = Task.Delay(
                        Timeout.Infinite,
                        cancellationToken
                    );
                    if (await Task.WhenAny(initializationTask, cancelledTask)
                        != initializationTask)
                    {
                        ObserveAbandonedTask(initializationTask, null);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                await initializationTask;
            }
            finally
            {
                if (initializationTask.IsCompleted)
                {
                    _pendingInitializationTasks.Remove(adapter);
                }
            }
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

        private void EnsureAllConfiguredLoadAdaptersAvailable()
        {
            ISaveAdapter[][] loadGroups = _configuredLoadOrder
                ?? _loadOrderGroups;
            if (loadGroups == null || loadGroups.Length == 0)
            {
                throw new SaveAdapterException(
                    "strict",
                    "No configured load adapters are available."
                );
            }

            ISaveAdapter[] adapters = loadGroups
                .SelectMany(group => group ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            if (adapters.Length == 0)
            {
                throw new SaveAdapterException(
                    "strict",
                    "No configured load adapters are available."
                );
            }

            foreach (ISaveAdapter adapter in adapters)
            {
                if (adapter.IsAvailable
                    && (adapter.SupportedFeatures & SaveAdapterFeature.Read) != 0)
                {
                    continue;
                }

                throw new SaveAdapterException(
                    adapter.AdapterId,
                    "A configured load adapter is unavailable or unreadable."
                );
            }
        }

        /// <summary>
        /// Emits a debug log only when verbose logging is enabled in the
        /// Data Sync configuration. Errors and warnings are never gated.
        /// </summary>
        private static void VerboseLog(string message, params object[] args)
        {
            if (!DataSyncLogging.Verbose) return;

            Debug.LogFormat(message, args);
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

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
            => RunWithKeyLockAsync(
                key,
                ct,
                () => SaveInternalAsync(key, data, ct, false)
            );

        public Task SaveStrictAsync<T>(
            string key,
            T data,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                ct,
                () => SaveInternalAsync(key, data, ct, true)
            );
        }

        public Task<T> LoadAsync<T>(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(
                key,
                ct,
                () => LoadInternalAsync<T>(key, ct, false)
            );

        public Task<DataSyncLoadResult<T>> LoadStrictWithPayloadAsync<T>(
            string key,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                ct,
                async () =>
                {
                    T data = await LoadInternalAsync<T>(key, ct, true);
                    if (!_lastLoadedPayloads.TryGetValue(
                            key,
                            out byte[] payload))
                    {
                        throw new DataSyncException(
                            $"No selected payload was recorded for key '{key}'."
                        );
                    }

                    return new DataSyncLoadResult<T>(data, payload);
                }
            );
        }

        public bool TryGetLastLoadedPayload(string key, out byte[] payload)
        {
            if (!string.IsNullOrEmpty(key)
                && _lastLoadedPayloads.TryGetValue(key, out byte[] stored))
            {
                payload = (byte[])stored.Clone();
                return true;
            }

            payload = null;
            return false;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(
                key,
                ct,
                () => DeleteInternalAsync(key, ct)
            );

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => RunWithKeyLockAsync(
                key,
                ct,
                () => ExistsInternalAsync(key, ct)
            );

        public Task<bool> ExistsStrictAsync(
            string key,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                ct,
                () => ExistsInternalAsync(key, ct, true)
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
                ct,
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
                ct,
                () => WriteStreamInternalAsync(key, data, ct, false)
            );
        }

        public Task WriteStreamStrictAsync(
            string key,
            Stream data,
            CancellationToken ct = default)
        {
            return RunWithKeyLockAsync(
                key,
                ct,
                () => WriteStreamInternalAsync(key, data, ct, true)
            );
        }

        public Task VerifyPayloadStrictAsync(
            string key,
            byte[] expectedPayload,
            CancellationToken ct = default)
        {
            if (expectedPayload == null)
            {
                throw new ArgumentNullException(nameof(expectedPayload));
            }

            byte[] payloadSnapshot = (byte[])expectedPayload.Clone();
            return RunWithKeyLockAsync(
                key,
                ct,
                () => VerifyPayloadInternalAsync(key, payloadSnapshot, ct)
            );
        }

        public Task EnsureExactPayloadStrictAsync(
            string key,
            byte[] expectedPayload,
            CancellationToken ct = default)
        {
            if (expectedPayload == null)
            {
                throw new ArgumentNullException(nameof(expectedPayload));
            }

            byte[] payloadSnapshot = (byte[])expectedPayload.Clone();
            return RunWithKeyLockAsync(
                key,
                ct,
                () => EnsureExactPayloadInternalAsync(
                    key,
                    payloadSnapshot,
                    ct
                )
            );
        }

        #endregion

        #region Internal Implementations (no locking — called under per-key lock)

        private async Task SaveInternalAsync<T>(
            string key,
            T data,
            CancellationToken ct,
            bool requireAllConfiguredGroups
        )
        {
            await RefreshAdapterAvailabilityAsync(ct);
            ISaveAdapter[][] saveGroups = _saveOrderGroups;

            VersionTag currentVersion = VersionRegistry.GetCurrentVersion(typeof(T));
            ISaveTranslator translator = ResolveTranslator();

            VerboseLog(
                "Save<{0}>('{1}'): version={2}, translator={3}",
                typeof(T).Name, key, currentVersion, translator.FormatId);

            if (saveGroups.Length == 0)
                throw new SaveAdapterException("none", "No save groups configured");

            using var encodeStream = new MemoryStream();
            await translator.EncodeAsync(
                data, currentVersion, encodeStream, ct
            );
            byte[] encodedBytes = encodeStream.ToArray();

            VerboseLog(
                "Save<{0}>('{1}'): encoded {2} bytes",
                typeof(T).Name, key, encodedBytes.Length);

            if (requireAllConfiguredGroups)
            {
                await WriteStrictPayloadToGroupsAsync(
                    key,
                    encodedBytes,
                    _configuredSaveOrder ?? saveGroups,
                    _configuredLoadOrder ?? _loadOrderGroups,
                    ct
                );
                QuickLog.Info<DataSyncDirector>(
                    "Strictly saved key '{0}' with configured load convergence.",
                    key
                );
                return;
            }

            int groupCount = saveGroups.Length;
            var groupTasks = new Task<(bool success, ISaveAdapter adapter, int groupIndex)>[groupCount];

            for (int g = 0; g < groupCount; g++)
            {
                int groupIndex = g;
                ISaveAdapter[] group = saveGroups[g];
                groupTasks[g] = TryWriteToGroupAsync(
                    key,
                    encodedBytes,
                    groupIndex,
                    group,
                    ct
                );
            }

            (bool success, ISaveAdapter adapter, int groupIndex)[] results = await Task.WhenAll(groupTasks);

            var successes = results.Where(r => r.success).ToArray();
            var failures = results.Where(r => !r.success).ToArray();

            if (successes.Length > 0)
            {
                QuickLog.Info<DataSyncDirector>(
                    "Saved key '{0}' -> {1}/{2} group(s): [{3}]",
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

        private static async Task<(
            bool success,
            ISaveAdapter adapter,
            int groupIndex)> TryWriteToGroupAsync(
            string key,
            byte[] encodedBytes,
            int groupIndex,
            ISaveAdapter[] group,
            CancellationToken ct)
        {
            foreach (ISaveAdapter adapter in group)
            {
                try
                {
                    using MemoryStream ms = new MemoryStream(encodedBytes);
                    await adapter.WriteAsync(key, ms, ct);
                    return (true, adapter, groupIndex);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
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

        private static async Task WriteStrictPayloadToGroupsAsync(
            string key,
            byte[] payload,
            ISaveAdapter[][] configuredSaveGroups,
            ISaveAdapter[][] configuredLoadGroups,
            CancellationToken cancellationToken = default)
        {
            ValidateStrictWriteArguments(
                key,
                payload,
                configuredSaveGroups,
                configuredLoadGroups
            );

            SaveAdapterReferenceComparer comparer
                = SaveAdapterReferenceComparer.Instance;
            var saveAdapters = new HashSet<ISaveAdapter>(comparer);
            foreach (ISaveAdapter[] group in configuredSaveGroups)
            {
                foreach (ISaveAdapter adapter in group)
                {
                    if (adapter != null) saveAdapters.Add(adapter);
                }
            }

            ISaveAdapter[] loadAdapters = configuredLoadGroups
                .SelectMany(group => group)
                .Where(adapter => adapter != null)
                .Distinct(comparer)
                .ToArray();
            ValidateStrictWriteTopology(
                configuredSaveGroups,
                loadAdapters,
                saveAdapters
            );

            var verifiedAdapters = new HashSet<ISaveAdapter>(comparer);
            for (int index = loadAdapters.Length - 1; index >= 0; index--)
            {
                ISaveAdapter adapter = loadAdapters[index];
                await WriteAndVerifyStrictAdapterAsync(
                    adapter,
                    key,
                    payload,
                    cancellationToken
                );
                verifiedAdapters.Add(adapter);
            }

            var selectedWriters = new HashSet<ISaveAdapter>(comparer);
            for (int groupIndex = 0;
                 groupIndex < configuredSaveGroups.Length;
                 groupIndex++)
            {
                ISaveAdapter writer = await SatisfyStrictSaveGroupAsync(
                    key,
                    payload,
                    configuredSaveGroups[groupIndex],
                    groupIndex,
                    verifiedAdapters,
                    cancellationToken
                );
                selectedWriters.Add(writer);
            }

            foreach (ISaveAdapter adapter in loadAdapters
                         .Concat(selectedWriters)
                         .Distinct(comparer))
            {
                await VerifyAdapterPayloadAsync(
                    adapter,
                    key,
                    payload,
                    cancellationToken
                );
            }
        }

        private static void ValidateStrictWriteArguments(
            string key,
            byte[] payload,
            ISaveAdapter[][] configuredSaveGroups,
            ISaveAdapter[][] configuredLoadGroups)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A save key is required.", nameof(key));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (configuredSaveGroups == null
                || configuredSaveGroups.Length == 0
                || configuredLoadGroups == null
                || configuredLoadGroups.Length == 0)
            {
                throw new SaveAdapterException(
                    "strict",
                    "Strict writes require configured save and load groups."
                );
            }

            if (configuredSaveGroups.Any(group => group == null || group.Length == 0)
                || configuredLoadGroups.Any(group => group == null || group.Length == 0))
            {
                throw new SaveAdapterException(
                    "strict",
                    "Strict writes do not support empty adapter groups."
                );
            }
        }

        private static void ValidateStrictWriteTopology(
            ISaveAdapter[][] configuredSaveGroups,
            ISaveAdapter[] loadAdapters,
            HashSet<ISaveAdapter> saveAdapters)
        {
            if (loadAdapters.Length == 0)
            {
                throw new SaveAdapterException(
                    "strict",
                    "Strict writes require at least one configured load adapter."
                );
            }

            foreach (ISaveAdapter adapter in loadAdapters)
            {
                if (!saveAdapters.Contains(adapter))
                {
                    throw new SaveAdapterException(
                        adapter.AdapterId,
                        "A strict load adapter must also exist in the save topology."
                    );
                }

                EnsureStrictAdapterCapability(adapter, "load");
            }

            for (int groupIndex = 0;
                 groupIndex < configuredSaveGroups.Length;
                 groupIndex++)
            {
                if (configuredSaveGroups[groupIndex].Any(CanStrictlyWrite))
                {
                    continue;
                }

                throw new SaveAdapterException(
                    $"group[{groupIndex}]",
                    "No available adapter can perform a verified strict write."
                );
            }
        }

        private static async Task<ISaveAdapter> SatisfyStrictSaveGroupAsync(
            string key,
            byte[] payload,
            ISaveAdapter[] group,
            int groupIndex,
            HashSet<ISaveAdapter> verifiedAdapters,
            CancellationToken cancellationToken)
        {
            var failures = new List<Exception>();
            foreach (ISaveAdapter adapter in group)
            {
                if (verifiedAdapters.Contains(adapter)) return adapter;
                if (!CanStrictlyWrite(adapter)) continue;

                try
                {
                    await WriteAndVerifyStrictAdapterAsync(
                        adapter,
                        key,
                        payload,
                        cancellationToken
                    );
                    verifiedAdapters.Add(adapter);
                    return adapter;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException(
                $"Strict save group {groupIndex} has no verified writer for "
                + $"'{key}'.",
                failures
            );
        }

        private static async Task WriteAndVerifyStrictAdapterAsync(
            ISaveAdapter adapter,
            string key,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            EnsureStrictAdapterCapability(adapter, "write");
            Exception writeFailure = null;
            try
            {
                using var stream = new MemoryStream(payload, writable: false);
                await adapter.WriteAsync(key, stream, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }

            try
            {
                await VerifyAdapterPayloadAsync(
                    adapter,
                    key,
                    payload,
                    cancellationToken
                );
                if (writeFailure != null)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Adapter '{0}' reported a strict write failure for "
                        + "'{1}', but exact read-back confirmed the commit: {2}",
                        adapter.AdapterId,
                        key,
                        writeFailure.Message
                    );
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception verificationFailure)
            {
                if (writeFailure == null) throw;
                throw new AggregateException(
                    $"Adapter '{adapter.AdapterId}' failed strict write and "
                    + $"verification for '{key}'.",
                    writeFailure,
                    verificationFailure
                );
            }
        }

        private static void EnsureStrictAdapterCapability(
            ISaveAdapter adapter,
            string role)
        {
            if (CanStrictlyWrite(adapter)) return;

            throw new SaveAdapterException(
                adapter?.AdapterId ?? "null",
                $"Configured {role} adapter is unavailable or does not support "
                + "read-back verified writes."
            );
        }

        private static bool CanStrictlyWrite(ISaveAdapter adapter)
        {
            const SaveAdapterFeature required = SaveAdapterFeature.Read
                | SaveAdapterFeature.Write;
            return adapter != null
                && adapter.IsAvailable
                && (adapter.SupportedFeatures & required) == required;
        }

        private static async Task VerifyAdapterPayloadAsync(
            ISaveAdapter adapter,
            string key,
            byte[] expectedPayload,
            CancellationToken cancellationToken)
        {
            using Stream stream = await AwaitAdapterTaskAsync(
                adapter.OpenReadAsync(key, cancellationToken),
                adapter,
                key,
                "payload verification",
                cancellationToken,
                DisposeStream
            );
            if (stream == null)
            {
                throw new SaveAdapterException(
                    adapter.AdapterId,
                    $"Adapter returned no payload while verifying '{key}'."
                );
            }

            using var buffer = new MemoryStream();
            await CopyAdapterStreamToAsync(
                stream,
                buffer,
                adapter,
                key,
                "payload verification",
                adapter.ReadTimeout,
                cancellationToken
            );
            if (!buffer.ToArray().SequenceEqual(expectedPayload))
            {
                throw new SaveAdapterException(
                    adapter.AdapterId,
                    $"Adapter payload verification failed for '{key}'."
                );
            }
        }

        private async Task<T> LoadInternalAsync<T>(
            string key,
            CancellationToken ct,
            bool requireAllConfiguredAdapters)
        {
            _lastLoadedPayloads.TryRemove(key, out _);
            await RefreshAdapterAvailabilityAsync(ct);
            if (requireAllConfiguredAdapters)
            {
                EnsureAllConfiguredLoadAdaptersAvailable();
                return await LoadStrictConvergedAsync<T>(key, ct);
            }

            ISaveAdapter[][] loadGroups = _loadOrderGroups;

            if (loadGroups.Length == 0)
                throw new SaveAdapterException("none", "No load groups configured");

            KeyConflictPlan keyPlan = FindPlanForKey(key);
            if (keyPlan != null)
            {
                QuickLog.Info<DataSyncDirector>(
                    "Conflict plan for key '{0}': '{1}' ({2})",
                    key, keyPlan.Plan.name, keyPlan.MatchType
                );
                return await LoadInternalWithPlanAsync<T>(
                    key, keyPlan.Plan, ct);
            }

            ResolveMode mode = _config != null
                ? _config.ResolveMode
                : ResolveMode.Priority;

            if (mode == ResolveMode.LastWriteComplete)
            {
                QuickLog.Info<DataSyncDirector>(
                    "LWC: resolving key='{0}' type={1} groups={2} adapters={3}",
                    key, typeof(T).Name, loadGroups.Length,
                    loadGroups.Sum(group => group.Length));
                return await LoadInternalLastWriteCompleteAsync<T>(key, ct);
            }

            bool parallel = _config != null && _config.ParallelLoadEnabled;
            VerboseLog(
                "Load<{0}>('{1}'): groups={2}, parallel={3}, translators={4}",
                typeof(T).Name, key, loadGroups.Length, parallel, _translators.Count);

            if (!parallel)
                return await LoadInternalSequentialAsync<T>(key, ct);

            return await LoadInternalParallelAsync<T>(key, ct);
        }

        private async Task<T> LoadStrictConvergedAsync<T>(
            string key,
            CancellationToken cancellationToken)
        {
            ISaveAdapter[] adapters = (_configuredLoadOrder
                    ?? _loadOrderGroups)
                .SelectMany(group => group ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            if (adapters.Length == 0)
            {
                throw new SaveAdapterException(
                    "strict",
                    "No configured load adapters are available."
                );
            }

            var readTasks = adapters.Select(async adapter =>
            {
                try
                {
                    byte[] payload = await ReadOptionalAdapterPayloadAsync(
                        adapter,
                        key,
                        cancellationToken
                    );
                    return (payload, error: (Exception)null);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return (payload: (byte[])null, error: exception);
                }
            }).ToArray();
            var results = await Task.WhenAll(readTasks);
            Exception[] failures = results
                .Where(result => result.error != null)
                .Select(result => result.error)
                .ToArray();
            if (failures.Length > 0)
            {
                throw new AggregateException(
                    $"Could not read every configured loader for '{key}'.",
                    failures
                );
            }

            int presentCount = results.Count(result => result.payload != null);
            if (presentCount == 0) throw new SaveNotFoundException(key);
            if (presentCount != results.Length)
            {
                throw new SaveAdapterException(
                    "strict",
                    $"Save key '{key}' exists on {presentCount}/"
                    + $"{results.Length} configured load adapters."
                );
            }

            byte[] selectedPayload = results[0].payload;
            if (results.Any(result =>
                    !result.payload.SequenceEqual(selectedPayload)))
            {
                throw new SaveAdapterException(
                    "strict",
                    $"Configured load adapters disagree on payload for '{key}'."
                );
            }

            using var decodeStream = new MemoryStream(
                selectedPayload,
                writable: false
            );
            var decodedPayload = await DecodeStream(
                decodeStream,
                cancellationToken
            );
            DecodeResult decoded = decodedPayload.result;
            Type snapshotType = VersionRegistry.GetSnapshotType(
                typeof(T),
                decoded.Version
            );
            object snapshot = decodedPayload.translator.ConvertTo(
                decoded.Data,
                snapshotType
            );
            T loaded = (T)VersionRegistry.MigrateToCurrent(
                snapshot,
                typeof(T),
                decoded.Version
            );
            StoreLoadedPayload(key, selectedPayload);
            return loaded;
        }

        private static async Task<byte[]> ReadOptionalAdapterPayloadAsync(
            ISaveAdapter adapter,
            string key,
            CancellationToken cancellationToken)
        {
            if (adapter == null
                || !adapter.IsAvailable
                || (adapter.SupportedFeatures & SaveAdapterFeature.Read) == 0)
            {
                throw new SaveAdapterException(
                    adapter?.AdapterId ?? "null",
                    "Configured load adapter is unavailable or unreadable."
                );
            }

            Stream stream = await AwaitAdapterTaskAsync(
                adapter.OpenReadAsync(key, cancellationToken),
                adapter,
                key,
                "strict payload read",
                cancellationToken,
                DisposeStream
            );
            if (stream == null) return null;

            using (stream)
            using (var buffer = new MemoryStream())
            {
                await CopyAdapterStreamToAsync(
                    stream,
                    buffer,
                    adapter,
                    key,
                    "strict payload read",
                    adapter.ReadTimeout,
                    cancellationToken
                );
                return buffer.ToArray();
            }
        }

        private async Task<T> LoadInternalSequentialAsync<T>(string key, CancellationToken ct)
        {
            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            for (int g = 0; g < loadGroups.Length; g++)
            {
                ISaveAdapter[] group = loadGroups[g];

                for (int a = 0; a < group.Length; a++)
                {
                    ISaveAdapter adapter = group[a];
                    try
                    {
                        VerboseLog(
                            "Load seq: trying group[{0}] adapter[{1}] '{2}' for key '{3}'",
                            g, a, adapter.AdapterId, key);

                        Stream stream = await AwaitAdapterTaskAsync(
                            adapter.OpenReadAsync(key, ct),
                            adapter,
                            key,
                            "sequential payload read",
                            ct,
                            DisposeStream
                        );
                        if (stream == null)
                        {
                            VerboseLog(
                                "Load seq: group[{0}] adapter '{1}' returned null stream for key '{2}'",
                                g, adapter.AdapterId, key);
                            continue;
                        }

                        using (stream)
                        using (var payloadBuffer = new MemoryStream())
                        {
                            long streamLength = stream.CanSeek
                                ? stream.Length
                                : -1;
                            VerboseLog(
                                "Load seq: group[{0}] adapter '{1}' returned "
                                + "{2} bytes for key '{3}'",
                                g,
                                adapter.AdapterId,
                                streamLength,
                                key
                            );
                            await CopyAdapterStreamToAsync(
                                stream,
                                payloadBuffer,
                                adapter,
                                key,
                                "sequential payload read",
                                adapter.ReadTimeout,
                                ct
                            );
                            byte[] payload = payloadBuffer.ToArray();
                            VerboseLog(
                                "Load seq: decoding stream for key '{0}'", key);

                            using var decodeStream = new MemoryStream(
                                payload,
                                writable: false
                            );
                            var decodedPayload = await DecodeStream(
                                decodeStream,
                                ct
                            );
                            DecodeResult decoded = decodedPayload.result;

                            VerboseLog(
                                "Load seq: decoded version={0}, dataType={1} for key '{2}'",
                                decoded.Version, decoded.DataType.Name, key);

                            Type snapshotType = VersionRegistry.GetSnapshotType(typeof(T), decoded.Version);
                            VerboseLog(
                                "Load seq: snapshotType={0} for key '{1}'",
                                snapshotType.Name, key);

                            object snapshot = decodedPayload.translator.ConvertTo(
                                decoded.Data,
                                snapshotType
                            );

                            VerboseLog(
                                "Load seq: converted to {0}, migrating for key '{1}'",
                                snapshot?.GetType().Name ?? "null", key);

                            T result = (T)VersionRegistry.MigrateToCurrent(snapshot, typeof(T), decoded.Version);

                            QuickLog.Info<DataSyncDirector>(
                                "Loaded key '{0}' from adapter '{1}' in group [{2}]",
                                key, adapter.AdapterId, g);
                            StoreLoadedPayload(key, payload);
                            return result;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
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
            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < loadGroups.Length; g++)
            {
                ISaveAdapter[] group = loadGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            VerboseLog(
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
                    VerboseLog(
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
                    VerboseLog(
                        "Load parallel: adapter '{0}' returned no data for key '{1}'",
                        entries[i].adapter.AdapterId, key);
            }

            candidates.Sort((a, b) =>
            {
                int cmp = a.groupIndex.CompareTo(b.groupIndex);
                if (cmp != 0) return cmp;
                return a.adapterIndex.CompareTo(b.adapterIndex);
            });

            VerboseLog(
                "Load parallel: {0} candidates to try for key '{1}'",
                candidates.Count, key);

            foreach (var candidate in candidates)
            {
                try
                {
                    VerboseLog(
                        "Load parallel: decoding candidate group[{0}] adapter '{1}' ({2} bytes) for key '{3}'",
                        candidate.groupIndex, candidate.adapterId, candidate.data.Length, key);

                    using var ms = new MemoryStream(candidate.data);
                    var decodedPayload = await DecodeStream(ms, ct);
                    DecodeResult decoded = decodedPayload.result;

                    VerboseLog(
                        "Load parallel: decoded version={0}, dataType={1}",
                        decoded.Version, decoded.DataType.Name);

                    Type snapshotType = VersionRegistry.GetSnapshotType(typeof(T), decoded.Version);
                    VerboseLog(
                        "Load parallel: snapshotType={0}", snapshotType.Name);

                    object snapshot = decodedPayload.translator.ConvertTo(
                        decoded.Data,
                        snapshotType
                    );

                    VerboseLog(
                        "Load parallel: converted to {0}, migrating",
                        snapshot?.GetType().Name ?? "null");

                    T result = (T)VersionRegistry.MigrateToCurrent(snapshot, typeof(T), decoded.Version);

                    QuickLog.Info<DataSyncDirector>(
                        "Loaded key '{0}' from adapter '{1}' (parallel)",
                        key, candidate.adapterId);
                    StoreLoadedPayload(key, candidate.data);
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                    when (ct.IsCancellationRequested)
                {
                    throw;
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

        private async Task<T> LoadInternalLastWriteCompleteAsync<T>(
            string key, CancellationToken ct)
        {
            // ── Phase 1: Flatten & launch all fetches ──
            var entries = FlattenLoadOrder();
            QuickLog.Info<DataSyncDirector>(
                "LWC: [phase=FETCH] key='{0}' launching {1} adapter(s)",
                key, entries.Count);

            var fetchTasks = entries.Select(e =>
                TryFetchAsync(e.adapter, key, e.adapter.ReadTimeout, ct));
            var results = await Task.WhenAll(fetchTasks);

            // ── Phase 2: Collect successful fetches ──
            var candidates = new List<(ISaveAdapter adapter, int groupIndex,
                int adapterIndex, byte[] data)>();
            int timedOut = 0;
            int notFound = 0;

            for (int i = 0; i < results.Length; i++)
            {
                string adapterId = entries[i].adapter.AdapterId;

                if (results[i].data != null)
                {
                    VerboseLog(
                        "LWC: [phase=FETCH] adapter='{0}' key='{1}' "
                        + "-> {2} bytes",
                        adapterId, key, results[i].data.Length);
                    candidates.Add((
                        entries[i].adapter,
                        entries[i].groupIndex,
                        entries[i].adapterIndex,
                        results[i].data
                    ));
                }
                else if (results[i].timedOut)
                {
                    timedOut++;
                    QuickLog.Warning<DataSyncDirector>(
                        "LWC: [phase=FETCH] adapter='{0}' key='{1}' "
                        + "-> TIMED OUT",
                        adapterId, key);
                }
                else
                {
                    notFound++;
                    VerboseLog(
                        "LWC: [phase=FETCH] adapter='{0}' key='{1}' "
                        + "-> NOT FOUND",
                        adapterId, key);
                }
            }

            QuickLog.Info<DataSyncDirector>(
                "LWC: [phase=FETCH] key='{0}' result: found={1} "
                + "timedOut={2} notFound={3}",
                key, candidates.Count, timedOut, notFound);

            if (candidates.Count == 0)
            {
                QuickLog.Info<DataSyncDirector>(
                    "LWC: [phase=DONE] key='{0}' NO DATA from any adapter",
                    key);
                throw new SaveNotFoundException(key);
            }

            // ── Phase 3: Query last-write times ──
            QuickLog.Info<DataSyncDirector>(
                "LWC: [phase=WRITE_TIME] key='{0}' querying {1} adapter(s)",
                key, candidates.Count);

            var enriched = await EnrichWithWriteTimesAsync(key, candidates, ct);

            foreach (var c in enriched)
            {
                string wtStr = c.writeTime.HasValue
                    ? c.writeTime.Value.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    : "null";
                VerboseLog(
                    "LWC: [phase=WRITE_TIME] adapter='{0}' key='{1}' "
                    + "lastWrite={2}",
                    c.adapterId, key, wtStr);
            }

            // ── Phase 4: Sort by timestamp (newest first) ──
            enriched.Sort((a, b) =>
            {
                int hasA = a.writeTime.HasValue ? 1 : 0;
                int hasB = b.writeTime.HasValue ? 1 : 0;
                int cmp = hasB.CompareTo(hasA);
                if (cmp != 0) return cmp;
                if (a.writeTime.HasValue && b.writeTime.HasValue)
                {
                    cmp = b.writeTime.Value.CompareTo(a.writeTime.Value);
                    if (cmp != 0) return cmp;
                }

                cmp = a.groupIndex.CompareTo(b.groupIndex);
                if (cmp != 0) return cmp;
                return a.adapterIndex.CompareTo(b.adapterIndex);
            });

            // Log sorted order
            var sortLogParts = new List<string>();
            for (int i = 0; i < enriched.Count; i++)
            {
                var c = enriched[i];
                string wtStr = c.writeTime.HasValue
                    ? c.writeTime.Value.ToString("HH:mm:ss.fff")
                    : "null";
                sortLogParts.Add(string.Format(
                    "#{0}:'{1}'(g{2},a{3},wt={4})",
                    i + 1, c.adapterId, c.groupIndex,
                    c.adapterIndex, wtStr));
            }

            QuickLog.Info<DataSyncDirector>(
                "LWC: [phase=SORT] key='{0}' order: [{1}]",
                key, string.Join(" → ", sortLogParts));

            // ── Phase 5: Decode best candidate ──
            QuickLog.Info<DataSyncDirector>(
                "LWC: [phase=DECODE] key='{0}' trying {1} candidate(s)",
                key, enriched.Count);

            for (int i = 0; i < enriched.Count; i++)
            {
                var candidate = enriched[i];
                try
                {
                    VerboseLog(
                        "LWC: [phase=DECODE] key='{0}' attempt #{1}/{2} "
                        + "adapter='{3}' wt={4}",
                        key, i + 1, enriched.Count,
                        candidate.adapterId,
                        candidate.writeTime?.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff") ?? "null");

                    using var ms = new MemoryStream(candidate.data);
                    var decodedPayload = await DecodeStream(ms, ct);
                    DecodeResult decoded = decodedPayload.result;

                    VerboseLog(
                        "LWC: [phase=DECODE] key='{0}' decoded "
                        + "version={1} type={2}",
                        key, decoded.Version, decoded.DataType.Name);

                    Type snapshotType = VersionRegistry.GetSnapshotType(
                        typeof(T), decoded.Version);
                    VerboseLog(
                        "LWC: [phase=DECODE] key='{0}' "
                        + "snapshotType={1}",
                        key, snapshotType.Name);

                    object snapshot = decodedPayload.translator.ConvertTo(
                        decoded.Data, snapshotType);

                    VerboseLog(
                        "LWC: [phase=DECODE] key='{0}' converted -> {1}",
                        key, snapshot?.GetType().Name ?? "null");

                    T result = (T)VersionRegistry.MigrateToCurrent(
                        snapshot, typeof(T), decoded.Version);

                    QuickLog.Info<DataSyncDirector>(
                        "LWC: [phase=DONE] key='{0}' RESOLVED -> "
                        + "adapter='{1}' wt={2} "
                        + "(attempt #{3}/{4})",
                        key, candidate.adapterId,
                        candidate.writeTime?.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff") ?? "null",
                        i + 1, enriched.Count);
                    StoreLoadedPayload(key, candidate.data);
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "LWC: [phase=DECODE] key='{0}' attempt #{1}/{2} "
                        + "adapter='{3}' FAILED: {4}",
                        key, i + 1, enriched.Count,
                        candidate.adapterId, ex.Message);
                }
            }

            QuickLog.Error<DataSyncDirector>(
                "LWC: [phase=DONE] key='{0}' ALL {1} decode(s) FAILED",
                key, enriched.Count);
            throw new SaveNotFoundException(key);
        }

        /// <summary>
        /// Loads a key using a per-key <see cref="ConflictResolutionPlan"/>.
        /// Fetches all adapters in parallel, enriches candidates with
        /// last-write times, lets the plan rank them, then decodes in ranked
        /// order until one succeeds.
        /// </summary>
        private async Task<T> LoadInternalWithPlanAsync<T>(
            string key,
            ConflictResolutionPlan plan,
            CancellationToken ct)
        {
            var entries = FlattenLoadOrder();

            QuickLog.Info<DataSyncDirector>(
                "Plan: [phase=FETCH] key='{0}' plan='{1}' launching {2} adapter(s)",
                key, plan.name, entries.Count);

            var fetchTasks = entries.Select(e =>
                TryFetchAsync(e.adapter, key, e.adapter.ReadTimeout, ct));
            var results = await Task.WhenAll(fetchTasks);

            var candidates = new List<ResolutionCandidate>();
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].data == null) continue;

                var entry = entries[i];
                VerboseLog(
                    "Plan: [phase=FETCH] adapter='{0}' key='{1}' -> {2} bytes",
                    entry.adapter.AdapterId, key, results[i].data.Length);
                candidates.Add(new ResolutionCandidate(
                    entry.adapter,
                    entry.groupIndex,
                    entry.adapterIndex,
                    results[i].data,
                    null
                ));
            }

            if (candidates.Count == 0)
            {
                QuickLog.Info<DataSyncDirector>(
                    "Plan: [phase=DONE] key='{0}' NO DATA from any adapter",
                    key);
                throw new SaveNotFoundException(key);
            }

            await EnrichPlanCandidatesWithWriteTimesAsync(key, candidates, ct);

            IReadOnlyList<ResolutionCandidate> ranked;
            try
            {
                ranked = await plan.RankCandidatesAsync(candidates, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<DataSyncDirector>(
                    "Plan '{0}' failed for key '{1}': {2}. Falling back to priority order.",
                    plan.name, key, ex.Message);
                ranked = candidates
                    .OrderBy(c => c.GroupIndex)
                    .ThenBy(c => c.AdapterIndex)
                    .ToArray();
            }

            QuickLog.Info<DataSyncDirector>(
                "Plan: [phase=SORT] key='{0}' order: [{1}]",
                key, string.Join(" -> ", ranked.Select(c => c.AdapterId)));

            foreach (ResolutionCandidate candidate in ranked)
            {
                try
                {
                    using var ms = new MemoryStream(candidate.Data);
                    var decodedPayload = await DecodeStream(ms, ct);
                    DecodeResult decoded = decodedPayload.result;

                    Type snapshotType = VersionRegistry.GetSnapshotType(
                        typeof(T), decoded.Version);
                    object snapshot = decodedPayload.translator.ConvertTo(
                        decoded.Data, snapshotType);
                    T result = (T)VersionRegistry.MigrateToCurrent(
                        snapshot, typeof(T), decoded.Version);

                    QuickLog.Info<DataSyncDirector>(
                        "Loaded key '{0}' from adapter '{1}' (plan '{2}')",
                        key, candidate.AdapterId, plan.name);
                    StoreLoadedPayload(key, candidate.Data);
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Plan load decode failed for adapter '{0}' key '{1}': {2}",
                        candidate.AdapterId, key, ex.Message);
                }
            }

            QuickLog.Error<DataSyncDirector>(
                "Plan: [phase=DONE] key='{0}' ALL {1} decode(s) FAILED",
                key, ranked.Count);
            throw new SaveNotFoundException(key);
        }

        private KeyConflictPlan FindPlanForKey(string key)
        {
            return _config?.FindPlanFor(key);
        }

        private static async Task EnrichPlanCandidatesWithWriteTimesAsync(
            string key,
            List<ResolutionCandidate> candidates,
            CancellationToken ct)
        {
            var tasks = candidates.Select(async c =>
            {
                DateTime? writeTime = null;
                try
                {
                    writeTime = await AwaitAdapterTaskAsync(
                        c.Adapter.GetLastWriteTimeAsync(key, ct),
                        c.Adapter,
                        key,
                        "last-write query",
                        ct
                    );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "Plan: write-time query failed for adapter '{0}' key '{1}': {2}",
                        c.AdapterId, key, ex.Message);
                }

                return c.WithLastWriteTime(writeTime);
            });

            var results = await Task.WhenAll(tasks);
            candidates.Clear();
            candidates.AddRange(results);
        }

        private List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>
            FlattenLoadOrder()
        {
            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            var entries = new List<(ISaveAdapter, int, int)>();
            for (int g = 0; g < loadGroups.Length; g++)
            {
                ISaveAdapter[] group = loadGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            return entries;
        }

        private static async Task<List<(ISaveAdapter adapter, int groupIndex,
            int adapterIndex, string adapterId, byte[] data, DateTime? writeTime)>>
            EnrichWithWriteTimesAsync(
                string key,
                List<(ISaveAdapter adapter, int groupIndex, int adapterIndex,
                    byte[] data)> candidates,
                CancellationToken ct)
        {
            VerboseLog(
                "LWC: [phase=WRITE_TIME] key='{0}' querying timestamps "
                + "from {1} adapter(s) in parallel",
                key, candidates.Count);

            var writeTimeTasks = candidates.Select(async c =>
            {
                DateTime? wt = null;
                try
                {
                    wt = await AwaitAdapterTaskAsync(
                        c.adapter.GetLastWriteTimeAsync(key, ct),
                        c.adapter,
                        key,
                        "last-write query",
                        ct
                    );
                    VerboseLog(
                        "LWC: [phase=WRITE_TIME] adapter='{0}' key='{1}' "
                        + "-> {2}",
                        c.adapter.AdapterId, key,
                        wt.HasValue
                            ? wt.Value.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            : "null");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DataSyncDirector>(
                        "LWC: [phase=WRITE_TIME] adapter='{0}' key='{1}' "
                        + "QUERY FAILED: {2}",
                        c.adapter.AdapterId, key, ex.Message);
                }

                return (
                    c.adapter,
                    c.groupIndex,
                    c.adapterIndex,
                    c.adapter.AdapterId,
                    c.data,
                    wt
                );
            });

            var results = await Task.WhenAll(writeTimeTasks);
            return new List<(ISaveAdapter, int, int, string, byte[], DateTime?)>(
                results
            );
        }

        private void StoreLoadedPayload(string key, byte[] payload)
        {
            if (string.IsNullOrEmpty(key) || payload == null) return;
            _lastLoadedPayloads[key] = (byte[])payload.Clone();
        }

        private static async Task<(byte[] data, bool timedOut)> TryFetchAsync(
            ISaveAdapter adapter, string key, TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                Task<Stream> readTask = adapter.OpenReadAsync(key, ct);
                if (await Task.WhenAny(readTask, Task.Delay(timeout, ct))
                    != readTask)
                {
                    ObserveAbandonedTask(readTask, DisposeStream);
                    ct.ThrowIfCancellationRequested();
                    VerboseLog(
                        "TryFetch: adapter '{0}' timed out ({1}ms) for key '{2}'",
                        adapter.AdapterId,
                        timeout.TotalMilliseconds,
                        key
                    );
                    return (null, true);
                }

                Stream stream = await readTask;
                if (stream == null)
                {
                    VerboseLog(
                        "TryFetch: adapter '{0}' returned null for key '{1}'",
                        adapter.AdapterId,
                        key
                    );
                    return (null, false);
                }

                using (stream)
                using (var ms = new MemoryStream())
                {
                    await CopyAdapterStreamToAsync(
                        stream,
                        ms,
                        adapter,
                        key,
                        "payload fetch",
                        timeout,
                        ct
                    );
                    VerboseLog(
                        "TryFetch: adapter '{0}' read {1} bytes for key '{2}'",
                        adapter.AdapterId,
                        ms.Length,
                        key
                    );
                    return (ms.ToArray(), false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                QuickLog.Warning<DataSyncDirector>(
                    "Adapter '{0}' failed to fetch key '{1}': {2}",
                    adapter.AdapterId,
                    key,
                    exception.Message
                );
                return (null, false);
            }
        }

        private async Task DeleteInternalAsync(string key, CancellationToken ct)
        {
            await RefreshAdapterAvailabilityAsync(ct);
            ISaveAdapter[][] saveGroups = _configuredSaveOrder
                ?? _saveOrderGroups;
            ISaveAdapter[][] loadGroups = _configuredLoadOrder
                ?? _loadOrderGroups;
            ISaveAdapter[] adapters = saveGroups
                .Concat(loadGroups)
                .SelectMany(group => group ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            await DeleteFromAdaptersAsync(key, adapters, ct);
        }

        private static async Task DeleteFromAdaptersAsync(
            string key,
            IEnumerable<ISaveAdapter> adapters,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A save key is required.", nameof(key));
            }

            ISaveAdapter[] uniqueAdapters = (adapters
                    ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            if (uniqueAdapters.Length == 0)
            {
                throw new SaveAdapterException(
                    "delete",
                    "No configured adapter can delete persisted data."
                );
            }

            var failures = new List<Exception>();
            foreach (ISaveAdapter adapter in uniqueAdapters)
            {
                if (!adapter.IsAvailable
                    || (adapter.SupportedFeatures & SaveAdapterFeature.Delete) == 0)
                {
                    failures.Add(new SaveAdapterException(
                        adapter.AdapterId,
                        "Configured adapter cannot delete persisted data."
                    ));
                    continue;
                }

                try
                {
                    await AwaitAdapterTaskAsync(
                        adapter.DeleteAsync(key, cancellationToken),
                        adapter,
                        key,
                        "delete",
                        cancellationToken
                    );
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    QuickLog.Warning<DataSyncDirector>(
                        "Delete adapter '{0}' failed for key '{1}': {2}",
                        adapter.AdapterId,
                        key,
                        exception.Message
                    );
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    $"One or more adapters could not delete '{key}'.",
                    failures
                );
            }
        }

        private async Task<bool> ExistsInternalAsync(
            string key,
            CancellationToken ct,
            bool requireAllConfiguredAdapters = false)
        {
            await RefreshAdapterAvailabilityAsync(ct);
            if (requireAllConfiguredAdapters)
            {
                EnsureAllConfiguredLoadAdaptersAvailable();
                return await ExistsOnEveryConfiguredAdapterAsync(key, ct);
            }

            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < loadGroups.Length; g++)
            {
                ISaveAdapter[] group = loadGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            if (entries.Count == 0)
            {
                throw new SaveAdapterException(
                    "none",
                    "No available load adapter can verify save existence."
                );
            }

            var tasks = entries.Select(async entry =>
            {
                try
                {
                    return (
                        exists: await AwaitAdapterTaskAsync(
                            entry.adapter.ExistsAsync(key, ct),
                            entry.adapter,
                            key,
                            "existence check",
                            ct
                        ),
                        groupIndex: entry.groupIndex,
                        error: (Exception)null
                    );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return (
                        exists: false,
                        groupIndex: entry.groupIndex,
                        error: exception
                    );
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();

            if (results.Any(result => result.exists))
            {
                return true;
            }

            Exception[] failures = results
                .Where(result => result.error != null)
                .Select(result => result.error)
                .ToArray();
            if (failures.Length > 0)
            {
                throw new AggregateException(
                    $"Could not determine whether save key '{key}' exists.",
                    failures
                );
            }

            return false;
        }

        private async Task<bool> ExistsOnEveryConfiguredAdapterAsync(
            string key,
            CancellationToken cancellationToken)
        {
            ISaveAdapter[] adapters = (_configuredLoadOrder
                    ?? _loadOrderGroups)
                .SelectMany(group => group ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            var tasks = adapters.Select(async adapter =>
            {
                try
                {
                    bool exists = await AwaitAdapterTaskAsync(
                        adapter.ExistsAsync(key, cancellationToken),
                        adapter,
                        key,
                        "strict existence check",
                        cancellationToken
                    );
                    return (exists, error: (Exception)null);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return (exists: false, error: exception);
                }
            }).ToArray();
            var results = await Task.WhenAll(tasks);
            Exception[] failures = results
                .Where(result => result.error != null)
                .Select(result => result.error)
                .ToArray();
            if (failures.Length > 0)
            {
                throw new AggregateException(
                    $"Could not verify '{key}' on every configured loader.",
                    failures
                );
            }

            int existingAdapterCount = results.Count(result => result.exists);
            if (existingAdapterCount > 0
                && existingAdapterCount != results.Length)
            {
                throw new SaveAdapterException(
                    "strict",
                    $"Save key '{key}' exists on {existingAdapterCount}/"
                    + $"{results.Length} configured load adapters."
                );
            }

            return existingAdapterCount > 0;
        }

        private async Task<Stream> OpenReadStreamInternalAsync(string key, CancellationToken ct)
        {
            await RefreshAdapterAvailabilityAsync(ct);

            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            var entries = new List<(ISaveAdapter adapter, int groupIndex, int adapterIndex)>();
            for (int g = 0; g < loadGroups.Length; g++)
            {
                ISaveAdapter[] group = loadGroups[g];
                for (int a = 0; a < group.Length; a++)
                    entries.Add((group[a], g, a));
            }

            var tasks = entries.Select(async e =>
            {
                try
                {
                    var readTask = e.adapter.OpenReadAsync(key, ct);
                    if (await Task.WhenAny(readTask, Task.Delay(e.adapter.ReadTimeout, ct)) != readTask)
                    {
                        ObserveAbandonedTask(readTask, DisposeStream);
                        ct.ThrowIfCancellationRequested();
                        return null;
                    }
                    return await readTask;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return null;
                }
            }).ToArray();

            Stream[] results;
            try
            {
                results = await Task.WhenAll(tasks);
            }
            catch
            {
                foreach (Task<Stream> task in tasks)
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result?.Dispose();
                    }
                }

                throw;
            }

            if (ct.IsCancellationRequested)
            {
                foreach (Stream stream in results) stream?.Dispose();
                ct.ThrowIfCancellationRequested();
            }

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

        private async Task WriteStreamInternalAsync(
            string key,
            Stream data,
            CancellationToken ct,
            bool requireAllConfiguredGroups)
        {
            await RefreshAdapterAvailabilityAsync(ct);
            ISaveAdapter[][] saveGroups = _saveOrderGroups;
            if (saveGroups.Length == 0)
            {
                throw new SaveAdapterException(
                    "none",
                    "No save groups configured."
                );
            }

            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.CanSeek) data.Position = 0;
            using var payloadBuffer = new MemoryStream();
            await data.CopyToAsync(payloadBuffer, 81920, ct);
            byte[] payload = payloadBuffer.ToArray();

            if (requireAllConfiguredGroups)
            {
                await WriteStrictPayloadToGroupsAsync(
                    key,
                    payload,
                    _configuredSaveOrder ?? saveGroups,
                    _configuredLoadOrder ?? _loadOrderGroups,
                    ct
                );
                return;
            }

            var groupTasks = new Task<(
                bool success,
                ISaveAdapter adapter,
                int groupIndex)>[saveGroups.Length];
            for (int groupIndex = 0;
                 groupIndex < saveGroups.Length;
                 groupIndex++)
            {
                groupTasks[groupIndex] = TryWriteToGroupAsync(
                    key,
                    payload,
                    groupIndex,
                    saveGroups[groupIndex],
                    ct
                );
            }

            var results = await Task.WhenAll(groupTasks);
            int successCount = results.Count(result => result.success);
            if (successCount == 0)
            {
                throw new SaveAdapterException(
                    "stream",
                    $"Raw write for '{key}' succeeded in {successCount}/"
                    + $"{saveGroups.Length} save groups."
                );
            }
        }

        private async Task VerifyPayloadInternalAsync(
            string key,
            byte[] expectedPayload,
            CancellationToken cancellationToken)
        {
            if (expectedPayload == null)
            {
                throw new ArgumentNullException(nameof(expectedPayload));
            }

            await RefreshAdapterAvailabilityAsync(cancellationToken);
            EnsureAllConfiguredLoadAdaptersAvailable();
            ISaveAdapter[] adapters = (_configuredLoadOrder
                    ?? _loadOrderGroups)
                .SelectMany(group => group ?? Array.Empty<ISaveAdapter>())
                .Where(adapter => adapter != null)
                .Distinct(SaveAdapterReferenceComparer.Instance)
                .ToArray();
            foreach (ISaveAdapter adapter in adapters)
            {
                await VerifyAdapterPayloadAsync(
                    adapter,
                    key,
                    expectedPayload,
                    cancellationToken
                );
            }
        }

        private async Task EnsureExactPayloadInternalAsync(
            string key,
            byte[] expectedPayload,
            CancellationToken cancellationToken)
        {
            await RefreshAdapterAvailabilityAsync(cancellationToken);
            await EnsureExactPayloadInGroupsAsync(
                key,
                expectedPayload,
                _configuredSaveOrder ?? _saveOrderGroups,
                _configuredLoadOrder ?? _loadOrderGroups,
                cancellationToken
            );
        }

        private static async Task EnsureExactPayloadInGroupsAsync(
            string key,
            byte[] expectedPayload,
            ISaveAdapter[][] saveGroups,
            ISaveAdapter[][] loadGroups,
            CancellationToken cancellationToken = default)
        {
            ValidateStrictWriteArguments(
                key,
                expectedPayload,
                saveGroups,
                loadGroups
            );

            SaveAdapterReferenceComparer comparer
                = SaveAdapterReferenceComparer.Instance;
            var saveAdapters = new HashSet<ISaveAdapter>(comparer);
            foreach (ISaveAdapter[] group in saveGroups)
            {
                foreach (ISaveAdapter adapter in group)
                {
                    if (adapter != null) saveAdapters.Add(adapter);
                }
            }

            ISaveAdapter[] loadAdapters = loadGroups
                .SelectMany(group => group)
                .Where(adapter => adapter != null)
                .Distinct(comparer)
                .ToArray();
            ValidateStrictWriteTopology(
                saveGroups,
                loadAdapters,
                saveAdapters
            );

            var inspections = new List<PayloadGroupInspection>(
                saveGroups.Length
            );
            ISaveAdapter[] selectedSaveWriters = new ISaveAdapter[
                saveGroups.Length
            ];
            var missingLoadAdapters = new List<ISaveAdapter>();
            var loadInspectionFailures = new List<Exception>();

            foreach (ISaveAdapter adapter in loadAdapters)
            {
                try
                {
                    PayloadPresence presence = await InspectAdapterPayloadAsync(
                        adapter,
                        key,
                        expectedPayload,
                        cancellationToken
                    );
                    if (presence == PayloadPresence.Missing)
                    {
                        missingLoadAdapters.Add(adapter);
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (SaveAdapterException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    loadInspectionFailures.Add(exception);
                }
            }

            if (loadInspectionFailures.Count > 0)
            {
                throw new AggregateException(
                    $"Could not safely inspect every load adapter for '{key}'.",
                    loadInspectionFailures
                );
            }

            await InspectSaveGroupsAsync(
                saveGroups,
                expectedPayload,
                inspections,
                selectedSaveWriters,
                key,
                cancellationToken
            );

            foreach (ISaveAdapter adapter in missingLoadAdapters)
            {
                PayloadPresence currentPresence = await InspectAdapterPayloadAsync(
                    adapter,
                    key,
                    expectedPayload,
                    cancellationToken
                );
                if (currentPresence == PayloadPresence.Exact) continue;

                await WriteAndVerifyStrictAdapterAsync(
                    adapter,
                    key,
                    expectedPayload,
                    cancellationToken
                );
            }

            foreach (PayloadGroupInspection inspection in inspections)
            {
                selectedSaveWriters[inspection.GroupIndex]
                    = await RepairMissingSaveGroupAsync(
                    key,
                    expectedPayload,
                    inspection,
                    cancellationToken
                );
            }

            foreach (ISaveAdapter adapter in loadAdapters
                         .Concat(selectedSaveWriters)
                         .Where(adapter => adapter != null)
                         .Distinct(comparer))
            {
                await VerifyAdapterPayloadAsync(
                    adapter,
                    key,
                    expectedPayload,
                    cancellationToken
                );
            }
        }

        private static async Task<PayloadPresence> InspectAdapterPayloadAsync(
            ISaveAdapter adapter,
            string key,
            byte[] expectedPayload,
            CancellationToken cancellationToken)
        {
            EnsureStrictAdapterCapability(adapter, "load");
            using Stream stream = await AwaitAdapterTaskAsync(
                adapter.OpenReadAsync(key, cancellationToken),
                adapter,
                key,
                "payload inspection",
                cancellationToken,
                DisposeStream
            );
            if (stream == null) return PayloadPresence.Missing;

            using var buffer = new MemoryStream();
            await CopyAdapterStreamToAsync(
                stream,
                buffer,
                adapter,
                key,
                "payload inspection",
                adapter.ReadTimeout,
                cancellationToken
            );
            if (buffer.ToArray().SequenceEqual(expectedPayload))
            {
                return PayloadPresence.Exact;
            }

            throw new SaveAdapterException(
                adapter.AdapterId,
                $"A different payload already exists for '{key}'; repair was aborted."
            );
        }

        private static async Task InspectSaveGroupsAsync(
            ISaveAdapter[][] saveGroups,
            byte[] expectedPayload,
            List<PayloadGroupInspection> inspections,
            ISaveAdapter[] selectedWriters,
            string key,
            CancellationToken cancellationToken)
        {
            for (int groupIndex = 0;
                 groupIndex < saveGroups.Length;
                 groupIndex++)
            {
                ISaveAdapter[] group = saveGroups[groupIndex]
                    ?? Array.Empty<ISaveAdapter>();
                var prioritizedWriters = new List<ISaveAdapter>();
                var failures = new List<Exception>();
                foreach (ISaveAdapter adapter in group)
                {
                    if (!CanStrictlyWrite(adapter)) continue;

                    try
                    {
                        PayloadPresence presence = await InspectAdapterPayloadAsync(
                            adapter,
                            key,
                            expectedPayload,
                            cancellationToken
                        );
                        if (presence == PayloadPresence.Exact)
                        {
                            if (prioritizedWriters.Count == 0)
                            {
                                selectedWriters[groupIndex] = adapter;
                            }
                            else
                            {
                                prioritizedWriters.Add(adapter);
                            }

                            break;
                        }

                        prioritizedWriters.Add(adapter);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                if (selectedWriters[groupIndex] != null) continue;
                if (prioritizedWriters.Count == 0)
                {
                    throw new AggregateException(
                        $"Save group {groupIndex} has no safe exact-payload "
                        + $"writer for '{key}'.",
                        failures
                    );
                }

                inspections.Add(PayloadGroupInspection.Missing(
                    groupIndex,
                    prioritizedWriters.ToArray()
                ));
            }
        }

        private static async Task<ISaveAdapter> RepairMissingSaveGroupAsync(
            string key,
            byte[] expectedPayload,
            PayloadGroupInspection inspection,
            CancellationToken cancellationToken)
        {
            var failures = new List<Exception>();
            foreach (ISaveAdapter adapter in inspection.WriteAdapters)
            {
                try
                {
                    PayloadPresence presence = await InspectAdapterPayloadAsync(
                        adapter,
                        key,
                        expectedPayload,
                        cancellationToken
                    );
                    if (presence == PayloadPresence.Exact) return adapter;

                    await WriteAndVerifyStrictAdapterAsync(
                        adapter,
                        key,
                        expectedPayload,
                        cancellationToken
                    );
                    return adapter;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            throw new AggregateException(
                $"No adapter could repair '{key}' in save group "
                + $"{inspection.GroupIndex}.",
                failures
            );
        }

        private static async Task<T> AwaitAdapterTaskAsync<T>(
            Task<T> adapterTask,
            ISaveAdapter adapter,
            string key,
            string operation,
            CancellationToken cancellationToken,
            Action<T> lateResultCleanup = null)
        {
            Task timeoutTask = Task.Delay(
                adapter.ReadTimeout,
                cancellationToken
            );
            if (await Task.WhenAny(adapterTask, timeoutTask) == adapterTask)
            {
                return await adapterTask;
            }

            ObserveAbandonedTask(adapterTask, lateResultCleanup);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Adapter '{adapter.AdapterId}' timed out during {operation} "
                + $"for '{key}'."
            );
        }

        private static async Task CopyAdapterStreamToAsync(
            Stream source,
            Stream destination,
            ISaveAdapter adapter,
            string key,
            string operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Task copyTask = source.CopyToAsync(
                destination,
                81920,
                cancellationToken
            );
            Task timeoutTask = Task.Delay(timeout, cancellationToken);
            if (await Task.WhenAny(copyTask, timeoutTask) == copyTask)
            {
                await copyTask;
                return;
            }

            source.Dispose();
            ObserveAbandonedTask(copyTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Adapter '{adapter.AdapterId}' timed out during {operation} "
                + $"for '{key}'."
            );
        }

        private static void ObserveAbandonedTask(Task task)
        {
            _ = task.ContinueWith(
                completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        _ = completedTask.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        private static void ObserveAbandonedTask<T>(
            Task<T> task,
            Action<T> lateResultCleanup)
        {
            _ = task.ContinueWith(
                completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        _ = completedTask.Exception;
                        return;
                    }

                    if (completedTask.Status == TaskStatus.RanToCompletion)
                    {
                        try
                        {
                            lateResultCleanup?.Invoke(completedTask.Result);
                        }
                        catch (Exception exception)
                        {
                            QuickLog.Warning<DataSyncDirector>(
                                "Late adapter result cleanup failed: {0}",
                                exception.Message
                            );
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        private static void DisposeStream(Stream stream)
        {
            stream?.Dispose();
        }

        #endregion

        #region Adapter / Translator Resolution

        public ISaveAdapter ResolveAdapter()
        {
            ISaveAdapter[][] loadGroups = _loadOrderGroups;
            if (loadGroups.Length > 0 && loadGroups[0].Length > 0)
            {
                return loadGroups[0][0];
            }

            throw new SaveAdapterException(
                "none",
                "No adapters configured"
            );
        }

        public ISaveAdapter[] ActiveAdapters
        {
            get
            {
                ISaveAdapter[][] saveGroups = _saveOrderGroups;
                ISaveAdapter[][] loadGroups = _loadOrderGroups;
                return saveGroups
                    .SelectMany(group => group)
                    .Union(loadGroups.SelectMany(group => group))
                    .Distinct()
                    .ToArray();
            }
        }

        public ISaveTranslator ResolveTranslator()
            => _translators.FirstOrDefault()
                ?? throw new TranslationException("No translators configured");

        #endregion

        #region Decode Pipeline

        private async Task<(DecodeResult result, ISaveTranslator translator)>
            DecodeStream(Stream stream, CancellationToken ct)
        {
            ISaveTranslator translator = ResolveTranslator();

            VerboseLog(
                "DecodeStream: trying primary translator '{0}', stream length={1}",
                translator.FormatId, stream.Length);

            try
            {
                DecodeResult result = await translator.DecodeAsync(stream, ct);
                VerboseLog(
                    "DecodeStream: primary translator '{0}' succeeded, version={1}",
                    translator.FormatId, result.Version);
                return (result, translator);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<DataSyncDirector>(
                    "Primary translator decode failed: {0}",
                    ex.Message
                );
            }

            VerboseLog(
                "DecodeStream: scanning {0} translators for signature match",
                _translators.Count);

            foreach (ISaveTranslator t in _translators)
            {
                stream.Position = 0;
                if (!TryMatchSignature(stream, t.Signature))
                {
                    VerboseLog(
                        "DecodeStream: signature mismatch for '{0}'", t.FormatId);
                    continue;
                }

                VerboseLog(
                    "DecodeStream: signature matched '{0}', attempting decode", t.FormatId);

                stream.Position = 0;
                DecodeResult result = await t.DecodeAsync(stream, ct);
                return (result, t);
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

        #region Nested Types

        private enum PayloadPresence
        {
            Exact,
            Missing
        }

        private readonly struct PayloadGroupInspection
        {
            public int GroupIndex { get; }

            public ISaveAdapter[] WriteAdapters { get; }

            private PayloadGroupInspection(
                int groupIndex,
                ISaveAdapter[] writeAdapters)
            {
                GroupIndex = groupIndex;
                WriteAdapters = writeAdapters;
            }

            public static PayloadGroupInspection Missing(
                int groupIndex,
                ISaveAdapter[] writeAdapters)
            {
                return new PayloadGroupInspection(
                    groupIndex,
                    writeAdapters
                );
            }
        }

        private sealed class SaveAdapterReferenceComparer
            : IEqualityComparer<ISaveAdapter>
        {
            internal static SaveAdapterReferenceComparer Instance { get; }
                = new SaveAdapterReferenceComparer();

            public bool Equals(ISaveAdapter left, ISaveAdapter right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(ISaveAdapter adapter)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers
                    .GetHashCode(adapter);
            }
        }

        #endregion
    }
}
