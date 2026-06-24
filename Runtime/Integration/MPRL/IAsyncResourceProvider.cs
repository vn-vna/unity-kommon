using System;
using System.Threading;

namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IAsyncResourceProvider
    {
        int Priority { get; }
        bool IsInitialized { get; }
        float ResourceLoadingTimeout { get; }
        void Initialize();
    }

    public interface IAsyncResourceProvider<ResourceType> :
        IAsyncResourceProvider
        where ResourceType : UnityEngine.Object
    {
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }

}