using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    [Serializable]
    public sealed class QueuedGoogleChange
    {
        public string PackageName;
        public string Version;
        public bool Remove;
    }

    [Serializable]
    public sealed class PackageVerification
    {
        public string PackageName;
        public string Version;
        public string ManifestValue;
        public bool Remove;
        public bool AssetsImport;
        public bool Applied;
        public string GitRevision;
        public List<string> AssetPaths = new List<string>();
        public GitHubReleaseInfo ReleaseTarget;
    }

    [Serializable]
    public sealed class PackageOperationResult
    {
        public string PackageName;
        public string Message;
        public bool Succeeded;
    }

    [Serializable]
    public sealed class DownloaderSessionState
    {
        public List<QueuedGoogleChange> GoogleChanges = new List<QueuedGoogleChange>();
        public int AdjustAction;
        public int AppLovinAction;
        public int AppMetricaAction;
        public int FacebookAction;
        public GitHubReleaseInfo AdjustTarget;
        public GitHubReleaseInfo AppLovinTarget;
        public GitHubReleaseInfo AppMetricaTarget;
        public GitHubReleaseInfo FacebookTarget;
        public List<PackageVerification> Verification = new List<PackageVerification>();
        public List<PackageOperationResult> Results = new List<PackageOperationResult>();
        public bool VerificationPending;
        public bool Interrupted;
        public int Revision;
        public DependencyInstallationState Installation;

        private const string Key = "Scheherazade.DependenciesDownloader.Session";

        public static DownloaderSessionState Load()
        {
            try
            {
                var json = SessionState.GetString(Key, string.Empty);
                var state = string.IsNullOrEmpty(json)
                    ? new DownloaderSessionState()
                    : JsonUtility.FromJson<DownloaderSessionState>(json) ?? new DownloaderSessionState();
                // JsonUtility can deserialize a serialized null nested object as an
                // empty instance. Do not resurrect discarded or legacy SDK targets.
                state.AdjustTarget = NormalizeTarget(state.AdjustTarget);
                state.AppLovinTarget = NormalizeTarget(state.AppLovinTarget);
                state.AppMetricaTarget = NormalizeTarget(state.AppMetricaTarget);
                state.FacebookTarget = NormalizeTarget(state.FacebookTarget);
                state.GoogleChanges ??= new List<QueuedGoogleChange>();
                state.Verification ??= new List<PackageVerification>();
                state.Results ??= new List<PackageOperationResult>();
                foreach (var verification in state.Verification)
                    verification.ReleaseTarget = NormalizeTarget(verification.ReleaseTarget);
                if (state.Installation != null)
                {
                    state.Installation.Tarballs ??= new List<DownloadEntry>();
                    state.Installation.ManifestRemovals ??= new List<string>();
                    state.Installation.GitAdditions ??= new List<ManifestGitAddition>();
                    state.Installation.Imports ??= new List<StagedSdkImport>();
                    state.Installation.Removals ??= new List<StagedSdkRemoval>();
                    foreach (var import in state.Installation.Imports)
                    {
                        import.CandidatePaths ??= new List<string>();
                        import.SelectedPaths ??= new List<string>();
                        if (string.IsNullOrEmpty(import.PreviousRecord?.PackageName)) import.PreviousRecord = null;
                    }
                }
                return state;
            }
            catch (ArgumentException)
            {
                return new DownloaderSessionState();
            }
        }

        public void Save()
        {
            SessionState.SetString(Key, JsonUtility.ToJson(this));
        }

        public void RecordResult(string packageName, string message, bool succeeded)
        {
            var result = Results.Find(item => item.PackageName == packageName);
            if (result == null)
            {
                result = new PackageOperationResult { PackageName = packageName };
                Results.Add(result);
            }
            result.Message = message;
            result.Succeeded = succeeded;
        }

        public void RemoveQueuedChange(PackageVerification expected)
        {
            switch (expected.PackageName)
            {
                case "Adjust": AdjustAction = 0; AdjustTarget = null; break;
                case "AppLovin": AppLovinAction = 0; AppLovinTarget = null; break;
                case "Facebook": FacebookAction = 0; FacebookTarget = null; break;
                case "io.appmetrica.analytics": AppMetricaAction = 0; AppMetricaTarget = null; break;
                default: GoogleChanges.RemoveAll(change => change.PackageName == expected.PackageName); break;
            }
        }

        private static GitHubReleaseInfo NormalizeTarget(GitHubReleaseInfo target)
        {
            return target == null || string.IsNullOrEmpty(target.Version) || string.IsNullOrEmpty(target.TagName)
                ? null : target;
        }
    }
}
