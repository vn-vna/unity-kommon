using System;
using System.Collections;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.Integration.RemoteConfig
{
    public interface IRemoteConfigManager
    {
        event Action<IRemoteConfigData> ConfigAcquired;

        IEnumerable<IRemoteConfigProvider> Providers { get; }
        Type RemoteConfigType { get; }
        object Config { get; }
        RemoteConfigStatus Status { get; }
        IReadOnlyDictionary<string, IRemoteConfigProvider> ResolvedProviders { get; }

        bool TryGetSourceProvider(string key, out IRemoteConfigProvider provider);
        void RegisterProvider(IRemoteConfigProvider provider);
        void Initialize(float timeOut = float.MaxValue);
        IEnumerator InitializeCoroutine(float timeOut);
    }
}