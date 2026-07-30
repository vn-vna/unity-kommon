using System.Collections.Generic;
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

        public IReadOnlyDictionary<string, string> CustomTags { get; set; }

        public static implicit operator PuzzleLevelId(string id)
            => new PuzzleLevelId { ResourceId = id };

        string IStreamingAssetId.GetFilePath(IStreamingAssetProvider provider)
            => ApplyTemplate(
                ((PuzzleLevelStreamingAssetProvider)provider).PathFormat,
                ResourceId,
                CustomTags);

        string IResourceFolderAsyncResourceId.GetResourcePath(
            IResourceFolderAsyncResourceProvider provider)
            => ApplyTemplate(
                ((PuzzleLevelResourceFolderProvider)provider).PathFormat,
                ResourceId,
                CustomTags);

        string IDownloadableAsyncResourceId.GetUrl(
            IDownloadableResourceProvider provider)
        {
            DownloadableResourceProvider<TextAsset> dlProvider
                = (DownloadableResourceProvider<TextAsset>)provider;
            return dlProvider.BaseUrl
                + ApplyTemplate(dlProvider.UrlFormat, ResourceId, CustomTags);
        }

        string IReferenceTableAsyncResourceId.GetResourceId(
            IReferenceTableAsyncResourceProvider provider)
            => ApplyTemplate(
                ((PuzzleLevelReferenceTableProvider)provider).KeyFormat,
                ResourceId,
                CustomTags);

#if UNITY_ADDRESSABLES
        string IAddressableAsyncResourceId.GetAddressableKey(
            IAddressableAsyncResourceProvider provider)
            => ApplyTemplate(
                ((PuzzleLevelAddressableProvider)provider).KeyFormat,
                ResourceId,
                CustomTags);
#endif

        private static string ApplyTemplate(
            string template,
            string resourceId,
            IReadOnlyDictionary<string, string> customTags)
        {
            string result = template
                .Replace("{id}", resourceId)
                .Replace("{0}", resourceId);

            if (customTags == null || customTags.Count == 0)
            {
                return result;
            }

            foreach (KeyValuePair<string, string> kvp in customTags)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value);
            }

            return result;
        }
    }

}