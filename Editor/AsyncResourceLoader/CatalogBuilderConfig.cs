using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    [CreateAssetMenu(
        fileName = "CatalogBuilderConfig",
        menuName = "Scheherazade/Async Resource Loader/Catalog Builder Config"
    )]
    public class CatalogBuilderConfig : ScriptableObject
    {
        public string OutputFolder = "Assets/StreamingAssets/Catalog";
        public string SubfolderName = "Levels";
        public string CatalogFileName = "catalog.json";
        public int Version = 1;
        public List<StagedCatalogEntry> Entries = new List<StagedCatalogEntry>();
        public S3UploadSettings S3 = new S3UploadSettings();

        public static CatalogBuilderConfig GetOrCreate()
        {
            CatalogBuilderConfig config = Resources.Load<CatalogBuilderConfig>(
                nameof(CatalogBuilderConfig)
            );

            if (config != null)
            {
                return config;
            }

#if UNITY_EDITOR
            string resourcesFolder = "Assets/Resources";
            if (!System.IO.Directory.Exists(resourcesFolder))
            {
                System.IO.Directory.CreateDirectory(resourcesFolder);
            }

            config = CreateInstance<CatalogBuilderConfig>();
            config.name = nameof(CatalogBuilderConfig);
            string assetPath = $"{resourcesFolder}/{nameof(CatalogBuilderConfig)}.asset";
            UnityEditor.AssetDatabase.CreateAsset(config, assetPath);
            UnityEditor.AssetDatabase.SaveAssets();
#endif

            return config;
        }
    }

    [Serializable]
    public class StagedCatalogEntry
    {
        public string Id;
        public DataType Type;
        public string RelativePath;
        public string SourceFilePath;
        public string ContentHash;
    }

    [Serializable]
    public class S3UploadSettings
    {
        public bool Enabled;
        public string Endpoint = "https://s3.amazonaws.com";
        public string Region = "us-east-1";
        public string Bucket = "";
        public string AccessKey = "";
        public string SecretKey = "";
        public string BasePrefix = "";
        public bool PublicRead;

        public bool IsValid
        {
            get
            {
                return Enabled
                    && !string.IsNullOrWhiteSpace(Endpoint)
                    && !string.IsNullOrWhiteSpace(Region)
                    && !string.IsNullOrWhiteSpace(Bucket)
                    && !string.IsNullOrWhiteSpace(AccessKey)
                    && !string.IsNullOrWhiteSpace(SecretKey);
            }
        }
    }
}
