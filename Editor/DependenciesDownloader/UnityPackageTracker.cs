using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
            if (!File.Exists(packagePath)) return new List<string>();
            try
            {
                using var stream = File.OpenRead(packagePath);
                return EnumerateFilesInPackage(stream);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependenciesDownloader] Could not inspect '{packagePath}': {ex.Message}");
                return new List<string>();
            }
        }

        public static List<string> EnumerateFilesInPackage(Stream packageStream)
        {
            var files = new List<string>();

            var uniquePaths = new HashSet<string>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            );

            try
            {
                using var gzipStream =
                    new GZipStream(packageStream, CompressionMode.Decompress, leaveOpen: true);
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
                SkipToEnd(reader);
            }
            catch (Exception ex)
            {
                files.Clear();
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to parse " +
                    $".unitypackage stream: {ex.Message}"
                );
            }

            return files;
        }

        public static void CreateFilteredPackage(
            string sourcePath,
            string destinationPath,
            Predicate<string> includeAsset,
            CancellationToken cancellationToken = default)
        {
            var includedGroups = new HashSet<string>();
            using (var file = File.OpenRead(sourcePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
            {
                while (TryReadTarHeader(reader, out var name, out var size))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (size < 0) throw new InvalidDataException("Invalid TAR entry size.");
                    if (name.EndsWith("/pathname", StringComparison.Ordinal) && size <= MaxPathnameBytes)
                    {
                        var bytes = reader.ReadBytes((int)size);
                        if (bytes.Length != size) throw new EndOfStreamException("Truncated pathname entry.");
                        var path = Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\r', '\n').Replace('\\', '/');
                        if (IsSafeAssetPath(path) && includeAsset(path)) includedGroups.Add(GetTarAssetGroup(name));
                    }
                    else SkipBytes(reader, size);
                    SkipBytes(reader, PaddingFor(size));
                }
            }
            if (includedGroups.Count == 0) throw new InvalidDataException("No SDK assets remain after filtering.");

            var temporaryPath = destinationPath + ".download";
            try
            {
                using (var file = File.OpenRead(sourcePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var reader = new BinaryReader(gzip))
                using (var output = File.Create(temporaryPath))
                using (var compressed = new GZipStream(output, CompressionMode.Compress))
                {
                    var buffer = new byte[64 * 1024];
                    while (TryReadTarHeader(reader, out var name, out var size, out var header))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (size < 0) throw new InvalidDataException("Invalid TAR entry size.");
                        var keep = includedGroups.Contains(GetTarAssetGroup(name));
                        if (keep) compressed.Write(header, 0, header.Length);
                        var remaining = size + PaddingFor(size);
                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0) throw new EndOfStreamException("Truncated package asset.");
                            if (keep) compressed.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                    compressed.Write(new byte[TarBlockSize * 2], 0, TarBlockSize * 2);
                }
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(temporaryPath, destinationPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static string GetTarAssetGroup(string entryName)
        {
            if (entryName.StartsWith("./", StringComparison.Ordinal)) entryName = entryName.Substring(2);
            var separator = entryName.IndexOf('/');
            return separator < 0 ? entryName : entryName.Substring(0, separator);
        }

        public static bool ValidateTarballPackage(string path, string expectedName, string expectedVersion)
        {
            try
            {
                using var file = File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new BinaryReader(gzip);
                var matches = false;
                while (TryReadTarHeader(reader, out var entryName, out var size))
                {
                    if (size < 0) return false;
                    if ((entryName == "package/package.json" || entryName == "./package/package.json") && size <= 1024 * 1024)
                    {
                        var bytes = reader.ReadBytes((int)size);
                        if (bytes.Length != size) return false;
                        var metadata = JsonUtility.FromJson<TarballMetadata>(Encoding.UTF8.GetString(bytes));
                        matches = metadata?.name == expectedName && metadata.version == expectedVersion;
                    }
                    else SkipBytes(reader, size);
                    SkipBytes(reader, PaddingFor(size));
                }
                // Consume the gzip trailer as well, so corrupt downloads are not reused.
                SkipToEnd(reader);
                return matches;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SkipToEnd(BinaryReader reader)
        {
            var buffer = new byte[8192];
            while (reader.Read(buffer, 0, buffer.Length) > 0) { }
        }

        [Serializable]
        private sealed class TarballMetadata
        {
            public string name;
            public string version;
        }

        public static UnityPackageInstallRecord GetInstallRecord(
            string packageName)
        {
            var record = ReadInstallRecord(TrackingFilePath(packageName));
            if (record != null) return record;

            var legacyPath = LegacyTrackingFilePath(packageName);
            record = ReadInstallRecord(legacyPath);
            if (record == null) return null;

            if (WriteInstallRecord(record))
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
                var record = new UnityPackageInstallRecord
                {
                    PackageName = packageName,
                    Version = version,
                    InstalledAt = DateTime.UtcNow.ToString("O"),
                    TrackedFiles = files,
                    Fingerprints = CaptureFingerprints(files),
                    HasFingerprints = true
                };

                return WriteInstallRecord(record);
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

        private static bool WriteInstallRecord(UnityPackageInstallRecord record)
        {
            try
            {
                var path = TrackingFilePath(record.PackageName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(record, prettyPrint: true), new UTF8Encoding(false));
                if (!File.Exists(path)) File.Move(temporaryPath, path);
                else
                {
                    try { File.Replace(temporaryPath, path, null); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, path, overwrite: true);
                        File.Delete(temporaryPath);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependenciesDownloader] Failed to save tracking for {record.PackageName}: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveTrackedFiles(
            string packageName,
            ICollection<string> preservedPaths = null,
            bool allowModifiedFiles = false,
            bool refreshAssets = true)
        {
            var record = GetInstallRecord(packageName);
            if (record?.TrackedFiles == null || record.TrackedFiles.Count == 0)
            {
                return false;
            }

            if (!allowModifiedFiles && GetModifiedFiles(record, preservedPaths).Count > 0)
            {
                Debug.LogWarning($"[DependenciesDownloader] {packageName} has locally modified files. Review them before removal.");
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

            if (refreshAssets) AssetDatabase.Refresh();
            return !hadFailure;
        }

        public static void DeleteTrackingFile(string packageName)
        {
            TryDeleteFile(TrackingFilePath(packageName));
            TryDeleteFile(LegacyTrackingFilePath(packageName));
        }

        public static List<string> GetMissingFiles(UnityPackageInstallRecord record)
        {
            var missing = new List<string>();
            if (record?.TrackedFiles == null) return missing;
            foreach (var path in record.TrackedFiles)
            {
                if (!IsSafeAssetPath(path))
                {
                    missing.Add(path);
                    continue;
                }
                var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) missing.Add(path);
                else if (record.HasFingerprints && !File.Exists(fullPath + ".meta")) missing.Add(path + ".meta");
            }
            return missing;
        }

        public static List<string> GetModifiedFiles(
            UnityPackageInstallRecord record,
            ICollection<string> preservedPaths = null)
        {
            var modified = new List<string>();
            if (record?.Fingerprints == null) return modified;
            foreach (var fingerprint in record.Fingerprints)
            {
                if (preservedPaths != null && preservedPaths.Contains(fingerprint.Path)) continue;
                if (!IsSafeAssetPath(fingerprint.Path)) continue;
                var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", fingerprint.Path));
                if ((File.Exists(fullPath) || Directory.Exists(fullPath)) &&
                    ((File.Exists(fullPath) && HashFile(fullPath) != fingerprint.Hash) ||
                     (!string.IsNullOrEmpty(fingerprint.MetaHash) && HashFile(fullPath + ".meta") != fingerprint.MetaHash)))
                    modified.Add(fingerprint.Path);
            }
            return modified;
        }

        private static List<TrackedAssetFingerprint> CaptureFingerprints(List<string> files)
        {
            var fingerprints = new List<TrackedAssetFingerprint>();
            foreach (var path in files)
            {
                if (!IsSafeAssetPath(path)) continue;
                var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) continue;
                fingerprints.Add(new TrackedAssetFingerprint
                {
                    Path = path,
                    Hash = File.Exists(fullPath) ? HashFile(fullPath) : string.Empty,
                    MetaHash = HashFile(fullPath + ".meta")
                });
            }
            return fingerprints;
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) return string.Empty;
            using var stream = File.OpenRead(path);
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(stream));
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
            return TryReadTarHeader(reader, out entryName, out size, out _);
        }

        private static bool TryReadTarHeader(
            BinaryReader reader,
            out string entryName,
            out long size,
            out byte[] header)
        {
            entryName = string.Empty;
            size = 0;

            header = reader.ReadBytes(TarBlockSize);
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
