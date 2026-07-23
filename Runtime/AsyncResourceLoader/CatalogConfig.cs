using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [Serializable]
    public struct CatalogConfig
    {
#if UNITY_EDITOR
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve. Disable to skip catalog-based filtering.")]
#endif
        public bool UseCatalog;

#if UNITY_EDITOR
        [Tooltip("Path to the catalog JSON file, relative to Application.streamingAssetsPath.")]
#endif
        public string CatalogFileName;
    }
}
