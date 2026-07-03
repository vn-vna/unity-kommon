using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    [Serializable]
    public sealed class GitHubReleaseInfo
    {
        public string TagName;
        public string Version;
        public string PublishedAt;
        public string Body;
        public string DownloadUrl;
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
    }
}
