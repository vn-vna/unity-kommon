using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class PackageManifestHelper
    {
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..")).Replace('\\', '/');

        private static string ManifestPath =>
            Path.GetFullPath(Path.Combine(
                ProjectRoot, "Packages", "manifest.json"));

        public static string GetRelativeManifestPath(string absoluteFilePath)
        {
            var normalizedFile =
                Path.GetFullPath(absoluteFilePath).Replace('\\', '/');
            var normalizedRoot = ProjectRoot;

            if (!normalizedFile.StartsWith(normalizedRoot + "/"))
            {
                return null;
            }

            return ".." + normalizedFile.Substring(normalizedRoot.Length);
        }

        public static bool IsPathInsideProject(string path)
        {
            var normalized = Path.GetFullPath(path).Replace('\\', '/');
            return normalized.StartsWith(ProjectRoot + "/");
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
            List<DownloadEntry> entries, string downloadPath)
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogError(
                    "[DependenciesDownloader] manifest.json not found.");
                return false;
            }

            var normalizedDownloadPath =
                Path.GetFullPath(downloadPath).Replace('\\', '/');
            if (!normalizedDownloadPath.StartsWith(ProjectRoot + "/"))
            {
                Debug.LogError(
                    "[DependenciesDownloader] Download path must be " +
                    "inside the project folder.");
                return false;
            }

#if NEWTONSOFT_JSON
            try
            {
                var jsonText = File.ReadAllText(ManifestPath);
                CreateBackup(jsonText);

                var root = JObject.Parse(jsonText);
                var deps = root["dependencies"] as JObject;
                if (deps == null)
                {
                    deps = new JObject();
                    root["dependencies"] = deps;
                }

                foreach (var entry in entries)
                {
                    var fileName =
                        $"{entry.PackageName}-{entry.Version}.tgz";
                    var fullPath = Path.GetFullPath(
                        Path.Combine(downloadPath, fileName))
                        .Replace('\\', '/');
                    var relativePath =
                        GetRelativeManifestPath(fullPath);
                    deps[entry.PackageName] = $"file:{relativePath}";
                }

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };

                var updatedJson =
                    JsonConvert.SerializeObject(root, settings);
                File.WriteAllText(
                    ManifestPath, updatedJson, new UTF8Encoding(false));
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] " +
                    $"Failed to update manifest.json: {ex.Message}");
                return false;
            }
#else
            Debug.LogError(
                "[DependenciesDownloader] Newtonsoft.Json is required " +
                "for manifest manipulation.");
            return false;
#endif
        }

        public static bool RemoveTarballEntries(
            HashSet<string> packageNames)
        {
            if (packageNames == null || packageNames.Count == 0)
                return true;
            if (!File.Exists(ManifestPath)) return false;

#if NEWTONSOFT_JSON
            try
            {
                var jsonText = File.ReadAllText(ManifestPath);
                CreateBackup(jsonText);

                var root = JObject.Parse(jsonText);
                var deps = root["dependencies"] as JObject;
                if (deps == null) return true;

                var removed = false;
                foreach (var name in packageNames)
                {
                    if (deps.Remove(name)) removed = true;
                }

                if (!removed) return true;

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };
                var updatedJson =
                    JsonConvert.SerializeObject(root, settings);
                File.WriteAllText(
                    ManifestPath, updatedJson, new UTF8Encoding(false));
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] " +
                    $"Failed to remove entries from manifest.json: " +
                    $"{ex.Message}");
                return false;
            }
#else
            Debug.LogError(
                "[DependenciesDownloader] Newtonsoft.Json is required " +
                "for manifest manipulation.");
            return false;
#endif
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
                    if (prop.Name.StartsWith("com.google."))
                    {
                        result[prop.Name] =
                            prop.Value?.ToString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] " +
                    "Could not read manifest.json.");
            }
#endif

            return result;
        }
    }
}
