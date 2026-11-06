using System.Collections.Generic;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class InstalledPackageState
    {
        public string Name;
        public string Version;
        public string ManifestValue;
        public string Source;
        public bool IsDirect;
        public bool IsRegistered = true;
        public readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        public readonly List<string> RequiredBy = new List<string>();
    }
}
