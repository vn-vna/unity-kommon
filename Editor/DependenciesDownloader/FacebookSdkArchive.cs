using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class FacebookSdkArchive
    {
        public const string DependencyManagerPackageId = "com.google.external-dependency-manager";
        private const long MaxPayloadBytes = 256 * 1024 * 1024;
        private const string ResolverRoot = "Assets/ExternalDependencyManager";

        public static bool IsUsable(string zipPath)
        {
            try
            {
                using var file = File.OpenRead(zipPath);
                using var archive = new ZipArchive(file, ZipArchiveMode.Read);
                var entry = GetUniquePayload(archive);
                using var stream = entry.Open();
                return UnityPackageTracker.EnumerateFilesInPackage(stream)
                    .Any(path => path.StartsWith("Assets/FacebookSDK/", StringComparison.Ordinal));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string PrepareForImport(
            string zipPath,
            string version,
            bool dependencyManagerOwnedByUpm,
            string installedResolverVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadPath = Path.ChangeExtension(zipPath, ".unitypackage");
            var temporaryPath = payloadPath + ".download";
            try
            {
                using (var file = File.OpenRead(zipPath))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var entry = GetUniquePayload(archive);
                    if (DependencyVersionUtility.Extract(entry.Name) != version)
                        throw new InvalidDataException("The Facebook ZIP payload version does not match the queued release.");
                    using var source = entry.Open();
                    using var output = File.Create(temporaryPath);
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(payloadPath)) File.Delete(payloadPath);
                File.Move(temporaryPath, payloadPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            var paths = UnityPackageTracker.EnumerateFilesInPackage(payloadPath);
            if (!paths.Any(path => path.StartsWith("Assets/FacebookSDK/", StringComparison.Ordinal)))
                throw new InvalidDataException("The extracted package does not contain FacebookSDK assets.");
            if (!dependencyManagerOwnedByUpm || !paths.Any(IsResolverPath)) return payloadPath;

            var bundledVersion = GetBundledResolverVersion(paths);
            if (string.IsNullOrEmpty(bundledVersion) ||
                !DependencyVersionUtility.Satisfies(DependencyManagerPackageId, installedResolverVersion, bundledVersion))
                throw new InvalidOperationException(
                    $"Facebook {version} bundles EDM {bundledVersion ?? "(unknown version)"}, but UPM provides {installedResolverVersion}. " +
                    "Update EDM in Package Manager before applying Facebook, then retry. Its bundled Assets copy will be excluded."
                );

            var filteredPath = Path.ChangeExtension(zipPath, ".filtered.unitypackage");
            UnityPackageTracker.CreateFilteredPackage(payloadPath, filteredPath, path => !IsResolverPath(path), cancellationToken);
            return filteredPath;
        }

        public static string GetBundledResolverVersion(IEnumerable<string> paths)
        {
            string latest = null;
            foreach (var path in paths.Where(IsResolverPath))
            {
                var match = Regex.Match(path, @"(?:/Editor/|_version-)(\d+\.\d+\.\d+(?:\.\d+)?)");
                if (!match.Success) continue;
                var version = match.Groups[1].Value;
                if (latest == null || DependencyVersionUtility.TryCompare(version, latest, out var comparison) && comparison > 0)
                    latest = version;
            }
            return latest;
        }

        private static bool IsResolverPath(string path)
        {
            return path.Equals(ResolverRoot, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ResolverRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static ZipArchiveEntry GetUniquePayload(ZipArchive archive)
        {
            var payloads = archive.Entries.Where(entry => entry.FullName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)).ToList();
            if (payloads.Count != 1)
                throw new InvalidDataException("The Facebook SDK ZIP must contain exactly one .unitypackage payload.");
            if (payloads[0].Length <= 0 || payloads[0].Length > MaxPayloadBytes)
                throw new InvalidDataException("The Facebook SDK ZIP contains an empty or oversized payload.");
            return payloads[0];
        }
    }
}
