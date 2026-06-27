using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{

    public class CachedAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public int Priority => throw new System.NotImplementedException();

        public bool IsInitialized => throw new System.NotImplementedException();

        public float ResourceLoadingTimeout => throw new System.NotImplementedException();

        public void Initialize()
        {
            throw new System.NotImplementedException();
        }

        public void TryLoadResource(IAsyncResourceId id, ResourceLoadingHandler<ResourceType> handler)
        {
            throw new System.NotImplementedException();
        }
    }

}
