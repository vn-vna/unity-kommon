using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
        GitPackage
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
                return JsonUtility.FromJson<GitHubReleaseCache>(json);
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
                GitHubReleaseDelivery.UnityPackageAsset
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
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastFetchError = request.error;
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
                            if (!candidate.EndsWith(
                                    ".unitypackage",
                                    StringComparison.OrdinalIgnoreCase
                                ))
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
                            DownloadUrl = url,
                            GitUrl = delivery ==
                                     GitHubReleaseDelivery.GitPackage
                                ? $"https://github.com/{repoOwner}/{repoName}.git#{tag}"
                                : string.Empty
                        });
                    }
                }

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
            var match = Regex.Match(
                tag ?? string.Empty,
                @"(\d+)[._](\d+)[._](\d+)(?:[._](\d+))?"
            );
            if (!match.Success) return tag ?? string.Empty;

            var version =
                $"{match.Groups[1].Value}." +
                $"{match.Groups[2].Value}." +
                match.Groups[3].Value;
            if (match.Groups[4].Success)
            {
                version += "." + match.Groups[4].Value;
            }

            return version;
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
