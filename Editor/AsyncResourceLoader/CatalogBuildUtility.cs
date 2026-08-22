using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    public static class CatalogBuildUtility
    {
        public static CatalogValidationResult Validate(
            CatalogBuilderConfig config)
        {
            CatalogValidationResult result = new CatalogValidationResult();
            if (config == null)
            {
                result.Errors.Add("Catalog configuration is missing.");
                return result;
            }

            if (!TryGetOutputDirectory(config, out string outputDirectory,
                    out string outputError))
            {
                result.Errors.Add(outputError);
            }
            else
            {
                result.OutputDirectory = outputDirectory;
            }

            if (!TryNormalizeFileName(config.CatalogFileName,
                    out string catalogFileName, out string catalogFileError))
            {
                result.Errors.Add(catalogFileError);
            }
            else
            {
                result.CatalogRelativePath = catalogFileName;
            }

            if (config.Entries == null || config.Entries.Count == 0)
            {
                result.Errors.Add("Stage at least one level file.");
                return result;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> paths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Entries.Count; i++)
            {
                StagedCatalogEntry entry = config.Entries[i];
                ValidateEntry(entry, i, ids, paths, result);
            }

            if (!string.IsNullOrEmpty(result.CatalogRelativePath)
                && paths.Contains(result.CatalogRelativePath))
            {
                result.Errors.Add(
                    "A level output path conflicts with the catalog file name.");
            }

            if (result.Errors.Count == 0)
            {
                result.ManifestHash = CreateManifestHash(result.ValidEntries);
            }

            return result;
        }

        public static bool TryGetOutputDirectory(
            CatalogBuilderConfig config,
            out string outputDirectory,
            out string error)
        {
            outputDirectory = null;
            error = null;
            if (string.IsNullOrWhiteSpace(config?.OutputFolder))
            {
                error = "Output folder is required.";
                return false;
            }

            string normalized = config.OutputFolder.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                && !string.Equals(normalized, "Assets", StringComparison.Ordinal))
            {
                error = "Output folder must be project-relative and start with 'Assets/'.";
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                error = "Unable to resolve the Unity project root.";
                return false;
            }

            outputDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, normalized));
            string assetsDirectory = Path.GetFullPath(Application.dataPath);
            if (!IsPathWithinDirectory(outputDirectory, assetsDirectory))
            {
                error = "Output folder must remain inside the project's Assets directory.";
                outputDirectory = null;
                return false;
            }

            return true;
        }

        public static bool TryNormalizeRelativePath(
            string value,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Relative path is required.";
                return false;
            }

            string candidate = value.Replace('\\', '/').Trim().TrimStart('/');
            if (Path.IsPathRooted(candidate)
                || candidate.IndexOf(':') >= 0)
            {
                error = "Relative path must not be rooted.";
                return false;
            }

            string[] segments = candidate.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i])
                    || segments[i] == "."
                    || segments[i] == "..")
                {
                    error = "Relative path must not contain empty, '.' or '..' segments.";
                    return false;
                }
            }

            normalized = string.Join("/", segments);
            return true;
        }

        public static string ComputeFileHash(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                using SHA256 sha256 = SHA256.Create();
                using FileStream stream = File.OpenRead(filePath);
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "")
                    .ToLowerInvariant();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string BuildCatalogJson(
            int version,
            IReadOnlyList<ValidatedCatalogEntry> entries)
        {
            CatalogDocument document = new CatalogDocument
            {
                Version = version,
                Entries = new CatalogDocumentEntry[entries.Count]
            };

            for (int i = 0; i < entries.Count; i++)
            {
                ValidatedCatalogEntry entry = entries[i];
                document.Entries[i] = new CatalogDocumentEntry
                {
                    Id = entry.Id,
                    Type = entry.Type,
                    RelativePath = entry.RelativePath,
                    ContentHash = entry.ContentHash
                };
            }

            return JsonConvert.SerializeObject(
                document,
                Formatting.Indented,
                new StringEnumConverter());
        }

        public static CatalogBuildState CreateBuildState(
            CatalogValidationResult validation,
            int version)
        {
            CatalogBuildState state = new CatalogBuildState
            {
                ManifestHash = validation.ManifestHash,
                Version = version,
                CatalogRelativePath = validation.CatalogRelativePath
            };

            foreach (ValidatedCatalogEntry entry in validation.ValidEntries)
            {
                state.Entries.Add(new GeneratedCatalogEntry
                {
                    Id = entry.Id,
                    Type = entry.Type,
                    RelativePath = entry.RelativePath,
                    ContentHash = entry.ContentHash
                });
            }

            return state;
        }

        public static List<string> GetStaleRelativePaths(
            CatalogBuildState previous,
            CatalogBuildState current)
        {
            List<string> stalePaths = new List<string>();
            if (previous?.Entries == null)
            {
                return stalePaths;
            }

            HashSet<string> currentPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (current?.Entries != null)
            {
                foreach (GeneratedCatalogEntry entry in current.Entries)
                {
                    currentPaths.Add(entry.RelativePath);
                }
            }

            foreach (GeneratedCatalogEntry entry in previous.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.RelativePath)
                    && !currentPaths.Contains(entry.RelativePath))
                {
                    stalePaths.Add(entry.RelativePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(previous.CatalogRelativePath)
                && !string.Equals(
                    previous.CatalogRelativePath,
                    current?.CatalogRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                stalePaths.Add(previous.CatalogRelativePath);
            }

            return stalePaths;
        }

        private static void ValidateEntry(
            StagedCatalogEntry entry,
            int index,
            ISet<string> ids,
            ISet<string> paths,
            CatalogValidationResult result)
        {
            string label = $"Entry {index + 1}";
            if (entry == null)
            {
                result.Errors.Add($"{label} is missing.");
                return;
            }

            string id = entry.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                result.Errors.Add($"{label} has an empty ID.");
            }
            else if (!ids.Add(id))
            {
                result.Errors.Add($"Duplicate level ID '{id}'.");
            }

            if (!TryNormalizeRelativePath(entry.RelativePath,
                    out string relativePath, out string pathError))
            {
                result.Errors.Add($"{label}: {pathError}");
            }
            else if (!paths.Add(relativePath))
            {
                result.Errors.Add($"Duplicate output path '{relativePath}'.");
            }

            if (string.IsNullOrWhiteSpace(entry.SourceFilePath)
                || !File.Exists(entry.SourceFilePath))
            {
                result.Errors.Add($"{label} source file is missing: '{entry.SourceFilePath}'.");
                return;
            }

            string hash = ComputeFileHash(entry.SourceFilePath);
            if (string.IsNullOrEmpty(hash))
            {
                result.Errors.Add($"{label} source file cannot be hashed.");
                return;
            }

            if (string.IsNullOrWhiteSpace(id) || relativePath == null)
            {
                return;
            }

            result.ValidEntries.Add(new ValidatedCatalogEntry(
                entry,
                id,
                entry.Type,
                relativePath,
                hash));
        }

        private static bool TryNormalizeFileName(
            string value,
            out string fileName,
            out string error)
        {
            fileName = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Catalog file name is required.";
                return false;
            }

            string candidate = value.Trim();
            if (!string.Equals(candidate, Path.GetFileName(candidate),
                    StringComparison.Ordinal)
                || !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                error = "Catalog file must be a .json file name without directory segments.";
                return false;
            }

            fileName = candidate;
            return true;
        }

        private static string CreateManifestHash(
            List<ValidatedCatalogEntry> entries)
        {
            List<ValidatedCatalogEntry> ordered
                = new List<ValidatedCatalogEntry>(entries);
            ordered.Sort((first, second) => string.CompareOrdinal(
                first.Id + "\n" + first.RelativePath,
                second.Id + "\n" + second.RelativePath));

            StringBuilder builder = new StringBuilder(ordered.Count * 160);
            foreach (ValidatedCatalogEntry entry in ordered)
            {
                builder.Append(entry.Id).Append('\n')
                    .Append((int)entry.Type).Append('\n')
                    .Append(entry.RelativePath).Append('\n')
                    .Append(entry.ContentHash).Append('\n');
            }

            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
            using SHA256 sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(bytes))
                .Replace("-", "").ToLowerInvariant();
        }

        private static bool IsPathWithinDirectory(
            string candidate,
            string directory)
        {
            string normalizedDirectory = directory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidate.StartsWith(
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, directory, StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class CatalogDocument
        {
            [JsonProperty("version")]
            public int Version;

            [JsonProperty("entries")]
            public CatalogDocumentEntry[] Entries;
        }

        [Serializable]
        private sealed class CatalogDocumentEntry
        {
            [JsonProperty("id")]
            public string Id;

            [JsonProperty("type")]
            public DataType Type;

            [JsonProperty("relativePath")]
            public string RelativePath;

            [JsonProperty("contentHash")]
            public string ContentHash;
        }
    }

    public sealed class CatalogValidationResult
    {
        public string OutputDirectory;
        public string CatalogRelativePath;
        public string ManifestHash;
        public List<string> Errors = new List<string>();
        public List<ValidatedCatalogEntry> ValidEntries
            = new List<ValidatedCatalogEntry>();

        public bool IsValid => Errors.Count == 0;
    }

    public readonly struct ValidatedCatalogEntry
    {
        public StagedCatalogEntry Source { get; }
        public string Id { get; }
        public DataType Type { get; }
        public string RelativePath { get; }
        public string ContentHash { get; }

        public ValidatedCatalogEntry(
            StagedCatalogEntry source,
            string id,
            DataType type,
            string relativePath,
            string contentHash)
        {
            Source = source;
            Id = id;
            Type = type;
            RelativePath = relativePath;
            ContentHash = contentHash;
        }
    }
}
