using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public enum MockRemoteValueType { String, Boolean, Integer, Float }

    [Serializable]
    public struct MockRemoteDefaultValue
    {
        public string Key;
        public MockRemoteValueType Type;
        public string Value;
    }

    [CreateAssetMenu(
        fileName = "MockRemoteConfigProvider",
        menuName = "Scheherazade/Remote Config Providers/Mock"
    )]
    public class MockRemoteConfigProvider :
        ScriptableObject,
        IRemoteConfigProvider
    {
        #region Interfaces & Properties

        public int Priority => -100;
        public bool IsInitialized { get; private set; }
        public bool IsReady { get; private set; }
        public IRemoteConfigManager Manager { get; set; }

        #endregion

        #region Serialized Fields

        [SerializeField]
        [Tooltip("Default values applied on Initialize. These can be overridden at runtime via SetValue.")]
        private List<MockRemoteDefaultValue> defaultValues = new();

        #endregion

        #region Private Fields

        private readonly Dictionary<string, object> _overrides = new();
        private readonly Dictionary<string, Type> _valueTypes = new();

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            ApplyDefaults();
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            ApplyDefaults();
            IsInitialized = true;
        }

        public void Refresh()
        {
            IsReady = true;
        }

        /// <summary>
        /// Sets or overrides a remote config value at runtime. Useful for testing
        /// specific configurations without needing a real remote service.
        /// </summary>
        public void SetValue<T>(string key, T value)
        {
            _overrides[key] = value;
            _valueTypes[key] = typeof(T);
        }

        /// <summary>
        /// Removes a previously set override value for the given key.
        /// </summary>
        public void RemoveValue(string key)
        {
            _overrides.Remove(key);
            _valueTypes.Remove(key);
        }

        /// <summary>
        /// Clears all runtime overrides. Useful for test teardown so each test
        /// starts with a clean slate.
        /// </summary>
        public void ClearAll()
        {
            _overrides.Clear();
            _valueTypes.Clear();
        }

        public bool TryGetConfig<T>(string key, out T result)
        {
            if (_overrides.TryGetValue(key, out object val) && val is T typedVal)
            {
                result = typedVal;
                return true;
            }

            result = default;
            return false;
        }

        #endregion

        #region Private Methods

        private void ApplyDefaults()
        {
            _overrides.Clear();
            _valueTypes.Clear();

            foreach (MockRemoteDefaultValue defaultValue in defaultValues)
            {
                switch (defaultValue.Type)
                {
                    case MockRemoteValueType.String:
                        _overrides[defaultValue.Key] = defaultValue.Value;
                        _valueTypes[defaultValue.Key] = typeof(string);
                        break;

                    case MockRemoteValueType.Boolean:
                        if (bool.TryParse(defaultValue.Value, out bool boolValue))
                        {
                            _overrides[defaultValue.Key] = boolValue;
                            _valueTypes[defaultValue.Key] = typeof(bool);
                        }
                        break;

                    case MockRemoteValueType.Integer:
                        if (int.TryParse(defaultValue.Value, out int intValue))
                        {
                            _overrides[defaultValue.Key] = intValue;
                            _valueTypes[defaultValue.Key] = typeof(int);
                        }
                        break;

                    case MockRemoteValueType.Float:
                        if (float.TryParse(defaultValue.Value, out float floatValue))
                        {
                            _overrides[defaultValue.Key] = floatValue;
                            _valueTypes[defaultValue.Key] = typeof(float);
                        }
                        break;
                }
            }
        }

        #endregion
    }
}
