using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    [Serializable]
    public sealed class GitHubReleaseInfo
    {
        public string TagName;
        public string Version;
        public string PublishedAt;
        public string Body;
        public string DownloadUrl;
        public string GitUrl;
        public bool IsPrerelease;
        public string ReleaseUrl;
        public bool IsZipArchive;
    }

    [Serializable]
    public sealed class GitHubReleaseCache
    {
        public string RepoOwner;
        public string RepoName;
        public string FetchedAt;
        public List<GitHubReleaseInfo> Releases;
    }

    [Serializable]
    public sealed class UnityPackageInstallRecord
    {
        public string PackageName;
        public string Version;
        public string InstalledAt;
        public List<string> TrackedFiles;
        public List<TrackedAssetFingerprint> Fingerprints;
        public bool HasFingerprints;
    }

    [Serializable]
    public sealed class TrackedAssetFingerprint
    {
        public string Path;
        public string Hash;
        public string MetaHash;
    }
}
