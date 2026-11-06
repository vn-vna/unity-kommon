using System;
using System.Collections.Generic;
using System.Linq;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class DependencyChangePlan
    {
        public readonly List<DownloadEntry> Downloads = new List<DownloadEntry>();
        public readonly List<string> Errors = new List<string>();
        public readonly Dictionary<string, DownloadEntry> ExistingDependencies = new Dictionary<string, DownloadEntry>();
    }

    public static class DependencyChangePlanner
    {
        public static DependencyChangePlan Build(
            GoogleArchiveCache archive,
            IDictionary<string, string> installs,
            ISet<string> removals,
            IDictionary<string, InstalledPackageState> installed,
            string unityVersion)
        {
            var plan = new DependencyChangePlan();
            var effectiveInstalled = installed.Where(pair =>
                    pair.Value.IsRegistered && !removals.Contains(pair.Key) && !installs.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Version);
            var resolved = new Dictionary<string, DownloadEntry>();

            foreach (var removal in removals.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!installed.TryGetValue(removal, out var state) || !state.IsDirect)
                {
                    plan.Errors.Add($"{removal} is not a direct dependency. Manage its parent packages in Package Manager.");
                    continue;
                }
                var parents = state.RequiredBy.Where(parent =>
                    !removals.Contains(parent) && !installs.ContainsKey(parent)).ToArray();
                if (parents.Length > 0)
                    plan.Errors.Add($"Cannot remove {removal}; it is required by {string.Join(", ", parents)}.");
            }

            foreach (var install in installs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                try
                {
                    var package = archive?.Packages?.Find(item => item.Name == install.Key);
                    var version = package?.Versions?.Find(item => item.Version == install.Value);
                    if (version != null && !string.IsNullOrEmpty(version.MinUnityVersion) &&
                        DependencyVersionUtility.TryCompare(unityVersion, version.MinUnityVersion, out var unityComparison) &&
                        unityComparison < 0)
                    {
                        plan.Errors.Add($"{install.Key} {install.Value} needs Unity {version.MinUnityVersion} or newer.");
                        continue;
                    }
                    var fullTree = GoogleDependencyResolver.ResolveFullDependencyTree(install.Key, install.Value, archive);
                    foreach (var dependency in fullTree.Where(entry => entry.IsTransitive))
                    {
                        if (effectiveInstalled.ContainsKey(dependency.PackageName))
                            plan.ExistingDependencies[dependency.PackageName] = dependency;
                        if (removals.Contains(dependency.PackageName))
                            plan.Errors.Add($"{install.Key} {install.Value} requires {dependency.PackageName}, which is queued for removal.");
                    }
                    var downloads = GoogleDependencyResolver.ResolveFullDependencyTree(
                        install.Key, install.Value, archive, effectiveInstalled
                    );
                    foreach (var entry in downloads)
                    {
                        if (resolved.TryGetValue(entry.PackageName, out var existing) && existing.Version != entry.Version)
                        {
                            if (entry.PackageName == "com.google.external-dependency-manager" && entry.IsTransitive && existing.IsTransitive &&
                                DependencyVersionUtility.TryCompare(existing.Version, entry.Version, out var comparison))
                            {
                                if (comparison >= 0) continue;
                                resolved[entry.PackageName] = entry;
                                continue;
                            }
                            plan.Errors.Add($"Dependency conflict: {entry.PackageName} is required as {existing.Version} and {entry.Version}. Change the queued versions.");
                            continue;
                        }
                        resolved[entry.PackageName] = entry;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    plan.Errors.Add(ex.Message);
                }
            }
            foreach (var entry in resolved.Values)
            {
                if (!installed.TryGetValue(entry.PackageName, out var previous)) continue;
                foreach (var parent in previous.RequiredBy.Where(name => !installs.ContainsKey(name) && !removals.Contains(name)))
                {
                    if (installed.TryGetValue(parent, out var parentState) &&
                        parentState.Dependencies.TryGetValue(entry.PackageName, out var required) &&
                        !DependencyVersionUtility.Satisfies(entry.PackageName, entry.Version, required))
                        plan.Errors.Add($"{parent} still requires {entry.PackageName} {required}. Queue a compatible {parent} update before changing it to {entry.Version}.");
                }
            }
            plan.Downloads.AddRange(resolved.Values.OrderBy(entry => entry.PackageName, StringComparer.Ordinal));
            return plan;
        }
    }
}
