using System;
using System.Collections;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public interface IRemoteConfigManager
    {
        event Action<IRemoteConfigData> ConfigAcquired;

        IEnumerable<IRemoteConfigProvider> Providers { get; }
        Type RemoteConfigType { get; }
        object Config { get; }
        RemoteConfigStatus Status { get; }

        void RegisterProvider(IRemoteConfigProvider provider);
        void Initialize(float timeOut = float.MaxValue);
        IEnumerator InitializeCoroutine(float timeOut);
    }
}