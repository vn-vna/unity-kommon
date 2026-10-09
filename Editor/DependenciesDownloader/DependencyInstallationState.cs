using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public enum DependencyInstallationPhase
    {
        None,
        ApplyingManifest,
        WaitingForPackageManager,
        ReadyToImport,
        WaitingForImport,
        FinalizingImport,
        WaitingForCompilation,
        RemovingAssets,
        Paused,
        Completed,
        Cancelled
    }

    [Serializable]
    public sealed class StagedSdkImport
    {
        public DownloadEntry Entry;
        public string ArchivePath;
        public string PackagePath;
        public bool AllowModifiedFiles;
        public bool ImportStarted;
        public bool SelectionReported;
        public List<string> CandidatePaths = new List<string>();
        public List<string> SelectedPaths = new List<string>();
        public UnityPackageInstallRecord PreviousRecord;
    }

    [Serializable]
    public sealed class StagedSdkRemoval
    {
        public string PackageName;
        public bool AllowModifiedFiles;
    }

    [Serializable]
    public sealed class ManifestGitAddition
    {
        public string PackageName;
        public string Url;
    }

    [Serializable]
    public sealed class DependencyInstallationState
    {
        public DependencyInstallationPhase Phase;
        public DependencyInstallationPhase ResumePhase;
        public DependencyInstallationPhase AfterCompilation;
        public List<DownloadEntry> Tarballs = new List<DownloadEntry>();
        public List<string> ManifestRemovals = new List<string>();
        public List<ManifestGitAddition> GitAdditions = new List<ManifestGitAddition>();
        public List<StagedSdkImport> Imports = new List<StagedSdkImport>();
        public List<StagedSdkRemoval> Removals = new List<StagedSdkRemoval>();
        public string TarballDirectory;
        public int ImportIndex;
        public int RemovalIndex;
        public bool ManifestApplied;
        public bool ResolutionRequested;
        public bool CancelAfterCurrent;
        public bool CompilationRequested;
        public bool CompilationObserved;
        public bool CompilationFinished;
        public bool AssemblyCompiled;
        public bool CompilationFailed;
        public bool ReloadPending;
        public int BarrierGeneration;
        public int IdleObservations;
        public long PhaseStartedUtcTicks;
        public string Status;

        public bool HasManifestChanges => Tarballs.Count > 0 || ManifestRemovals.Count > 0 || GitAdditions.Count > 0;
        public bool IsActive => Phase != DependencyInstallationPhase.None && Phase != DependencyInstallationPhase.Paused &&
            Phase != DependencyInstallationPhase.Completed && Phase != DependencyInstallationPhase.Cancelled;
        public bool HasPendingWork => IsActive || Phase == DependencyInstallationPhase.Paused;

        public static string SdkDownloadDirectory => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Temp", "DependenciesDownloader", "Downloads"
        ));

        public static string GetArchivePath(DownloadEntry entry, string tarballDirectory)
        {
            var sdk = entry.PackageName == "Adjust" || entry.PackageName == "AppLovin" || entry.PackageName == "Facebook";
            var fileName = sdk
                ? $"{entry.PackageName}_v{entry.Version}.{(entry.IsZipArchive ? "zip" : "unitypackage")}" 
                : $"{entry.PackageName}-{entry.Version}.tgz";
            return Path.Combine(sdk ? SdkDownloadDirectory : tarballDirectory, fileName);
        }
    }
}
