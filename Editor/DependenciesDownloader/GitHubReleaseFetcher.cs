using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

#if NEWTONSOFT_JSON
using Newtonsoft.Json.Linq;
#endif

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public enum GitHubReleaseDelivery
    {
        UnityPackageAsset,
        GitPackage,
        UnityPackageZipAsset
    }

    public static class GitHubReleaseFetcher
    {
        public static string LastFetchError { get; private set; }
        public static bool LastFetchUsedCache { get; private set; }

        private static string CacheFilePath(string repoOwner, string repoName) =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Temp",
                "DependenciesDownloader",
                $"{repoOwner}_{repoName}_cache.json"));

        public static GitHubReleaseCache GetCachedRelease(
            string repoOwner, string repoName)
        {
            var cachePath = CacheFilePath(repoOwner, repoName);
            if (!File.Exists(cachePath)) return null;

            try
            {
                var json = File.ReadAllText(cachePath);
                var cache = JsonUtility.FromJson<GitHubReleaseCache>(json);
                if (cache?.Releases != null)
                {
                    foreach (var release in cache.Releases)
                        release.IsPrerelease |= release.Version?.Contains("-") == true;
                }
                return cache;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<GitHubReleaseCache> FetchReleasesAsync(
            string repoOwner,
            string repoName,
            GitHubReleaseDelivery delivery =
                GitHubReleaseDelivery.UnityPackageAsset,
            CancellationToken cancellationToken = default,
            Action<float> reportProgress = null
        )
        {
            LastFetchError = string.Empty;
            LastFetchUsedCache = false;

            var apiUrl =
                $"https://api.github.com/repos/{repoOwner}/{repoName}/releases?per_page=100";
            using var request = UnityWebRequest.Get(apiUrl);
            request.timeout = 30;
            request.SetRequestHeader("Accept", "application/vnd.github.v3+json");
            request.SetRequestHeader("User-Agent", "UnityEditor");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                reportProgress?.Invoke(request.downloadProgress);
                await Task.Delay(50);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastFetchError = request.error;
                if (request.responseCode == 429 || request.GetResponseHeader("X-RateLimit-Remaining") == "0")
                    LastFetchError = "GitHub rate limit reached. Retry after the limit resets; cached releases remain available.";
                var fallback = GetCachedRelease(repoOwner, repoName);
                LastFetchUsedCache = fallback != null;
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to fetch releases " +
                    $"for {repoOwner}/{repoName}: {request.error}"
                );
                return fallback;
            }

            var releases = ParseReleaseJson(
                request.downloadHandler.text,
                repoOwner,
                repoName,
                delivery
            );
            if (releases == null || releases.Count == 0)
            {
                LastFetchError = delivery ==
                                 GitHubReleaseDelivery.GitPackage
                    ? "No usable Git package releases were found."
                    : delivery == GitHubReleaseDelivery.UnityPackageZipAsset
                        ? "No releases with a unique Facebook SDK ZIP asset were found."
                        : "No releases with a unique .unitypackage asset were found.";
                var fallback = GetCachedRelease(repoOwner, repoName);
                LastFetchUsedCache = fallback != null;
                return fallback;
            }

            var cache = new GitHubReleaseCache
            {
                RepoOwner = repoOwner,
                RepoName = repoName,
                FetchedAt = DateTime.UtcNow.ToString(
                    "O", CultureInfo.InvariantCulture),
                Releases = releases
            };

            SaveCache(cache);
            return cache;
        }

        private static List<GitHubReleaseInfo> ParseReleaseJson(
            string json,
            string repoOwner,
            string repoName,
            GitHubReleaseDelivery delivery
        )
        {
#if NEWTONSOFT_JSON
            try
            {
                var array = JArray.Parse(json);
                var releases = new List<GitHubReleaseInfo>();

                foreach (var item in array)
                {
                    if (item["draft"]?.Value<bool>() == true) continue;
                    var tag = item["tag_name"]?.ToString() ?? string.Empty;
                    var version = ParseVersionFromTag(tag);
                    var published =
                        item["published_at"]?.ToString() ?? string.Empty;
                    var body = item["body"]?.ToString() ?? string.Empty;
                    var url = string.Empty;

                    var assets = item["assets"] as JArray;
                    var unityPackageCount = 0;
                    if (assets != null)
                    {
                        foreach (var asset in assets)
                        {
                            var candidate =
                                asset["browser_download_url"]?.ToString()
                                ?? string.Empty;
                            var assetName = asset["name"]?.ToString() ?? Path.GetFileName(candidate);
                            var matches = delivery == GitHubReleaseDelivery.UnityPackageZipAsset
                                ? assetName.StartsWith("facebook-unity-sdk-", StringComparison.OrdinalIgnoreCase) &&
                                  assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                  DependencyVersionUtility.Extract(assetName) == version
                                : candidate.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
                            if (!matches)
                            {
                                continue;
                            }

                            unityPackageCount++;
                            url = candidate;
                        }
                    }

                    var hasDownload = delivery ==
                                      GitHubReleaseDelivery.GitPackage ||
                                      unityPackageCount == 1;
                    if (!string.IsNullOrEmpty(version) && hasDownload)
                    {
                        releases.Add(new GitHubReleaseInfo
                        {
                            TagName = tag,
                            Version = version,
                            PublishedAt = published,
                            Body = body.Trim(),
                            IsPrerelease = item["prerelease"]?.Value<bool>() == true,
                            ReleaseUrl = item["html_url"]?.ToString() ?? string.Empty,
                            DownloadUrl = url,
                            IsZipArchive = delivery == GitHubReleaseDelivery.UnityPackageZipAsset,
                            GitUrl = delivery ==
                                     GitHubReleaseDelivery.GitPackage
                                ? $"https://github.com/{repoOwner}/{repoName}.git#{tag}"
                                : string.Empty
                        });
                    }
                }

                releases.Sort((left, right) =>
                {
                    if (left.IsPrerelease != right.IsPrerelease)
                        return left.IsPrerelease ? 1 : -1;
                    return DependencyVersionUtility.TryCompare(right.Version, left.Version, out var comparison)
                        ? comparison : string.CompareOrdinal(right.PublishedAt, left.PublishedAt);
                });
                return releases;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to parse GitHub " +
                    $"release JSON: {ex.Message}");
                return null;
            }
#else
            Debug.LogError(
                "[DependenciesDownloader] Newtonsoft.Json is required.");
            return null;
#endif
        }

        private static string ParseVersionFromTag(string tag)
        {
            return DependencyVersionUtility.Extract(tag);
        }

        private static void SaveCache(GitHubReleaseCache cache)
        {
            if (cache == null) return;
            try
            {
                var path = CacheFilePath(
                    cache.RepoOwner, cache.RepoName);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonUtility.ToJson(cache, prettyPrint: false);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to save cache: " +
                    $"{ex.Message}");
            }
        }
    }
}
