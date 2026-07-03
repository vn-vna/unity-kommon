using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
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

            var rootKey = MakeKey(packageName, version);
            CollectDependencies(packageName, version, archive, false, resolved, visited, alreadyInstalled);

            var entries = new List<DownloadEntry>();
            foreach (var kv in resolved)
            {
                if (alreadyInstalled != null && alreadyInstalled.ContainsKey(kv.Value.PackageName))
                {
                    continue;
                }

                entries.Add(kv.Value);
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

            if (!visited.Add(key))
            {
                return;
            }

            var packageInfo = FindPackage(archive, packageName);
            if (packageInfo == null)
            {
                return;
            }

            var versionInfo = FindVersion(packageInfo, version);
            if (versionInfo == null)
            {
                return;
            }

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
                if (string.IsNullOrEmpty(dep.Name) || string.IsNullOrEmpty(dep.TarballUrl))
                {
                    continue;
                }

                if (alreadyInstalled != null && alreadyInstalled.ContainsKey(dep.Name))
                {
                    continue;
                }

                var depVersion = dep.Version;
                if (string.IsNullOrEmpty(depVersion))
                {
                    depVersion = "0.0.0";
                }

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

            return package.Versions.Count > 0 ? package.Versions[0] : null;
        }

        private static string MakeKey(string name, string version)
        {
            return $"{name}@{version}";
        }
    }
}
