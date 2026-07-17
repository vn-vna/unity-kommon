using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public class KvBudget
    {
        #region Interfaces & Properties

        public string Name { get; }

        internal string DataKey => $"kv_{Name}";

        public bool IsLoaded { get; private set; }

        public ICollection<string> Keys => _entries.Keys;

        #endregion

        #region Private Fields

        private readonly ConcurrentDictionary<string, string> _entries = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        #endregion

        #region Construction

        internal KvBudget(string name)
        {
            Name = name;
        }

        internal async Task LoadAsync(CancellationToken ct = default)
        {
            await DataSyncDirector.ReadyTask;

            try
            {
                KvStoreContainer container = await DataSyncDirector.Instance
                    .LoadAsync<KvStoreContainer>(DataKey, ct);

                foreach (KvEntry entry in container.entries)
                    _entries[entry.key] = entry.serializedValue;
            }
            catch (SaveNotFoundException) { }

            IsLoaded = true;
        }

        #endregion

        #region Async API

        public async Task SetAsync<T>(
            string key,
            T value,
            CancellationToken ct = default)
        {
            EnsureLoaded();

            string serialized = SerializeValue(value);
            _entries[key] = serialized;
            await SaveAsync(ct);
        }

        public Task<T> GetAsync<T>(
            string key,
            T defaultValue = default,
            CancellationToken ct = default)
        {
            EnsureLoaded();

            if (_entries.TryGetValue(key, out string serialized))
                return Task.FromResult(
                    DeserializeValue<T>(serialized, defaultValue)
                );

            return Task.FromResult(defaultValue);
        }

        public Task<bool> HasKeyAsync(
            string key,
            CancellationToken ct = default)
        {
            EnsureLoaded();
            return Task.FromResult(_entries.ContainsKey(key));
        }

        public async Task DeleteAsync(
            string key,
            CancellationToken ct = default)
        {
            EnsureLoaded();
            _entries.TryRemove(key, out _);
            await SaveAsync(ct);
        }

        public async Task ForceSyncAsync(CancellationToken ct = default)
        {
            EnsureLoaded();
            await SaveAsync(ct);
        }

        #endregion

        #region Sync API (no I/O — budget must be loaded)

        public T Get<T>(string key, T defaultValue = default)
        {
            EnsureLoaded();

            if (_entries.TryGetValue(key, out string serialized))
                return DeserializeValue<T>(serialized, defaultValue);

            return defaultValue;
        }

        public bool HasKey(string key)
        {
            EnsureLoaded();
            return _entries.ContainsKey(key);
        }

        public void Set<T>(string key, T value)
        {
            EnsureLoaded();
            _entries[key] = SerializeValue(value);
        }

        public void Delete(string key)
        {
            EnsureLoaded();
            _entries.TryRemove(key, out _);
        }

        #endregion

        #region Fire-and-Forget (safe for Update / non-async callers)

        public async void Save<T>(
            string key,
            T value,
            Action<bool> onComplete = null)
        {
            try
            {
                await SetAsync(key, value);
                onComplete?.Invoke(true);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KvBudget:{Name}] Save failed for '{key}': {ex}"
                );
                onComplete?.Invoke(false);
            }
        }

        public async void Load<T>(
            string key,
            Action<T> callback,
            T defaultValue = default)
        {
            try
            {
                T result = await GetAsync(key, defaultValue);
                callback?.Invoke(result);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KvBudget:{Name}] Load failed for '{key}': {ex}"
                );
                callback?.Invoke(defaultValue);
            }
        }

        public async void Remove(
            string key,
            Action<bool> onComplete = null)
        {
            try
            {
                await DeleteAsync(key);
                onComplete?.Invoke(true);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KvBudget:{Name}] Remove failed for '{key}': {ex}"
                );
                onComplete?.Invoke(false);
            }
        }

        #endregion

        #region Private Methods

        private void EnsureLoaded()
        {
            if (!IsLoaded)
            {
                throw new InvalidOperationException(
                    $"Budget '{Name}' is not loaded. "
                    + "Use KvStore.OpenAsync() first."
                );
            }
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            await _saveLock.WaitAsync(ct);
            try
            {
                KvStoreContainer container = new KvStoreContainer
                {
                    entries = _entries
                        .Select(kvp => new KvEntry
                        {
                            key = kvp.Key,
                            serializedValue = kvp.Value
                        })
                        .ToList()
                };

                await DataSyncDirector.Instance.SaveAsync(
                    DataKey, container, ct
                );
            }
            finally
            {
                _saveLock.Release();
            }
        }

        #endregion

        #region Serialization Helpers

        private static string SerializeValue<T>(T value)
        {
            if (value == null) return string.Empty;
            return value is IConvertible convertible
                ? convertible.ToString(CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static T DeserializeValue<T>(
            string serialized,
            T defaultValue)
        {
            if (string.IsNullOrEmpty(serialized)) return defaultValue;

            Type targetType = typeof(T);
            if (targetType == typeof(string)) return (T)(object)serialized;

            if (
                targetType == typeof(int) &&
                int.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intResult)
            )
            {
                return (T)(object)intResult;
            }

            if (
                targetType == typeof(float) &&
                float.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatResult)
            )
            {
                return (T)(object)floatResult;
            }

            if (targetType == typeof(bool)
                && bool.TryParse(serialized, out bool boolResult))
                return (T)(object)boolResult;

            if (targetType == typeof(long)
                && long.TryParse(
                    serialized,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long longResult))
                return (T)(object)longResult;

            if (targetType == typeof(double)
                && double.TryParse(
                    serialized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double doubleResult))
                return (T)(object)doubleResult;

            if (typeof(IConvertible).IsAssignableFrom(targetType))
            {
                try
                {
                    return (T)Convert.ChangeType(
                        serialized,
                        targetType,
                        CultureInfo.InvariantCulture
                    );
                }
                catch { }
            }

            return defaultValue;
        }

        #endregion
    }
}
