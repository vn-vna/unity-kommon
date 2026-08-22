using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        private static readonly Regex UnresolvedTagRegex = new Regex(
            @"\{[^{}]+\}",
            RegexOptions.Compiled);

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
            return CombineUrl(
                dlProvider.BaseUrl,
                ApplyTemplate(
                    dlProvider.UrlFormat,
                    ResourceId,
                    CustomTags));
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
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new System.ArgumentException(
                    "Resource ID cannot be null, empty, or whitespace.",
                    nameof(resourceId));
            }

            string evaluatedTemplate = string.IsNullOrWhiteSpace(template)
                ? "{id}"
                : template;
            string result = evaluatedTemplate
                .Replace("{id}", resourceId)
                .Replace("{0}", resourceId);

            if (customTags != null)
            {
                foreach (KeyValuePair<string, string> kvp in customTags)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }

                    result = result.Replace(
                        $"{{{kvp.Key}}}",
                        kvp.Value ?? string.Empty);
                }
            }

            Match unresolvedTag = UnresolvedTagRegex.Match(result);
            if (unresolvedTag.Success)
            {
                throw new System.FormatException(
                    $"Template contains unresolved tag "
                    + $"'{unresolvedTag.Value}'.");
            }

            return result;
        }

        private static string CombineUrl(string baseUrl, string relativePath)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                return relativePath;
            }

            if (System.Uri.TryCreate(
                    relativePath,
                    System.UriKind.Absolute,
                    out System.Uri absoluteUri))
            {
                return absoluteUri.AbsoluteUri;
            }

            return baseUrl.TrimEnd('/')
                + "/"
                + (relativePath ?? string.Empty).TrimStart('/');
        }
    }

}
