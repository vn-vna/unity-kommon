using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    public struct PuzzleLevelId :
        IAsyncResourceId,
        IStreamingAssetId,
        IResourceFolderAsyncResourceId,
        IDownloadableAsyncResourceId,
        IReferenceTableAsyncResourceId
#if UNITY_ADDRESSABLES
        , IAddressableAsyncResourceId
#endif
    {
        public string ResourceId { get; set; }

        public static implicit operator PuzzleLevelId(string id)
            => new PuzzleLevelId { ResourceId = id };

        string IStreamingAssetId.GetFilePath(IStreamingAssetProvider provider)
            => string.Format(((PuzzleLevelStreamingAssetProvider)provider).PathFormat, ResourceId);

        string IResourceFolderAsyncResourceId.GetResourcePath(IResourceFolderAsyncResourceProvider provider)
            => string.Format(((PuzzleLevelResourceFolderProvider)provider).PathFormat, ResourceId);

        string IDownloadableAsyncResourceId.GetUrl(IDownloadableResourceProvider provider)
            => ((DownloadableResourceProvider<TextAsset>)provider).BaseUrl
                + string.Format(((DownloadableResourceProvider<TextAsset>)provider).UrlFormat, ResourceId);

        string IReferenceTableAsyncResourceId.GetResourceId(IReferenceTableAsyncResourceProvider provider)
            => string.Format(((PuzzleLevelReferenceTableProvider)provider).KeyFormat, ResourceId);

#if UNITY_ADDRESSABLES
        string IAddressableAsyncResourceId.GetAddressableKey(IAddressableAsyncResourceProvider provider)
            => string.Format(((PuzzleLevelAddressableProvider)provider).KeyFormat, ResourceId);
#endif
    }

}