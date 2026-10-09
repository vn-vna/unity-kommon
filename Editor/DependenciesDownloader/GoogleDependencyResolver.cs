using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public static class GoogleDependencyResolver
    {
        public static List<DownloadEntry> ResolveFullDependencyTree(
            string packageName,
            string version,
            GoogleArchiveCache archive,
            Dictionary<string, string> alreadyInstalled = null)
        {
            var resolved = new Dictionary<string, DownloadEntry>();
            var visited = new HashSet<string>();

            CollectDependencies(
                packageName,
                version,
                archive,
                false,
                resolved,
                visited,
                alreadyInstalled
            );

            var entries = new List<DownloadEntry>();
            foreach (var entry in resolved.Values)
            {
                entries.Add(entry);
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.PackageName, b.PackageName));

            return entries;
        }

        private static void CollectDependencies(
            string packageName,
            string version,
            GoogleArchiveCache archive,
            bool isTransitive,
            Dictionary<string, DownloadEntry> resolved,
            HashSet<string> visited,
            Dictionary<string, string> alreadyInstalled)
        {
            var key = MakeKey(packageName, version);

            if (resolved.TryGetValue(packageName, out var previous) && previous.Version != version)
                throw new InvalidOperationException($"Dependency conflict: {packageName} requires both {previous.Version} and {version}.");

            if (!visited.Add(key))
            {
                return;
            }

            var packageInfo = FindPackage(archive, packageName);
            if (packageInfo == null && !isTransitive)
                throw new InvalidOperationException($"{packageName} is missing from the registry. Refresh the registry.");
            if (packageInfo == null) return;

            var versionInfo = FindVersion(packageInfo, version);
            if (versionInfo == null && !isTransitive)
                throw new InvalidOperationException($"{packageName} {version} is unavailable. Choose another version.");
            if (versionInfo == null) return;

            if (!resolved.ContainsKey(packageName))
            {
                resolved[packageName] = new DownloadEntry
                {
                    PackageName = packageName,
                    Version = version,
                    TarballUrl = versionInfo.TarballUrl,
                    IsTransitive = isTransitive
                };
            }

            if (versionInfo.Dependencies == null || versionInfo.Dependencies.Count == 0)
            {
                return;
            }

            foreach (var dep in versionInfo.Dependencies)
            {
                if (string.IsNullOrEmpty(dep.Name))
                {
                    continue;
                }

                var depVersion = dep.Version;
                if (string.IsNullOrEmpty(depVersion))
                {
                    throw new InvalidOperationException($"{packageName} requires {dep.Name}, but its required version is unknown.");
                }

                if (alreadyInstalled != null &&
                    alreadyInstalled.ContainsKey(dep.Name))
                {
                    if (!DependencyVersionUtility.Satisfies(dep.Name, alreadyInstalled[dep.Name], depVersion))
                        throw new InvalidOperationException(
                            $"{packageName} {version} requires {dep.Name} {depVersion}, but " +
                            $"{DependencyVersionUtility.Extract(alreadyInstalled[dep.Name])} is installed. " +
                            "Queue a compatible update or manage this dependency in Package Manager."
                        );
                    continue;
                }

                if (resolved.TryGetValue(dep.Name, out var existing) && existing.Version != depVersion)
                {
                    if (dep.Name == "com.google.external-dependency-manager" && existing.IsTransitive &&
                        DependencyVersionUtility.TryCompare(existing.Version, depVersion, out var comparison))
                    {
                        if (comparison >= 0) continue;
                        existing.Version = depVersion;
                        existing.TarballUrl = dep.TarballUrl;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Dependency conflict: {packageName} {version} requires {dep.Name} {depVersion}, " +
                            $"but the same dependency tree also requires {existing.Version}."
                        );
                    }
                }
                if (string.IsNullOrEmpty(dep.TarballUrl))
                    throw new InvalidOperationException($"No download URL is available for {dep.Name} {depVersion}.");

                var depKey = MakeKey(dep.Name, depVersion);
                if (visited.Contains(depKey))
                {
                    continue;
                }

                if (!resolved.ContainsKey(dep.Name))
                {
                    resolved[dep.Name] = new DownloadEntry
                    {
                        PackageName = dep.Name,
                        Version = depVersion,
                        TarballUrl = dep.TarballUrl,
                        IsTransitive = true
                    };
                }

                CollectDependencies(dep.Name, depVersion, archive, true, resolved, visited, alreadyInstalled);
            }
        }

        private static GooglePackageInfo FindPackage(GoogleArchiveCache archive, string packageName)
        {
            if (archive?.Packages == null)
            {
                return null;
            }

            foreach (var pkg in archive.Packages)
            {
                if (pkg.Name == packageName)
                {
                    return pkg;
                }
            }

            return null;
        }

        private static GooglePackageVersion FindVersion(GooglePackageInfo package, string version)
        {
            if (package?.Versions == null)
            {
                return null;
            }

            foreach (var v in package.Versions)
            {
                if (v.Version == version)
                {
                    return v;
                }
            }

            return null;
        }

        private static string MakeKey(string name, string version)
        {
            return $"{name}@{version}";
        }
    }
}
