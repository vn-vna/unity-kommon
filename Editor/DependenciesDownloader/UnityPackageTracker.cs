using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class UnityPackageTracker
    {
        private const int TarBlockSize = 512;
        private const int MaxPathnameBytes = 32 * 1024;

        private static string TrackingFilePath(string packageName) =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "ProjectSettings",
                "Scheherazade", "DependenciesDownloader",
                $"{GetSafeFileName(packageName)}_install.json"));

        private static string LegacyTrackingFilePath(string packageName) =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Temp",
                "DependenciesDownloader",
                $"{GetSafeFileName(packageName)}_install.json"));

        public static List<string> EnumerateFilesInPackage(
            string packagePath)
        {
            var files = new List<string>();
            if (!File.Exists(packagePath)) return files;

            var uniquePaths = new HashSet<string>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            );

            try
            {
                using var fileStream = File.OpenRead(packagePath);
                using var gzipStream =
                    new GZipStream(fileStream, CompressionMode.Decompress);
                using var reader = new BinaryReader(gzipStream);

                while (TryReadTarHeader(reader, out var entryName, out var size))
                {
                    if (size < 0)
                    {
                        throw new InvalidDataException(
                            $"Invalid TAR entry size for '{entryName}'."
                        );
                    }

                    if (entryName.EndsWith(
                            "/pathname",
                            StringComparison.Ordinal
                        ) && size <= MaxPathnameBytes)
                    {
                        var pathBytes = reader.ReadBytes((int)size);
                        if (pathBytes.Length != size)
                        {
                            throw new EndOfStreamException(
                                $"Unexpected end of package while reading '{entryName}'."
                            );
                        }

                        var assetPath = Encoding.UTF8.GetString(pathBytes)
                            .TrimEnd('\0', '\r', '\n')
                            .Replace('\\', '/');
                        if (IsSafeAssetPath(assetPath) &&
                            uniquePaths.Add(assetPath))
                        {
                            files.Add(assetPath);
                        }
                    }
                    else
                    {
                        SkipBytes(reader, size);
                    }

                    SkipBytes(reader, PaddingFor(size));
                }
            }
            catch (Exception ex)
            {
                files.Clear();
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to parse " +
                    $".unitypackage '{packagePath}': {ex.Message}"
                );
            }

            return files;
        }

        public static UnityPackageInstallRecord GetInstallRecord(
            string packageName)
        {
            var record = ReadInstallRecord(TrackingFilePath(packageName));
            if (record != null) return record;

            var legacyPath = LegacyTrackingFilePath(packageName);
            record = ReadInstallRecord(legacyPath);
            if (record == null) return null;

            if (SaveInstallRecord(
                    record.PackageName,
                    record.Version,
                    record.TrackedFiles
                ))
            {
                TryDeleteFile(legacyPath);
            }

            return record;
        }

        public static bool SaveInstallRecord(
            string packageName,
            string version,
            List<string> files)
        {
            if (string.IsNullOrWhiteSpace(packageName) ||
                string.IsNullOrWhiteSpace(version) ||
                files == null || files.Count == 0)
            {
                Debug.LogWarning(
                    "[DependenciesDownloader] Refusing to save an empty " +
                    $"install record for '{packageName}'."
                );
                return false;
            }

            try
            {
                var path = TrackingFilePath(packageName);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var record = new UnityPackageInstallRecord
                {
                    PackageName = packageName,
                    Version = version,
                    InstalledAt = DateTime.UtcNow.ToString("O"),
                    TrackedFiles = files
                };

                var json = JsonUtility.ToJson(record, prettyPrint: true);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to save tracking: " +
                    $"{ex.Message}"
                );
                return false;
            }
        }

        public static bool RemoveTrackedFiles(
            string packageName,
            ICollection<string> preservedPaths = null)
        {
            var record = GetInstallRecord(packageName);
            if (record?.TrackedFiles == null || record.TrackedFiles.Count == 0)
            {
                return false;
            }

            var comparer = Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var preserved = preservedPaths == null
                ? new HashSet<string>(comparer)
                : new HashSet<string>(preservedPaths, comparer);

            var hadFailure = false;
            var paths = new List<string>(record.TrackedFiles);
            paths.Sort((left, right) => right.Length.CompareTo(left.Length));

            foreach (var relativePath in paths)
            {
                if (preserved.Contains(relativePath)) continue;
                if (!IsSafeAssetPath(relativePath))
                {
                    hadFailure = true;
                    Debug.LogWarning(
                        $"[DependenciesDownloader] Refusing to delete unsafe " +
                        $"tracked path '{relativePath}'."
                    );
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    relativePath
                ));
                if (!PackageManifestHelper.TryGetPathInsideProject(
                        fullPath,
                        out var safeFullPath
                    ))
                {
                    hadFailure = true;
                    continue;
                }

                try
                {
                    if (File.Exists(safeFullPath))
                    {
                        File.Delete(safeFullPath);
                        if (!TryDeleteFile(safeFullPath + ".meta"))
                            hadFailure = true;
                    }
                    else if (Directory.Exists(safeFullPath) &&
                             IsDirectoryEmpty(safeFullPath))
                    {
                        Directory.Delete(safeFullPath);
                        if (!TryDeleteFile(safeFullPath + ".meta"))
                            hadFailure = true;
                    }
                    else if (!Directory.Exists(safeFullPath) &&
                             !TryDeleteFile(safeFullPath + ".meta"))
                    {
                        hadFailure = true;
                    }
                }
                catch (Exception ex)
                {
                    hadFailure = true;
                    Debug.LogWarning(
                        $"[DependenciesDownloader] Failed to delete " +
                        $"'{relativePath}': {ex.Message}"
                    );
                }
            }

            if (!hadFailure)
            {
                DeleteTrackingFile(packageName);
            }

            AssetDatabase.Refresh();
            return !hadFailure;
        }

        public static void DeleteTrackingFile(string packageName)
        {
            TryDeleteFile(TrackingFilePath(packageName));
            TryDeleteFile(LegacyTrackingFilePath(packageName));
        }

        private static UnityPackageInstallRecord ReadInstallRecord(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var record =
                    JsonUtility.FromJson<UnityPackageInstallRecord>(json);
                return record?.TrackedFiles != null ? record : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to read tracking " +
                    $"record '{path}': {ex.Message}"
                );
                return null;
            }
        }

        private static bool TryReadTarHeader(
            BinaryReader reader,
            out string entryName,
            out long size)
        {
            entryName = string.Empty;
            size = 0;

            var header = reader.ReadBytes(TarBlockSize);
            if (header.Length == 0) return false;
            if (header.Length < TarBlockSize)
            {
                throw new EndOfStreamException(
                    "Unexpected end of package while reading a TAR header."
                );
            }

            entryName = ReadTarString(header, 0, 100);
            if (string.IsNullOrEmpty(entryName)) return false;

            size = ReadTarOctal(header, 124, 12);
            return true;
        }

        private static void SkipBytes(BinaryReader reader, long byteCount)
        {
            var buffer = new byte[8192];
            var remaining = byteCount;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = reader.Read(buffer, 0, toRead);
                if (read <= 0)
                {
                    throw new EndOfStreamException(
                        "Unexpected end of package while reading a TAR entry."
                    );
                }

                remaining -= read;
            }
        }

        private static long PaddingFor(long size)
        {
            var remainder = size % TarBlockSize;
            return remainder == 0 ? 0 : TarBlockSize - remainder;
        }

        private static bool IsSafeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                if (Path.IsPathRooted(path)) return false;

                var normalized = path.Replace('\\', '/');
                if (!normalized.StartsWith(
                        "Assets/",
                        StringComparison.Ordinal
                    ))
                {
                    return false;
                }

                var fullPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    normalized
                )).Replace('\\', '/');
                var assetsRoot = Path.GetFullPath(Application.dataPath)
                    .Replace('\\', '/');
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return fullPath.StartsWith(
                    assetsRoot + "/",
                    comparison
                );
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsDirectoryEmpty(string path)
        {
            using var enumerator =
                Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return !enumerator.MoveNext();
        }

        private static string GetSafeFileName(string value)
        {
            var safeName = value ?? string.Empty;
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidCharacter, '_');
            }

            return safeName;
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to delete '{path}': " +
                    ex.Message
                );
                return false;
            }
        }

        private static string ReadTarString(byte[] buffer, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && end < buffer.Length &&
                   buffer[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(
                buffer, offset, end - offset
            ).Trim();
        }

        private static long ReadTarOctal(byte[] buffer, int offset, int length)
        {
            var value = ReadTarString(buffer, offset, length);
            if (string.IsNullOrEmpty(value)) return 0;

            try
            {
                return Convert.ToInt64(value, 8);
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
