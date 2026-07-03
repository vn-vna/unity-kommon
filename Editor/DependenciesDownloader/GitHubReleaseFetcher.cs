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

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class GitHubReleaseFetcher
    {
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
            string repoOwner, string repoName)
        {
            var apiUrl =
                $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
            using var request = UnityWebRequest.Get(apiUrl);
            request.SetRequestHeader("Accept", "application/vnd.github.v3+json");
            request.SetRequestHeader("User-Agent", "UnityEditor");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to fetch releases " +
                    $"for {repoOwner}/{repoName}: {request.error}");
                return GetCachedRelease(repoOwner, repoName);
            }

            var releases = ParseReleaseJson(
                request.downloadHandler.text);
            if (releases == null || releases.Count == 0)
            {
                return GetCachedRelease(repoOwner, repoName);
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

        private static List<GitHubReleaseInfo> ParseReleaseJson(string json)
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
                    if (assets != null && assets.Count > 0)
                    {
                        var firstAsset = assets[0];
                        url = firstAsset["browser_download_url"]?.ToString()
                              ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(version) &&
                        !string.IsNullOrEmpty(url))
                    {
                        releases.Add(new GitHubReleaseInfo
                        {
                            TagName = tag,
                            Version = version,
                            PublishedAt = published,
                            Body = body.Trim(),
                            DownloadUrl = url
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
                tag, @"(\d+\.\d+\.\d+(?:\.\d+)?)");
            return match.Success ? match.Groups[1].Value : tag;
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
