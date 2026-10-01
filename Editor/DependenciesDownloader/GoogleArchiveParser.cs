using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class GoogleArchiveParser
    {
        private const string ArchiveUrl = "https://developers.google.com/unity/archive.md.txt";

        private static string CacheFilePath =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Temp",
                "DependenciesDownloader", "archive_cache.json"));

        private static GoogleArchiveCache _cachedArchive;

        public static string LastFetchError { get; private set; }
        public static bool LastFetchCanceled { get; private set; }
        public static bool LastFetchUsedCache { get; private set; }

        private static readonly Regex PackageNameRegex = new Regex(
            @"^`(com\.google\.[^`]+)`", RegexOptions.Compiled);
        private static readonly Regex VersionRowRegex = new Regex(
            @"(?<![\d.])(?<version>\d+\.\d+\.\d+(?:\.\d+)?)\s+" +
            @"(?<date>\d{4}-\d{2})\s+" +
            @"(?<unity>\S+)\s+" +
            @"(?:\[\.unitypackage\]\([^)]+\)\s+)?" +
            @"\[\.tgz\]\((?<tarball>[^)]+\.tgz)\)",
            RegexOptions.Compiled);
        private static readonly Regex DependencyLinkRegex = new Regex(
            @"\[(com\.[a-z]+(?:\.[a-z0-9_-]+)*)\]\(([^)]+)\)",
            RegexOptions.Compiled);
        private static readonly Regex DepVersionRegex = new Regex(
            @"-(\d+\.\d+\.\d+(?:\.\d+)?)\.tgz$", RegexOptions.Compiled);

        public static GoogleArchiveCache GetCachedArchive()
        {
            if (_cachedArchive != null) return _cachedArchive;

            _cachedArchive = LoadCacheFromDisk();
            return _cachedArchive;
        }

        public static async Task<GoogleArchiveCache> FetchAndParseArchiveAsync()
        {
            LastFetchError = string.Empty;
            LastFetchCanceled = false;
            LastFetchUsedCache = false;

            using var request = UnityWebRequest.Get(ArchiveUrl);
            request.timeout = 30;
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                    "Fetching Google Archive",
                    "Downloading package registry...",
                    operation.progress))
                {
                    request.Abort();
                    EditorUtility.ClearProgressBar();
                    LastFetchCanceled = true;
                    LastFetchUsedCache = _cachedArchive != null;
                    return _cachedArchive;
                }

                await Task.Yield();
            }

            EditorUtility.ClearProgressBar();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastFetchError = request.error;
                var fallback = _cachedArchive ?? LoadCacheFromDisk();
                LastFetchUsedCache = fallback != null;
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to fetch archive: " +
                    $"{request.error}"
                );
                return fallback;
            }

            var rawText = request.downloadHandler.text;
            var packages = ParseArchive(rawText);
            if (packages.Count == 0)
            {
                LastFetchError =
                    "No packages could be parsed; the source format may have changed.";
                var fallback = _cachedArchive ?? LoadCacheFromDisk();
                LastFetchUsedCache = fallback != null;
                Debug.LogError(
                    "[DependenciesDownloader] Google archive was downloaded " +
                    "but no packages could be parsed. The source format may " +
                    "have changed."
                );
                return fallback;
            }

            _cachedArchive = new GoogleArchiveCache
            {
                FetchedAt = DateTime.UtcNow.ToString(
                    "O", CultureInfo.InvariantCulture),
                Packages = packages
            };

            SaveCacheToDisk(_cachedArchive);
            return _cachedArchive;
        }

        // ── Cache persistence ────────────────────────────────

        private static GoogleArchiveCache LoadCacheFromDisk()
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return null;
                var json = File.ReadAllText(CacheFilePath);
                var cache = JsonUtility.FromJson<GoogleArchiveCache>(json);
                if (cache?.Packages != null && cache.Packages.Count > 0)
                {
                    return cache;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to load cache: " +
                    $"{ex.Message}");
            }

            return null;
        }

        private static void SaveCacheToDisk(GoogleArchiveCache cache)
        {
            if (cache == null) return;
            try
            {
                var dir = Path.GetDirectoryName(CacheFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonUtility.ToJson(cache, prettyPrint: false);
                File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to save cache: " +
                    $"{ex.Message}");
            }
        }

        // ── Parsing ──────────────────────────────────────────

        private static List<GooglePackageInfo> ParseArchive(string rawText)
        {
            var packages = new List<GooglePackageInfo>();
            var lines = rawText.Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.None);

            string currentCategory = null;
            string currentSubCategory = null;
            string pendingDescription = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var catMatch = Regex.Match(line, @"^## (.+)$");
                if (catMatch.Success)
                {
                    currentCategory = catMatch.Groups[1].Value.Trim();
                    currentSubCategory = null;
                    pendingDescription = null;
                    continue;
                }

                var subMatch = Regex.Match(line, @"^### (.+)$");
                if (subMatch.Success)
                {
                    currentSubCategory = subMatch.Groups[1].Value.Trim();
                    pendingDescription = null;
                    continue;
                }

                var nameMatch = PackageNameRegex.Match(line);
                if (!nameMatch.Success)
                {
                    if (pendingDescription == null &&
                        IsDescriptionLine(line, currentSubCategory))
                    {
                        pendingDescription = line;
                    }
                    else if (pendingDescription != null &&
                             IsDescriptionLine(line, currentSubCategory))
                    {
                        pendingDescription += " " + line;
                    }

                    continue;
                }

                var packageName = nameMatch.Groups[1].Value;
                var displayName = DeriveDisplayName(packageName);

                var packageInfo = new GooglePackageInfo
                {
                    Name = packageName,
                    DisplayName = displayName,
                    Description = pendingDescription ?? string.Empty,
                    Category = currentCategory ?? "Other",
                    SubCategory = currentSubCategory ?? string.Empty,
                    Versions = new List<GooglePackageVersion>()
                };

                var versions =
                    ParseVersionsForPackage(lines, i, packageName);
                packageInfo.Versions = versions;

                if (versions.Count > 0)
                {
                    packages.Add(packageInfo);
                }

                pendingDescription = null;
            }

            return packages;
        }

        private static bool IsDescriptionLine(
            string line, string currentSubCategory)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (Regex.IsMatch(line, @"^`")) return false;
            if (Regex.IsMatch(line, @"^\|")) return false;
            if (Regex.IsMatch(line, @"^##")) return false;
            if (Regex.IsMatch(line, @"^###")) return false;
            if (line == currentSubCategory) return false;
            if (Regex.IsMatch(line, @"^\[.*\]\(http")) return false;
            if (Regex.IsMatch(line, @"^<br")) return false;

            return true;
        }

        private static List<GooglePackageVersion> ParseVersionsForPackage(
            string[] lines, int startIndex, string packageName)
        {
            var section = new System.Text.StringBuilder();
            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (i > startIndex &&
                    (PackageNameRegex.IsMatch(line) ||
                     Regex.IsMatch(line, @"^### |^## ")))
                {
                    break;
                }

                if (!string.IsNullOrEmpty(line))
                {
                    section.Append(line).Append(' ');
                }
            }

            var sectionText = section.ToString();
            var matches = VersionRowRegex.Matches(sectionText);
            var versions = new List<GooglePackageVersion>(matches.Count);

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var dependencyStart = match.Index + match.Length;
                var dependencyEnd = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : sectionText.Length;
                var dependencyText = sectionText.Substring(
                    dependencyStart,
                    dependencyEnd - dependencyStart
                );

                versions.Add(new GooglePackageVersion
                {
                    Version = match.Groups["version"].Value,
                    TarballUrl = match.Groups["tarball"].Value,
                    PublishDate = match.Groups["date"].Value,
                    MinUnityVersion = match.Groups["unity"].Value,
                    Dependencies = ExtractDependencies(
                        dependencyText,
                        packageName
                    )
                });
            }

            return versions;
        }

        private static List<GooglePackageDependency> ExtractDependencies(
            string rowText,
            string packageName)
        {
            var dependencies = new List<GooglePackageDependency>();
            var matches = DependencyLinkRegex.Matches(rowText);

            foreach (Match match in matches)
            {
                var depName = match.Groups[1].Value;
                if (depName == packageName) continue;

                var depUrl = match.Groups[2].Value;
                var versionMatch = DepVersionRegex.Match(depUrl);
                var depVersion = versionMatch.Success
                    ? versionMatch.Groups[1].Value
                    : string.Empty;

                dependencies.Add(new GooglePackageDependency
                {
                    Name = depName,
                    TarballUrl = depUrl,
                    Version = depVersion
                });
            }

            return dependencies;
        }

        private static string DeriveDisplayName(string packageName)
        {
            var parts = packageName.Split('.');
            var name = parts[^1];
            var displayParts = new List<string>();

            foreach (var part in name.Split('-', '_'))
            {
                if (part.Length > 0)
                {
                    displayParts.Add(
                        char.ToUpperInvariant(part[0]) +
                        part.Substring(1));
                }
            }

            return string.Join(" ", displayParts);
        }
    }
}
