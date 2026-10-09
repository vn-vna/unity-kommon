using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    [InitializeOnLoad]
    public static class DependencyInstallCoordinator
    {
        private const string GenerationKey = "Scheherazade.DependenciesDownloader.ReloadGeneration";
        private static readonly int Generation;
        private static DownloaderSessionState _session;
        private static DependencyInstallationController _controller;
        private static double _nextUpdate;

        static DependencyInstallCoordinator()
        {
            Generation = SessionState.GetInt(GenerationKey, 0) + 1;
            SessionState.SetInt(GenerationKey, Generation);
            _session = DownloaderSessionState.Load();
            if (_session.Installation?.HasPendingWork == true) AttachController();
            EditorApplication.update += Update;
            AssetDatabase.importPackageStarted += name => _controller?.ImportStarted(name);
            AssetDatabase.onImportPackageItemsCompleted += paths => _controller?.ImportItemsCompleted(paths);
            AssetDatabase.importPackageCompleted += name => _controller?.ImportCompleted(name);
            AssetDatabase.importPackageCancelled += name => _controller?.ImportFailed(name, "Import cancelled. Resume to reopen this package, or stop the remaining sequence.");
            AssetDatabase.importPackageFailed += (name, error) => _controller?.ImportFailed(name, error);
            CompilationPipeline.compilationStarted += context => _controller?.CompilationStarted();
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
                _controller?.AssemblyCompilationFinished(messages.Any(message => message.type == CompilerMessageType.Error));
            CompilationPipeline.compilationFinished += context => _controller?.CompilationFinished();
            AssemblyReloadEvents.beforeAssemblyReload += () => _controller?.BeforeAssemblyReload();
        }

        public static DependencyInstallationState State => _controller == null ? null : _session.Installation;
        public static bool IsActive => State?.IsActive == true;
        public static bool HasPendingWork => State?.HasPendingWork == true;
        public static int Revision => _controller == null ? 0 : _session.Revision;

        public static void Start(DownloaderSessionState session, DependencyInstallationState installation)
        {
            if (HasPendingWork) throw new InvalidOperationException("Another dependency installation sequence is still pending.");
            _session = session;
            _session.Installation = installation;
            _session.Interrupted = true;
            installation.Phase = installation.HasManifestChanges ? DependencyInstallationPhase.ApplyingManifest : DependencyInstallationPhase.ReadyToImport;
            installation.Status = installation.HasManifestChanges ? "Applying tarballs and Git dependencies first..." : "Opening staged SDK packages one at a time...";
            installation.PhaseStartedUtcTicks = DateTime.UtcNow.Ticks;
            _session.Revision++;
            _session.Save();
            AttachController();
        }

        public static void Cancel() => _controller?.Cancel();
        public static void Resume() => _controller?.Resume();

        private static void AttachController()
        {
            _controller = new DependencyInstallationController(_session, new UnityInstallationHost(Generation), () => _session.Save());
        }

        private static void Update()
        {
            if (!IsActive || EditorApplication.timeSinceStartup < _nextUpdate) return;
            _nextUpdate = EditorApplication.timeSinceStartup + 0.1;
            _controller.Tick();
        }

        private sealed class UnityInstallationHost : IDependencyInstallationHost
        {
            private ListRequest _listRequest;
            public bool IsBusy => EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode;
            public int Generation { get; }

            public UnityInstallationHost(int generation) { Generation = generation; }

            public bool ManifestMatches(IEnumerable<PackageVerification> expectations)
            {
                return expectations.All(expected => GetManifestValue(expected.PackageName) ==
                    (expected.Remove ? string.Empty : expected.ManifestValue));
            }

            public bool ApplyManifest(DependencyInstallationState state)
            {
                return PackageManifestHelper.ApplyTarballChanges(state.Tarballs, new HashSet<string>(state.ManifestRemovals),
                    state.TarballDirectory, state.GitAdditions.ToDictionary(entry => entry.PackageName, entry => entry.Url));
            }

            public void RefreshPackageManager()
            {
                _listRequest = null;
                Client.Resolve();
                AssetDatabase.Refresh();
            }

            public PackageManagerInstallationSnapshot PollPackageManager()
            {
                if (_listRequest == null) _listRequest = Client.List(offlineMode: true, includeIndirectDependencies: true);
                if (!_listRequest.IsCompleted) return new PackageManagerInstallationSnapshot();
                if (_listRequest.Status != StatusCode.Success)
                    return new PackageManagerInstallationSnapshot { Ready = true, Error = "UPM resolution could not be inspected: " + _listRequest.Error?.message };
                // Registered packages are the live result; a completed List request
                // establishes the UPM barrier without repeating resolution/refresh.
                return new PackageManagerInstallationSnapshot
                {
                    Ready = true,
                    Packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().ToDictionary(
                        package => package.name,
                        package => new RegisteredDependency
                        {
                            Version = package.version,
                            GitRevision = package.git?.revision,
                            Error = package.errors == null ? string.Empty : string.Join("; ", package.errors.Select(error => error.message))
                        })
                };
            }

            public string GetManifestValue(string packageName) => PackageManifestHelper.GetInstalledPackageVersion(packageName);

            public void PrepareImport(StagedSdkImport import)
            {
                if (EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Resolve the current Console compilation errors before opening the next SDK package.");
                var sdkDirectory = DependencyInstallationState.SdkDownloadDirectory.Replace('\\', '/');
                var archivePath = Path.GetFullPath(import.ArchivePath).Replace('\\', '/');
                if (!archivePath.StartsWith(sdkDirectory + "/", StringComparison.OrdinalIgnoreCase) || !File.Exists(archivePath))
                    throw new InvalidOperationException("The staged SDK archive is missing from Temp. Stop this sequence and apply the remaining queue to download it again.");
                var installed = PackageManifestHelper.GetInstalledPackageStates();
                var upmId = import.Entry.PackageName == "Adjust" ? "com.adjust.sdk"
                    : import.Entry.PackageName == "AppLovin" ? "com.applovin.mediation.ads" : null;
                if (upmId != null && installed.ContainsKey(upmId))
                    throw new InvalidOperationException(import.Entry.PackageName + " is now UPM-managed. Stop this SDK import and use Package Manager.");
                import.PackagePath = import.ArchivePath;
                if (import.Entry.IsZipArchive)
                {
                    installed.TryGetValue(FacebookSdkArchive.DependencyManagerPackageId, out var resolver);
                    var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                        .FirstOrDefault(package => package.name == FacebookSdkArchive.DependencyManagerPackageId);
                    if (resolver != null && registered == null)
                        throw new InvalidOperationException("EDM is declared in UPM but is not registered. Resolve it before opening Facebook's import dialog.");
                    import.PackagePath = FacebookSdkArchive.PrepareForImport(import.ArchivePath, import.Entry.Version,
                        resolver != null, registered?.version);
                }
                import.CandidatePaths = UnityPackageTracker.EnumerateFilesInPackage(import.PackagePath);
                import.PreviousRecord = UnityPackageTracker.GetInstallRecord(import.Entry.PackageName);
            }

            public void OpenImportDialog(string packagePath) => AssetDatabase.ImportPackage(packagePath, interactive: true);

            public string FinalizeImport(StagedSdkImport import)
            {
                var paths = new HashSet<string>(import.SelectedPaths.Where(Exists), StringComparer.Ordinal);
                if (paths.Count == 0) return "Unity's selected SDK assets could not be found after import.";
                var old = import.PreviousRecord;
                var cleanupSucceeded = true;
                if (old?.TrackedFiles != null)
                {
                    cleanupSucceeded = UnityPackageTracker.RemoveTrackedFiles(import.Entry.PackageName,
                        import.CandidatePaths, import.AllowModifiedFiles, refreshAssets: false);
                    foreach (var path in old.TrackedFiles.Where(Exists))
                    {
                        if (import.CandidatePaths.Contains(path) || !cleanupSucceeded) paths.Add(path);
                    }
                }
                if (!UnityPackageTracker.SaveInstallRecord(import.Entry.PackageName, import.Entry.Version, paths.ToList()))
                    return "SDK assets imported, but the tracking record could not be saved. Inspect the files before resuming.";
                if (old?.TrackedFiles.Any(path => !import.CandidatePaths.Contains(path)) == true) AssetDatabase.Refresh();
                if (!cleanupSucceeded)
                    _session.RecordResult(import.Entry.PackageName + " cleanup", "Some obsolete files could not be removed; they remain tracked.", false);
                return null;
            }

            public string RemoveAssets(StagedSdkRemoval removal)
            {
                if (!UnityPackageTracker.RemoveTrackedFiles(removal.PackageName, allowModifiedFiles: removal.AllowModifiedFiles, refreshAssets: false))
                    return removal.PackageName + " removal failed; review its tracked paths before resuming.";
                if (UnityPackageTracker.GetInstallRecord(removal.PackageName) != null)
                    return removal.PackageName + " tracking record still exists after removal.";
                AssetDatabase.Refresh();
                return null;
            }

            public void RequestCompilation() => CompilationPipeline.RequestScriptCompilation();

            private static bool Exists(string path)
            {
                return PackageManifestHelper.TryGetPathInsideProject(path, out var fullPath) &&
                    (File.Exists(fullPath) || Directory.Exists(fullPath));
            }
        }
    }
}
