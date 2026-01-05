#if FIREBASE_REMOTE
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.LocalSave;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using Firebase.RemoteConfig;
using Newtonsoft.Json.Linq;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public enum FirebaseRemoteValueType { String, Boolean, Integer, Float }

    public struct FirebaseRemoteDefaultValue
    {
        public string Key;
        public FirebaseRemoteValueType Type;
        public string Value;
    }

    public class FirebaseRemoteConfigProvider : IRemoteConfigProvider
    {
        private const string FirebaseCachedConfigKey = "__firebase_cached_config__";

        public int Priority => 0;
        public bool IsInitialized { get; private set; }
        public bool IsReady { get; set; }
        public IRemoteConfigManager Manager { get; set; }

        private Firebase.FirebaseApp _app;
        private int _tryCount = 0;
        private int? _refreshCount = null;
        private List<(string, FirebaseRemoteValueType)> _cachedKeys;

        public FirebaseRemoteConfigProvider()
        {
            _tryCount = 0;
        }

        public void Initialize()
        {
            if (_tryCount++ > 3)
            {
                QuickLog.Critical<FirebaseRemoteConfigProvider>(
                    "Firebase Remote Config initialization failed after multiple attempts."
                );
                return;
            }

            IsInitialized = false;
            IsReady = false;

            _cachedKeys = new List<(string, FirebaseRemoteValueType)>();

            FirebaseRemoteConfig
                .DefaultInstance
                .ActivateAsync()
                .ContinueTaskOnMainThread(HandleActiveTaskCompleted);
        }

        private void HandleActiveTaskCompleted(Task<bool> task)
        {
            if (task.IsCompleted && !task.IsFaulted)
            {
                PerformInitialize();
                return;
            }

            QuickLog.Critical<FirebaseRemoteConfigProvider>(
                "Firebase Remote Config activation failed: {0}",
                task.Exception
            );

            Dispatcher
                .DispatchDelayedOnMainThread(
                    () => Initialize(),
                    1.0f
                );
        }

        private async Task<Dictionary<string, object>> GetDefaults()
        {
            Dictionary<string, object> defaults = new Dictionary<string, object>();
            // Find All Types
            PropertyInfo[] properties = Manager.Config.GetType().GetProperties();
            foreach (PropertyInfo pi in properties)
            {
                RemoteConfigAttribute attribute = pi.GetCustomAttribute<RemoteConfigAttribute>();
                if (attribute == null) continue;
                defaults.Add(attribute.Key, attribute.DefaultValue);
            }

            // Add Cached Config Key
            if (LocalFileHandler.Exists(FirebaseCachedConfigKey))
            {
                try
                {
                    await TryApplyCachedDefaults(defaults);
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<FirebaseRemoteConfigProvider>(
                        "Failed to load cached Firebase Remote Config: {0}",
                        ex
                    );
                }
            }

            return defaults;
        }

        private static async Task TryApplyCachedDefaults(Dictionary<string, object> defaults)
        {
            string path = LocalFileHandler.GetFilePath(FirebaseCachedConfigKey);
            string json = await File.ReadAllTextAsync(path);
            JObject jObject = JObject.Parse(json);

            List<string> keys = new List<string>(defaults.Keys);
            foreach (string key in keys)
            {
                switch (defaults[key])
                {
                    case string:
                        defaults[key] = jObject[key]?.ToString() ?? defaults[key];
                        break;
                    case bool:
                        defaults[key] = jObject[key]?.ToObject<bool>() ?? defaults[key];
                        break;
                    case int:
                        defaults[key] = jObject[key]?.ToObject<int>() ?? defaults[key];
                        break;
                    case float:
                        defaults[key] = jObject[key]?.ToObject<float>() ?? defaults[key];
                        break;
                }
            }
        }

        private void PerformInitialize()
        {
            GetDefaults().ContinueTaskOnMainThread(HandlePerformGetDefaultsTaskCompleted);
        }

        private void HandlePerformGetDefaultsTaskCompleted(Task<Dictionary<string, object>> task)
        {
            _cachedKeys.Clear();

            foreach ((string key, object value) in task.Result)
            {
                if (value is string)
                {
                    _cachedKeys.Add((key, FirebaseRemoteValueType.String));
                }
                else if (value is bool)
                {
                    _cachedKeys.Add((key, FirebaseRemoteValueType.Boolean));
                }
                else if (value is int)
                {
                    _cachedKeys.Add((key, FirebaseRemoteValueType.Integer));
                }
                else if (value is float)
                {
                    _cachedKeys.Add((key, FirebaseRemoteValueType.Float));
                }
            }

            FirebaseRemoteConfig
                .DefaultInstance
                .SetDefaultsAsync(task.Result)
                .ContinueTaskOnMainThread(HandleFirebaseSetDefaultTaskCompleted);

        }

        private void HandleFirebaseSetDefaultTaskCompleted(Task task)
        {
            IsInitialized = task.IsCompleted && !task.IsFaulted;

            QuickLog.Info<FirebaseRemoteConfigProvider>(
                "Firebase Remote Config initialized: {0}",
                IsInitialized
            );

            if (!IsInitialized)
            {
                Dispatcher
                    .DispatchDelayedOnMainThread(() => Initialize(), 1.0f);
                return;
            }

            _refreshCount = null;
        }

        public void Refresh()
        {
            if (!_refreshCount.HasValue)
            {
                _refreshCount = 0;
            }

            if (_tryCount++ > 3)
            {
                QuickLog.Critical<FirebaseRemoteConfigProvider>(
                    "Firebase Remote Config fetch failed after multiple attempts."
                );
                return;
            }

            IsReady = false;
            FirebaseRemoteConfig
                .DefaultInstance
                .FetchAsync(TimeSpan.Zero)
                .ContinueTaskOnMainThread(HandleFirebaseFetchTaskCompleted);
        }

        private async void HandleFirebaseFetchTaskCompleted(Task task)
        {
            switch (FirebaseRemoteConfig.DefaultInstance.Info.LastFetchStatus)
            {
                case LastFetchStatus.Success:
                    IsReady = true;
                    break;

                case LastFetchStatus.Pending:
                    IsReady = false;
                    break;

                case LastFetchStatus.Failure:
                    QuickLog.Warning<FirebaseRemoteConfigProvider>(
                        "Firebase Remote Config fetch failed."
                    );
                    Dispatcher.DispatchOnMainThread(Refresh);
                    return;
            }

            // Save Cached Config
            try
            {
                CacheRemoteConfig().ContinueTaskOnMainThread(HandleConfigCachedTaskCompleted);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<FirebaseRemoteConfigProvider>(
                    "Failed to save cached Firebase Remote Config: {0}",
                    ex
                );
            }
        }

        private async Task CacheRemoteConfig()
        {
            string path = LocalFileHandler.GetFilePath(FirebaseCachedConfigKey);
            JObject jObject = new JObject();
            foreach ((string key, FirebaseRemoteValueType type) in _cachedKeys)
            {
                switch (type)
                {
                    case FirebaseRemoteValueType.String:
                        jObject[key] = FirebaseRemoteConfig.DefaultInstance.GetValue(key).StringValue;
                        break;
                    case FirebaseRemoteValueType.Boolean:
                        jObject[key] = FirebaseRemoteConfig.DefaultInstance.GetValue(key).BooleanValue;
                        break;
                    case FirebaseRemoteValueType.Integer:
                        jObject[key] = FirebaseRemoteConfig.DefaultInstance.GetValue(key).LongValue;
                        break;
                    case FirebaseRemoteValueType.Float:
                        jObject[key] = FirebaseRemoteConfig.DefaultInstance.GetValue(key).DoubleValue;
                        break;
                }
            }
            await File.WriteAllTextAsync(path, jObject.ToString());
        }

        private void HandleConfigCachedTaskCompleted(Task task)
        {
            if (task.IsCompleted && !task.IsFaulted)
            {
                QuickLog.Info<FirebaseRemoteConfigProvider>(
                    "Firebase Remote Config cached successfully."
                );
                return;
            }

            QuickLog.Warning<FirebaseRemoteConfigProvider>(
                "Failed to cache Firebase Remote Config: {0}",
                task.Exception
            );
        }

        public bool TryGetConfig<T>(string key, out T result)
        {
            try
            {
                return AcquireSingleConfig(
                    FirebaseRemoteConfig.DefaultInstance.GetValue(key),
                    out result
                );
            }
            catch (Exception ex)
            {
                QuickLog.Warning<FirebaseRemoteConfigProvider>(
                    "Failed to get remote config value for key {0}: {1}",
                    key, ex
                );
                result = default;
                return false;
            }
        }

        private static bool AcquireSingleConfig<T>(ConfigValue v, out T result)
        {
            switch (typeof(T))
            {
                case Type t when t == typeof(string):
                    result = (T)(object)v.StringValue;
                    return true;

                case Type t when t == typeof(bool):
                    result = (T)Convert.ChangeType(v.BooleanValue, typeof(T));
                    return true;

                case Type t when t == typeof(int):
                    result = (T)Convert.ChangeType(v.LongValue, typeof(T));
                    return true;

                case Type t when t == typeof(float):
                    result = (T)Convert.ChangeType(v.DoubleValue, typeof(T));
                    return true;

                default:
                    result = default;
                    return false;
            }
        }
    }
}

#endif