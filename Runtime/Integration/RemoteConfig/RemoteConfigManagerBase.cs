using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public abstract class RemoteConfigManagerBase<T, Self> :
        SingletonBehavior<Self>,
        IRemoteConfigManager
        where T : IRemoteConfigData, new()
        where Self : RemoteConfigManagerBase<T, Self>
    {
        public event Action<IRemoteConfigData> ConfigAcquired;

        public IEnumerable<IRemoteConfigProvider> Providers => _providers;
        public Type RemoteConfigType => typeof(T);
        object IRemoteConfigManager.Config => _configData;
        public T ConfigData => _configData;
        public RemoteConfigStatus Status { get; private set; } = RemoteConfigStatus.Uninitialized;

        private List<IRemoteConfigProvider> _providers;
        private T _configData;

        protected override void Awake()
        {
            base.Awake();
            _configData = new T();
            _providers ??= new List<IRemoteConfigProvider>();
            Integration.RegisterManager(this);
        }

        public void RegisterProvider(IRemoteConfigProvider provider)
        {
            if (_providers.Contains(provider))
            {
                QuickLog.Warning<RemoteConfigManagerBase<T, Self>>(
                    "Provider with type {0} already registered. Skipping.",
                    provider.GetType().Name
                );

                return;
            }
            _providers.Add(provider);
            provider.Manager = this;
        }

        public void Initialize(float timeOut = float.MaxValue)
        {
            StartCoroutine(InitializeCoroutine(timeOut));
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            Status = RemoteConfigStatus.Initializing;

            float timer = 0f;

            foreach (var provider in Providers)
            {
                provider.Initialize();
            }

            while (true)
            {
                if (timer > timeOut)
                {
                    Status = RemoteConfigStatus.Uninitialized;
                    break;
                }

                if (Providers.All(p => p.IsInitialized))
                {
                    Status = RemoteConfigStatus.Initialized;
                    break;
                }

                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            HandleInitializationComplete();
        }

        public void AcquireRemoteConfig()
        {
            PropertyInfo[] properties = _configData.GetType().GetProperties();
            foreach (var property in properties)
            {
                RemoteConfigAttribute attribute = property.GetCustomAttribute<RemoteConfigAttribute>();
                if (attribute == null) continue;

                AcquireRemoteConfigProperty(property, attribute);
            }

            HandleAcquiredConfigComplete();
        }

        public bool TryAcquireValueFromProvider<V>(string key, out V value, Func<V> defaultValueProvider = null)
        {
            foreach (var provider in Providers)
            {
                if (provider.TryGetConfig(key, out value))
                {
                    return true;
                }
            }

            value = defaultValueProvider != null ? defaultValueProvider() : default;
            return false;
        }

        protected virtual void HandleInitializationComplete()
        { }

        public IEnumerator RefreshConfigCoroutine()
        {
            Status = RemoteConfigStatus.Refreshing;

            foreach (var provider in Providers)
            {
                provider.Refresh();
            }

            while (true)
            {
                if (Providers.All(p => p.IsReady))
                {
                    Status = RemoteConfigStatus.Ready;
                    break;
                }

                yield return null;
            }

            HandleRefreshComplete();
        }


        protected virtual void HandleRefreshComplete()
        { }

        protected virtual void HandleAcquiredConfigComplete()
        {
            ConfigAcquired?.Invoke(_configData);
        }

        private void AcquireRemoteConfigProperty(PropertyInfo property, RemoteConfigAttribute attribute)
        {
            if (attribute.Key == null)
            {
                Debug.LogError($"RemoteConfigAttribute on property {property.Name} must have a valid Key.");
            }

            if (attribute.ParserModule != null)
            {
                HandleParserModuleForProperty(property, attribute);
                return;
            }

            switch (property.PropertyType)
            {
                case Type t when t == typeof(int):
                    if (TryAcquireValueFromProvider(attribute.Key, out int intValue))
                    {
                        property.SetValue(_configData, intValue);
                    }
                    break;

                case Type t when t == typeof(float):
                    if (TryAcquireValueFromProvider(attribute.Key, out float floatValue))
                    {
                        property.SetValue(_configData, floatValue);
                    }
                    break;

                case Type t when t == typeof(bool):
                    if (TryAcquireValueFromProvider(attribute.Key, out bool boolValue))
                    {
                        property.SetValue(_configData, boolValue);
                    }
                    break;

                case Type t when t == typeof(string):
                    if (TryAcquireValueFromProvider(attribute.Key, out string stringValue))
                    {
                        property.SetValue(_configData, stringValue);
                    }
                    break;

                default:
                    Debug.LogError($"Unsupported property type {property.PropertyType} for RemoteConfigAttribute on property {property.Name}.");
                    break;
            }
        }

        private void HandleParserModuleForProperty(PropertyInfo property, RemoteConfigAttribute attribute)
        {
            IRemoteConfigParserModule parserModule = Activator.CreateInstance(attribute.ParserModule) as IRemoteConfigParserModule;
            if (parserModule == null)
            {
                Debug.LogError($"Failed to create parser module for property {property.Name}.");
                return;
            }

            string value = TryAcquireValueFromProvider(attribute.Key, out string stringValue) ? stringValue : null;
            if (parserModule.TryParse(value, out object parsedValue))
            {
                property.SetValue(_configData, parsedValue);
            }
        }
    }
}