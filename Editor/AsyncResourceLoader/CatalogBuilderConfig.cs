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
        public bool EnableEntryAutoId;
        public string EntryAutoIdTemplate = "level_{index:+1}";
        public bool EnableEntryAutoIdRegex;
        public string EntryAutoIdRegexPattern;
        public List<StagedCatalogEntry> Entries = new List<StagedCatalogEntry>();
        public CatalogBuildState LastGenerated = new CatalogBuildState();
        public CatalogBuildState LastUploaded = new CatalogBuildState();
        public ScriptableObject RuntimeProvider;
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
    public class CatalogBuildState
    {
        public string ManifestHash;
        public int Version;
        public string OutputFolder;
        public string CatalogRelativePath;
        public List<GeneratedCatalogEntry> Entries
            = new List<GeneratedCatalogEntry>();

        public bool HasBuild => !string.IsNullOrWhiteSpace(ManifestHash);
    }

    [Serializable]
    public class GeneratedCatalogEntry
    {
        public string Id;
        public DataType Type;
        public string RelativePath;
        public string ContentHash;
    }

    [Serializable]
    public class S3UploadSettings
    {
        public bool Enabled;
        public string Endpoint = "https://s3.amazonaws.com";
        public string Region = "us-east-1";
        public string Bucket = "";

        [Tooltip("Read access credentials from environment variables instead of serializing them in this asset.")]
        public bool UseEnvironmentCredentials = true;

        public string AccessKeyEnvironmentVariable = "AWS_ACCESS_KEY_ID";
        public string SecretKeyEnvironmentVariable = "AWS_SECRET_ACCESS_KEY";

        [HideInInspector]
        public string AccessKey = "";

        [HideInInspector]
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
                    && !string.IsNullOrWhiteSpace(ResolvedAccessKey)
                    && !string.IsNullOrWhiteSpace(ResolvedSecretKey);
            }
        }

        public string ResolvedAccessKey => UseEnvironmentCredentials
            ? Environment.GetEnvironmentVariable(AccessKeyEnvironmentVariable)
            : AccessKey;

        public string ResolvedSecretKey => UseEnvironmentCredentials
            ? Environment.GetEnvironmentVariable(SecretKeyEnvironmentVariable)
            : SecretKey;
    }
}
