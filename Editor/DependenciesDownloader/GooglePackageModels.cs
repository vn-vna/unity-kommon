using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    [Serializable]
    public sealed class GooglePackageInfo
    {
        public string Name;
        public string DisplayName;
        public string Description;
        public string Category;
        public string SubCategory;
        public List<GooglePackageVersion> Versions;
    }

    [Serializable]
    public sealed class GooglePackageVersion
    {
        public string Version;
        public string TarballUrl;
        public string PublishDate;
        public string MinUnityVersion;
        public List<GooglePackageDependency> Dependencies;
    }

    [Serializable]
    public sealed class GooglePackageDependency
    {
        public string Name;
        public string TarballUrl;
        public string Version;
    }

    [Serializable]
    public sealed class DownloadEntry
    {
        public string PackageName;
        public string Version;
        public string TarballUrl;
        public bool IsTransitive;
    }

    [Serializable]
    public sealed class GoogleArchiveCache
    {
        public string FetchedAt;
        public List<GooglePackageInfo> Packages;
    }
}
