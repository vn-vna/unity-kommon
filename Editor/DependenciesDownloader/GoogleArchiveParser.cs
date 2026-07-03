using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class GoogleArchiveParser
    {
        private const string ArchiveUrl = "https://developers.google.com/unity/archive.md.txt";
        private const string TarballBasePattern = "https://dl.google.com/games/registry/unity/";

        private static string CacheFilePath =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Temp",
                "DependenciesDownloader", "archive_cache.json"));

        private static GoogleArchiveCache _cachedArchive;

        private static readonly Regex VersionRowRegex = new Regex(
            @"\| (\d+\.\d+\.\d+(?:\.\d+)?) \| (\d{4}-\d{2}) \| (\S+?) \|",
            RegexOptions.Compiled);
        private static readonly Regex TarballLinkRegex = new Regex(
            @"\[\.tgz\]\(([^)]+\.tgz)\)", RegexOptions.Compiled);
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
            using var request = UnityWebRequest.Get(ArchiveUrl);
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
                    return _cachedArchive;
                }

                await Task.Yield();
            }

            EditorUtility.ClearProgressBar();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to fetch archive: " +
                    $"{request.error}");
                return _cachedArchive ?? LoadCacheFromDisk();
            }

            var rawText = request.downloadHandler.text;
            var packages = ParseArchive(rawText);
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

                var nameMatch =
                    Regex.Match(line, @"^`(com\.google\.[^`]+)`$");
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
                    ParseVersionsForPackage(lines, i + 1, packageName);
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
            var versions = new List<GooglePackageVersion>();

            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (string.IsNullOrEmpty(line)) continue;

                if (Regex.IsMatch(line, @"^`com\.google\.[^`]+`$")) break;
                if (Regex.IsMatch(line, @"^### ")) break;
                if (Regex.IsMatch(line, @"^## ")) break;

                var versionMatch = VersionRowRegex.Match(line);
                if (!versionMatch.Success) continue;

                var version = versionMatch.Groups[1].Value;
                var date = versionMatch.Groups[2].Value;
                var minUnity = versionMatch.Groups[3].Value;

                var rowText = line;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var nextLine = lines[j].Trim();
                    if (string.IsNullOrEmpty(nextLine)) break;
                    if (VersionRowRegex.IsMatch(nextLine) ||
                        Regex.IsMatch(
                            nextLine, @"^`com\.google\.[^`]+`$") ||
                        Regex.IsMatch(nextLine, @"^### |^## "))
                    {
                        break;
                    }

                    rowText += " " + nextLine;
                }

                var tarballUrl =
                    ExtractTarballUrl(rowText, packageName, version);
                var dependencies = ExtractDependencies(rowText);

                versions.Add(new GooglePackageVersion
                {
                    Version = version,
                    TarballUrl = tarballUrl,
                    PublishDate = date,
                    MinUnityVersion = minUnity,
                    Dependencies = dependencies
                });
            }

            return versions;
        }

        private static string ExtractTarballUrl(
            string rowText, string packageName, string version)
        {
            var match = TarballLinkRegex.Match(rowText);
            if (match.Success) return match.Groups[1].Value;

            return
                $"{TarballBasePattern}{packageName}/{packageName}-{version}.tgz";
        }

        private static List<GooglePackageDependency> ExtractDependencies(
            string rowText)
        {
            var dependencies = new List<GooglePackageDependency>();
            var matches = DependencyLinkRegex.Matches(rowText);

            foreach (Match match in matches)
            {
                var depName = match.Groups[1].Value;
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
