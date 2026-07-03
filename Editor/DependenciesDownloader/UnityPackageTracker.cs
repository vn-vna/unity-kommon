using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class UnityPackageTracker
    {
        private static string TrackingFilePath(string packageName) =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Temp",
                "DependenciesDownloader",
                $"{packageName}_install.json"));

        public static List<string> EnumerateFilesInPackage(
            string packagePath)
        {
            var files = new List<string>();
            if (!File.Exists(packagePath)) return files;

            try
            {
                using var fileStream =
                    File.OpenRead(packagePath);
                using var gzipStream =
                    new GZipStream(fileStream, CompressionMode.Decompress);
                using var reader = new BinaryReader(gzipStream);

                while (true)
                {
                    var header = reader.ReadBytes(512);
                    if (header.Length < 512) break;

                    var fileName = ReadTarString(header, 0, 100);
                    if (string.IsNullOrEmpty(fileName)) break;

                    var fileSize = ReadTarOctal(header, 124, 12);

                    var typeFlag = header[156];
                    if (typeFlag == 0 || typeFlag == '0')
                    {
                        files.Add(fileName);
                    }

                    var contentBlocks =
                        (fileSize + 511L) / 512L;
                    reader.BaseStream.Seek(
                        contentBlocks * 512, SeekOrigin.Current);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to parse " +
                    $".unitypackage: {ex.Message}");
            }

            return files;
        }

        public static UnityPackageInstallRecord GetInstallRecord(
            string packageName)
        {
            var path = TrackingFilePath(packageName);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<UnityPackageInstallRecord>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveInstallRecord(
            string packageName,
            string version,
            List<string> files)
        {
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

                var json = JsonUtility.ToJson(record, prettyPrint: false);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DependenciesDownloader] Failed to save tracking: " +
                    $"{ex.Message}");
            }
        }

        public static bool RemoveTrackedFiles(string packageName)
        {
            var record = GetInstallRecord(packageName);
            if (record?.TrackedFiles == null) return false;

            var projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            var deleted = false;

            foreach (var relativePath in record.TrackedFiles)
            {
                if (string.IsNullOrEmpty(relativePath)) continue;
                if (Path.IsPathRooted(relativePath)) continue;

                var fullPath = Path.GetFullPath(
                    Path.Combine(projectRoot, relativePath));
                if (!fullPath.StartsWith(projectRoot)) continue;

                try
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        deleted = true;
                    }

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, recursive: true);
                        deleted = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[DependenciesDownloader] Failed to delete " +
                        $"{fullPath}: {ex.Message}");
                }
            }

            DeleteTrackingFile(packageName);
            AssetDatabase.Refresh();
            return deleted;
        }

        public static void DeleteTrackingFile(string packageName)
        {
            var path = TrackingFilePath(packageName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    var metaPath = path + ".meta";
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }
            }
            catch
            {
                // ignore
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
                buffer, offset, end - offset);
        }

        private static long ReadTarOctal(byte[] buffer, int offset, int length)
        {
            var str = ReadTarString(buffer, offset, length);
            if (string.IsNullOrEmpty(str)) return 0;

            try
            {
                return Convert.ToInt64(str, 8);
            }
            catch
            {
                return 0;
            }
        }
    }
}
