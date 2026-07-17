using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public static class KvStore
    {
        #region Private Fields

        private static readonly ConcurrentDictionary<string, KvBudget> _openBudgets
            = new ConcurrentDictionary<string, KvBudget>();

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _budgetSemaphores
            = new ConcurrentDictionary<string, SemaphoreSlim>();

        private static string _defaultBudgetName;
        private static KvBudget _defaultBudget;

        #endregion

        #region Properties

        public static ICollection<string> OpenBudgets
            => _openBudgets.Keys;

        public static KvBudget DefaultBudget
        {
            get
            {
                if (_defaultBudget == null)
                {
                    throw new InvalidOperationException(
                        "No default budget set. "
                        + "Call KvStore.SetDefaultBudgetAsync() first."
                    );
                }

                return _defaultBudget;
            }
        }

        #endregion

        #region Public Methods

        public static async Task<KvBudget> OpenAsync(
            string name,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            if (_openBudgets.TryGetValue(name, out KvBudget existing)
                && existing.IsLoaded)
                return existing;

            SemaphoreSlim sem = _budgetSemaphores.GetOrAdd(
                name,
                _ => new SemaphoreSlim(1, 1)
            );

            await sem.WaitAsync(ct);
            try
            {
                if (_openBudgets.TryGetValue(name, out existing)
                    && existing.IsLoaded)
                    return existing;

                KvBudget budget = new KvBudget(name);
                await budget.LoadAsync(ct);
                _openBudgets[name] = budget;
                return budget;
            }
            finally
            {
                sem.Release();
            }
        }

        public static async Task CloseAsync(
            string name,
            CancellationToken ct = default)
        {
            if (_openBudgets.TryRemove(name, out KvBudget budget))
                await budget.ForceSyncAsync(ct);
        }

        public static async Task ForceSyncAllAsync(
            CancellationToken ct = default)
        {
            var syncTasks = new List<Task>();

            foreach (KvBudget budget in _openBudgets.Values)
                syncTasks.Add(budget.ForceSyncAsync(ct));

            await Task.WhenAll(syncTasks);
        }

        public static bool TryGetBudget(
            string name,
            out KvBudget budget)
        {
            return _openBudgets.TryGetValue(name, out budget)
                && budget.IsLoaded;
        }

        #endregion

        #region Default Budget

        public static async Task SetDefaultBudgetAsync(
            string name,
            CancellationToken ct = default)
        {
            _defaultBudgetName = name;
            _defaultBudget = await OpenAsync(name, ct);
        }

        public static void SetDefaultBudget(KvBudget budget)
        {
            if (budget == null)
                throw new ArgumentNullException(nameof(budget));

            _defaultBudgetName = budget.Name;
            _defaultBudget = budget;
        }

        #endregion

        #region Static Convenience API (uses DefaultBudget)

        // --- Async ---

        public static Task SetAsync<T>(
            string key,
            T value,
            CancellationToken ct = default)
            => DefaultBudget.SetAsync(key, value, ct);

        public static Task<T> GetAsync<T>(
            string key,
            T defaultValue = default,
            CancellationToken ct = default)
            => DefaultBudget.GetAsync(key, defaultValue, ct);

        public static Task DeleteAsync(
            string key,
            CancellationToken ct = default)
            => DefaultBudget.DeleteAsync(key, ct);

        public static Task<bool> HasKeyAsync(
            string key,
            CancellationToken ct = default)
            => DefaultBudget.HasKeyAsync(key, ct);

        public static Task ForceSyncAsync(
            CancellationToken ct = default)
            => DefaultBudget.ForceSyncAsync(ct);

        // --- Sync (dict-only, no I/O) ---

        public static T Get<T>(
            string key,
            T defaultValue = default)
            => DefaultBudget.Get(key, defaultValue);

        public static bool HasKey(string key)
            => DefaultBudget.HasKey(key);

        public static void Set<T>(string key, T value)
            => DefaultBudget.Set(key, value);

        public static void Delete(string key)
            => DefaultBudget.Delete(key);

        // --- Fire-and-Forget ---

        public static void Save<T>(
            string key,
            T value,
            Action<bool> onComplete = null)
            => DefaultBudget.Save(key, value, onComplete);

        public static void Load<T>(
            string key,
            Action<T> callback,
            T defaultValue = default)
            => DefaultBudget.Load(key, callback, defaultValue);

        public static void Remove(
            string key,
            Action<bool> onComplete = null)
            => DefaultBudget.Remove(key, onComplete);

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject(
                "[Scheherazade KV Store Director]"
            );
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<KvStoreDirector>();
        }

        #endregion
    }
}
