using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class PackageManifestHelper
    {
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..")).Replace('\\', '/');

        private static string ManifestPath =>
            Path.GetFullPath(Path.Combine(
                ProjectRoot, "Packages", "manifest.json"));

        private static string PackagesLockPath =>
            Path.GetFullPath(Path.Combine(
                ProjectRoot, "Packages", "packages-lock.json"));

        public static string GetRelativeManifestPath(string absoluteFilePath)
        {
            return TryGetPathInsideProject(absoluteFilePath, out var normalizedFile)
                ? ".." + normalizedFile.Substring(ProjectRoot.Length)
                : null;
        }

        public static bool IsPathInsideProject(string path)
        {
            return TryGetPathInsideProject(path, out _);
        }

        public static bool TryGetPathInsideProject(
            string path,
            out string normalizedPath)
        {
            normalizedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return normalizedPath.StartsWith(
                    ProjectRoot + "/",
                    comparison
                );
            }
            catch (Exception)
            {
                normalizedPath = string.Empty;
                return false;
            }
        }

        public static string GetInstalledPackageVersion(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName) ||
                !File.Exists(ManifestPath))
            {
                return string.Empty;
            }

#if NEWTONSOFT_JSON
            try
            {
                var json = File.ReadAllText(ManifestPath);
                var root = JObject.Parse(json);
                var deps = root["dependencies"] as JObject;
                return deps?[packageName]?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Could not read package " +
                    $"'{packageName}' from manifest.json: {ex.Message}"
                );
            }
#endif

            return string.Empty;
        }

        public static Dictionary<string, string> ReadAllTarballEntries()
        {
            var result = new Dictionary<string, string>();
            if (!File.Exists(ManifestPath)) return result;

#if NEWTONSOFT_JSON
            try
            {
                var json = File.ReadAllText(ManifestPath);
                var root = JObject.Parse(json);
                var deps = root["dependencies"] as JObject;
                if (deps == null) return result;

                foreach (var prop in deps.Properties())
                {
                    var value = prop.Value?.ToString();
                    if (!string.IsNullOrEmpty(value) &&
                        value.StartsWith("file:"))
                    {
                        result[prop.Name] = value;
                    }
                }
            }
            catch
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] " +
                    "Could not parse manifest.json.");
            }
#endif

            return result;
        }

        public static bool AddTarballEntries(
            List<DownloadEntry> entries,
            string downloadPath)
        {
            return ApplyTarballChanges(
                entries,
                new HashSet<string>(),
                downloadPath
            );
        }

        public static bool ApplyTarballChanges(
            List<DownloadEntry> additions,
            HashSet<string> removals,
            string downloadPath)
        {
            return ApplyTarballChanges(
                additions,
                removals,
                downloadPath,
                null
            );
        }

        public static bool ApplyTarballChanges(
            List<DownloadEntry> additions,
            HashSet<string> removals,
            string downloadPath,
            Dictionary<string, string> gitAdditions)
        {
            additions ??= new List<DownloadEntry>();
            removals ??= new HashSet<string>();
            gitAdditions ??= new Dictionary<string, string>();
            if (additions.Count == 0 && removals.Count == 0 &&
                gitAdditions.Count == 0)
            {
                return true;
            }

            if (!File.Exists(ManifestPath))
            {
                Debug.LogError(
                    "[DependenciesDownloader] manifest.json not found."
                );
                return false;
            }

            if (additions.Count > 0 &&
                !TryGetPathInsideProject(downloadPath, out _))
            {
                Debug.LogError(
                    "[DependenciesDownloader] Download path must be " +
                    "inside the project folder."
                );
                return false;
            }

#if NEWTONSOFT_JSON
            try
            {
                var originalJson = File.ReadAllText(ManifestPath);
                var root = JObject.Parse(originalJson);
                var dependencies = root["dependencies"] as JObject;
                if (dependencies == null)
                {
                    dependencies = new JObject();
                    root["dependencies"] = dependencies;
                }

                foreach (var packageName in removals)
                {
                    dependencies.Remove(packageName);
                }

                foreach (var entry in additions)
                {
                    var fileName =
                        $"{entry.PackageName}-{entry.Version}.tgz";
                    var fullPath = Path.GetFullPath(Path.Combine(
                        downloadPath,
                        fileName
                    ));
                    var relativePath = GetRelativeManifestPath(fullPath);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        throw new IOException(
                            $"Package path escaped the project: {fullPath}"
                        );
                    }

                    dependencies[entry.PackageName] =
                        $"file:{relativePath}";
                }

                foreach (var gitAddition in gitAdditions)
                {
                    if (string.IsNullOrWhiteSpace(gitAddition.Key) ||
                        string.IsNullOrWhiteSpace(gitAddition.Value))
                    {
                        throw new IOException(
                            "Git package additions must have a name and URL."
                        );
                    }

                    dependencies[gitAddition.Key] = gitAddition.Value;
                }

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };
                var updatedJson = JsonConvert.SerializeObject(root, settings);
                WriteManifestAtomically(originalJson, updatedJson);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Failed to apply manifest " +
                    $"changes: {ex.Message}"
                );
                return false;
            }
#else
            Debug.LogError(
                "[DependenciesDownloader] Newtonsoft.Json is required " +
                "for manifest manipulation."
            );
            return false;
#endif
        }

        public static bool RemoveTarballEntries(
            HashSet<string> packageNames)
        {
            return ApplyTarballChanges(
                new List<DownloadEntry>(),
                packageNames,
                ProjectRoot
            );
        }

        private static void WriteManifestAtomically(
            string originalJson,
            string updatedJson)
        {
            var temporaryPath = ManifestPath + ".tmp";
            var backupPath = ManifestPath + ".bak";
            File.WriteAllText(
                temporaryPath,
                updatedJson,
                new UTF8Encoding(false)
            );

            try
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Replace(
                    temporaryPath,
                    ManifestPath,
                    backupPath,
                    ignoreMetadataErrors: true
                );
            }
            catch (PlatformNotSupportedException)
            {
                CreateBackup(originalJson);
                File.Copy(temporaryPath, ManifestPath, overwrite: true);
                File.Delete(temporaryPath);
            }
        }

        private static void CreateBackup(string content)
        {
            var backupPath = ManifestPath + ".bak";
            try
            {
                File.WriteAllText(
                    backupPath, content, new UTF8Encoding(false));
            }
            catch
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] " +
                    "Could not create manifest backup.");
            }
        }

        public static Dictionary<string, string>
            GetCurrentlyInstalledGooglePackages()
        {
            var result = new Dictionary<string, string>();

#if NEWTONSOFT_JSON
            ReadManifestGooglePackages(result);
            ReadLockedGooglePackages(result);
#endif

            return result;
        }

#if NEWTONSOFT_JSON
        private static void ReadManifestGooglePackages(
            Dictionary<string, string> result)
        {
            if (!File.Exists(ManifestPath)) return;

            try
            {
                var json = File.ReadAllText(ManifestPath);
                var root = JObject.Parse(json);
                var dependencies = root["dependencies"] as JObject;
                if (dependencies == null) return;

                foreach (var property in dependencies.Properties())
                {
                    if (IsGooglePackage(property.Name))
                    {
                        result[property.Name] =
                            property.Value?.ToString() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] Could not read manifest.json: " +
                    ex.Message
                );
            }
        }

        private static void ReadLockedGooglePackages(
            Dictionary<string, string> result)
        {
            if (!File.Exists(PackagesLockPath)) return;

            try
            {
                var json = File.ReadAllText(PackagesLockPath);
                var root = JObject.Parse(json);
                var dependencies = root["dependencies"] as JObject;
                if (dependencies == null) return;

                foreach (var property in dependencies.Properties())
                {
                    if (!IsGooglePackage(property.Name)) continue;

                    var packageInfo = property.Value as JObject;
                    var version = packageInfo?["version"]?.ToString();
                    if (!string.IsNullOrEmpty(version))
                    {
                        result[property.Name] = version;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] Could not read packages-lock.json: " +
                    ex.Message
                );
            }
        }

        private static bool IsGooglePackage(string packageName)
        {
            return packageName.StartsWith(
                "com.google.",
                StringComparison.Ordinal
            );
        }
#endif
    }
}
