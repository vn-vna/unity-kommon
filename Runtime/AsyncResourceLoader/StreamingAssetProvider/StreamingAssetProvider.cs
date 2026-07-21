using System;
using System.Collections;
using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Streaming Assets",
        "Loads from Application.streamingAssetsPath via UnityWebRequest. "
        + "Override ConvertResource(byte[])."
    )]
    public abstract class StreamingAssetProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IStreamingAssetProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public int Priority => priority;
        public float ResourceLoadingTimeout => timeout;
        public bool IsInitialized { get; private set; }
        public string SubFolder => subFolder;

        [SerializeField]
        [Tooltip("Subfolder relative to Application.streamingAssetsPath. "
            + "Leave empty to read from the root.")]
        private string subFolder;

        [SerializeField]
        [Tooltip("Lower values load first (cascade fallback).")]
        private int priority;

        [SerializeField]
        [Tooltip("Maximum time in seconds to wait for a single load request.")]
        private float timeout = 10f;

        [SerializeField]
        [Tooltip("Request timeout in seconds. Aborts if no progress is made "
            + "within this duration.")]
        private float requestTimeout = 15f;

        /// <summary>
        /// Override to convert raw bytes into the target resource type.
        /// </summary>
        protected abstract ResourceType ConvertResource(byte[] data);

        public virtual void Initialize()
        {
            string fullPath = BuildBasePath();
            if (!Directory.Exists(fullPath))
            {
                QuickLog.Warning<StreamingAssetProvider<ResourceType>>(
                    "StreamingAssets subfolder '{0}' not found at '{1}'. "
                    + "Provider initialized but may fail to find resources.",
                    subFolder, fullPath
                );
            }

            IsInitialized = true;

            QuickLog.Debug<StreamingAssetProvider<ResourceType>>(
                "StreamingAsset provider initialized for '{0}'.",
                subFolder ?? "<root>"
            );
        }

        public void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            if (!IsInitialized)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "StreamingAsset provider is not initialized."
                );
                return;
            }

            if (id is not IStreamingAssetId streamingId)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "Invalid resource ID type. Expected IStreamingAssetId."
                );
                return;
            }

            string filePath = streamingId.GetFilePath(this);
            if (string.IsNullOrEmpty(filePath))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "File path returned by resource ID is null or empty."
                );
                return;
            }

            string fullPath = BuildFullPath(filePath);

            if (string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentNullException(
                    nameof(id), "Resource ID is null or empty."
                );

                QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                    "Resource ID is null or empty. Cannot load from "
                    + "StreamingAssets."
                );
                return;
            }

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            QuickLog.Debug<StreamingAssetProvider<ResourceType>>(
                "Loading StreamingAsset '{0}'...", fullPath
            );

            LoadFromFileCoroutine(fullPath, handler)
                .DispatchOnDispatcher();
        }

        private IEnumerator LoadFromFileCoroutine(
            string fullPath,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            using UnityWebRequest webRequest = UnityWebRequest.Get(fullPath);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            UnityWebRequestAsyncOperation operation =
                webRequest.SendWebRequest();

            float startTime = Time.realtimeSinceStartup;

            while (!operation.isDone)
            {
                handler.Progress = webRequest.downloadProgress;

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (requestTimeout > 0f
                    && elapsed > requestTimeout
                    && webRequest.downloadProgress <= 0f)
                {
                    webRequest.Abort();
                    break;
                }

                yield return null;
            }

            handler.Progress = webRequest.downloadProgress;

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                byte[] data = webRequest.downloadHandler.data;

                try
                {
                    ResourceType resource = ConvertResource(data);

                    if (resource != null)
                    {
                        handler.Resouce = resource;
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Loaded;

                        QuickLog.Info<StreamingAssetProvider<ResourceType>>(
                            "StreamingAsset '{0}' loaded successfully.",
                            fullPath
                        );
                    }
                    else
                    {
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Failed;
                        handler.Exception = new InvalidOperationException(
                            $"ConvertResource returned null for '{fullPath}'. "
                            + "The data may be malformed or not of the "
                            + $"expected type ({typeof(ResourceType).Name})."
                        );

                        QuickLog.Warning<StreamingAssetProvider<ResourceType>>(
                            "ConvertResource returned null for '{0}'.",
                            fullPath
                        );
                    }
                }
                catch (Exception ex)
                {
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Failed;
                    handler.Exception = ex;

                    QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                        "ConvertResource threw for '{0}': {1}",
                        fullPath, ex.Message
                    );
                }
            }
            else
            {
                string error = webRequest.error ?? "Unknown error";

                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    $"Failed to load StreamingAsset '{fullPath}': {error}"
                );

                QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                    "Failed to load StreamingAsset '{0}': {1}",
                    fullPath, error
                );
            }
        }

        private string BuildBasePath()
        {
            string basePath = Application.streamingAssetsPath;
            if (string.IsNullOrEmpty(subFolder))
            {
                return basePath;
            }

            return Path.Combine(basePath, subFolder);
        }

        private string BuildFullPath(string relativePath)
        {
            string basePath = Application.streamingAssetsPath;
            if (!string.IsNullOrEmpty(subFolder))
            {
                basePath = Path.Combine(basePath, subFolder);
            }

            return Path.Combine(basePath, relativePath);
        }
    }
}
