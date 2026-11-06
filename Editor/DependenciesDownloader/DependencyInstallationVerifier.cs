using System.Collections.Generic;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class RegisteredDependency
    {
        public string Version;
        public string GitRevision;
        public string Error;
    }

    public static class DependencyInstallationVerifier
    {
        public static string GetFailure(
            PackageVerification expected,
            IDictionary<string, RegisteredDependency> registered,
            string currentManifestValue)
        {
            if (expected.Remove)
                return registered.ContainsKey(expected.PackageName)
                    ? "Still registered in UPM; another dependency may require it."
                    : string.IsNullOrEmpty(currentManifestValue) ? null : "The manifest still contains this package.";

            if (currentManifestValue != expected.ManifestValue)
                return "The manifest does not match the queued target. Review external package changes.";
            if (!registered.TryGetValue(expected.PackageName, out var actual))
                return "Not registered in UPM yet.";
            if (!string.IsNullOrEmpty(actual.Error)) return actual.Error;
            if (!DependencyVersionUtility.TryCompare(actual.Version, expected.Version, out var comparison) || comparison != 0)
                return $"Expected {expected.Version}, but UPM resolved {actual.Version}.";
            if (!string.IsNullOrEmpty(expected.GitRevision) && actual.GitRevision != expected.GitRevision)
                return $"Expected Git revision {expected.GitRevision}, but UPM resolved {actual.GitRevision}.";
            return null;
        }
    }
}
