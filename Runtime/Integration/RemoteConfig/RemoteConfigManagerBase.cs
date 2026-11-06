using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Scheherazade.Common.Logging;
using Com.Scheherazade.Common.Singleton;
using Com.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.RemoteConfig
{
    public abstract class RemoteConfigManagerBase<T, Self> :
        SingletonScriptableObject<Self>,
        IRemoteConfigManager,
        IIntegrationModule
        where T : IRemoteConfigData, new()
        where Self : ScriptableObject
    {
        public event Action<IRemoteConfigData> ConfigAcquired;

        public IEnumerable<IRemoteConfigProvider> Providers => _providers;
        public Type RemoteConfigType => typeof(T);
        object IRemoteConfigManager.Config => _configData;
        public T ConfigData => _configData;
        public RemoteConfigStatus Status { get; private set; } = RemoteConfigStatus.Uninitialized;
        public IReadOnlyDictionary<string, IRemoteConfigProvider> ResolvedProviders =>
            _resolvedProviders;

        [SerializeField]
        [HideInInspector]
        private ScriptableObject[] initialProviders;

        private List<IRemoteConfigProvider> _providers;
        private readonly Dictionary<string, IRemoteConfigProvider> _resolvedProviders = new();
        private T _configData;

        protected override void OnEnable()
        {
            base.OnEnable();
            _configData = new T();
            _providers ??= new List<IRemoteConfigProvider>();
            _resolvedProviders.Clear();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            Status = RemoteConfigStatus.Uninitialized;
            _configData = new T();
            _resolvedProviders.Clear();
            _providers ??= new List<IRemoteConfigProvider>();
            _providers.Clear();

            if (initialProviders == null || initialProviders.Length == 0)
            {
                return;
            }

            RegisterInitialProviders();
        }

        public void RegisterProvider(IRemoteConfigProvider provider)
        {
            if (_providers.Any(existingProvider => existingProvider.GetType() == provider.GetType()))
            {
                QuickLog.Warning<RemoteConfigManagerBase<T, Self>>(
                    "Provider with type {0} already registered. Skipping.",
                    provider.GetType().Name
                );

                return;
            }
            _providers.Add(provider);
            provider.Manager = this;
            _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public bool TryGetSourceProvider(
            string key,
            out IRemoteConfigProvider provider)
        {
            return _resolvedProviders.TryGetValue(key, out provider);
        }

        public void Initialize(float timeOut = float.MaxValue)
        {
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
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

            if (Status == RemoteConfigStatus.Initialized)
            {
                HandleInitializationComplete();
            }
        }

        public void AcquireRemoteConfig()
        {
            _resolvedProviders.Clear();
            PropertyInfo[] properties = _configData.GetType().GetProperties();
            foreach (var property in properties)
            {
                RemoteConfigAttribute attribute = property.GetCustomAttribute<RemoteConfigAttribute>();
                if (attribute == null) continue;

                AcquireRemoteConfigProperty(property, attribute);
            }

            LogAppliedPropertySources(properties);
            HandleAcquiredConfigComplete();
        }

        private void LogAppliedPropertySources(IEnumerable<PropertyInfo> properties)
        {
            foreach (PropertyInfo property in properties)
            {
                RemoteConfigAttribute attribute =
                    property.GetCustomAttribute<RemoteConfigAttribute>();
                if (attribute == null || string.IsNullOrEmpty(attribute.Key)) continue;

                string source = TryGetSourceProvider(
                    attribute.Key,
                    out IRemoteConfigProvider provider)
                    ? provider.GetType().Name
                    : "configured default";
                QuickLog.Info<RemoteConfigManagerBase<T, Self>>(
                    "Applied remote config property {0} (key: {1}) from {2}.",
                    property.Name,
                    attribute.Key,
                    source);
            }
        }

        public bool TryAcquireValueFromProvider<V>(
            string key,
            out V value,
            Func<V> defaultValueProvider = null)
        {
            return TryAcquireValueFromProvider(
                key,
                out value,
                out _,
                defaultValueProvider);
        }

        public bool TryAcquireValueFromProvider<V>(
            string key,
            out V value,
            out IRemoteConfigProvider sourceProvider,
            Func<V> defaultValueProvider = null)
        {
            foreach (IRemoteConfigProvider provider in Providers)
            {
                if (provider.TryGetConfig(key, out value))
                {
                    sourceProvider = provider;
                    return true;
                }
            }

            value = defaultValueProvider != null ? defaultValueProvider() : default;
            sourceProvider = null;
            return false;
        }

        protected virtual void HandleInitializationComplete()
        {
            RefreshConfigCoroutine().DispatchOnDispatcher();
        }

        public IEnumerator RefreshConfigCoroutine(float timeOut = 30f)
        {
            Status = RemoteConfigStatus.Refreshing;

            foreach (var provider in Providers)
            {
                provider.Refresh();
            }

            float timer = 0f;

            while (timer <= timeOut)
            {
                if (Providers.All(p => p.IsReady))
                {
                    Status = RemoteConfigStatus.Ready;
                    break;
                }

                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            HandleRefreshComplete();
        }


        protected virtual void HandleRefreshComplete()
        {
            AcquireRemoteConfig();
        }

        protected virtual void HandleAcquiredConfigComplete()
        {
            ConfigAcquired?.Invoke(_configData);
        }

        private void RegisterInitialProviders()
        {
            foreach (var providerAsset in initialProviders)
            {
                if (providerAsset is not IRemoteConfigProvider provider)
                {
                    Debug.LogWarning(
                        $"Initial remote config provider asset {providerAsset?.name} does not implement IRemoteConfigProvider. Skipping."
                    );
                    continue;
                }

                RegisterProvider(provider);
            }
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

            try
            {
                switch (property.PropertyType)
                {
                    case Type t when t == typeof(int):
                        SetScalarProperty<int>(property, attribute);
                        break;
                    case Type t when t == typeof(float):
                        SetScalarProperty<float>(property, attribute);
                        break;
                    case Type t when t == typeof(bool):
                        SetScalarProperty<bool>(property, attribute);
                        break;
                    case Type t when t == typeof(string):
                        SetScalarProperty<string>(property, attribute);
                        break;
                    default:
                        Debug.LogError($"Unsupported property type {property.PropertyType} for RemoteConfigAttribute on property {property.Name}.");
                        break;
                }
            }
            catch (Exception ex)
            {
                QuickLog.Critical<RemoteConfigManagerBase<T, Self>>(
                    "Acquire remote config property failed for property [{0}]: {1}",
                    property.Name, ex
                );
            }
        }

        private void SetScalarProperty<V>(
            PropertyInfo property,
            RemoteConfigAttribute attribute)
        {
            if (TryAcquireValueFromProvider(
                    attribute.Key,
                    out V value,
                    out IRemoteConfigProvider sourceProvider))
            {
                property.SetValue(_configData, value);
                _resolvedProviders[attribute.Key] = sourceProvider;
                return;
            }

            property.SetValue(_configData, (V)attribute.DefaultValue);
        }

        private void HandleParserModuleForProperty(PropertyInfo property, RemoteConfigAttribute attribute)
        {
            IRemoteConfigParserModule parserModule;
            try
            {
                parserModule = Activator.CreateInstance(attribute.ParserModule)
                    as IRemoteConfigParserModule;
            }
            catch (Exception exception)
            {
                QuickLog.Error<RemoteConfigManagerBase<T, Self>>(
                    "Failed to create parser {0} for remote config property {1}: {2}",
                    attribute.ParserModule?.Name ?? "<null>",
                    property.Name,
                    exception.Message);
                return;
            }

            if (parserModule == null)
            {
                QuickLog.Error<RemoteConfigManagerBase<T, Self>>(
                    "Remote config parser {0} for property {1} does not implement IRemoteConfigParserModule.",
                    attribute.ParserModule?.Name ?? "<null>",
                    property.Name);
                return;
            }

            if (TryAcquireValueFromProvider(
                    attribute.Key,
                    out string remoteValue,
                    out IRemoteConfigProvider sourceProvider)
                && parserModule.TryParse(remoteValue, out object parsedRemoteValue)
                && parsedRemoteValue != null
                && property.PropertyType.IsInstanceOfType(parsedRemoteValue))
            {
                property.SetValue(_configData, parsedRemoteValue);
                _resolvedProviders[attribute.Key] = sourceProvider;
                return;
            }

            string defaultInput = attribute.DefaultValue as string
                ?? attribute.DefaultValue?.ToString();
            if (defaultInput != null
                && parserModule.TryParse(defaultInput, out object parsedDefaultValue)
                && parsedDefaultValue != null
                && property.PropertyType.IsInstanceOfType(parsedDefaultValue))
            {
                property.SetValue(_configData, parsedDefaultValue);
                return;
            }

            if (attribute.DefaultValue != null
                && property.PropertyType.IsInstanceOfType(attribute.DefaultValue))
            {
                property.SetValue(_configData, attribute.DefaultValue);
                return;
            }

            QuickLog.Error<RemoteConfigManagerBase<T, Self>>(
                "Remote config key {0} for property {1} could not be parsed from either the provider value or its default.",
                attribute.Key,
                property.Name);
        }
    }
}