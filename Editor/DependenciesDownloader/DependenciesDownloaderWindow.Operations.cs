using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed partial class DependenciesDownloaderWindow
    {
        private static int _activeOperationOwner;
        private bool _waitingForOperation;
        private string _currentOperationPackage = "Dependencies";

        private void ResetCancellation()
        {
            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
        }

        private string GetResolvedVersion(string packageId, string manifestValue)
        {
            return _installedStates.TryGetValue(packageId, out var state)
                ? state.Version : DependencyVersionUtility.Extract(manifestValue);
        }

        private bool HandleExternalOperation()
        {
            if (_activeOperationOwner != 0 && _activeOperationOwner != GetInstanceID())
            {
                _waitingForOperation = true;
                EditorGUILayout.HelpBox("The previous window is finishing a dependency operation. Its results will appear here when it finishes.", MessageType.Info);
                Repaint();
                return true;
            }
            if (_waitingForOperation)
            {
                _waitingForOperation = false;
                _queuedInstalls.Clear();
                _queuedRemovals.Clear();
                _queuedGitHubTargets.Clear();
                _packageVersions.Clear();
                RestoreSession();
                RefreshInstalledState();
                if (_session.VerificationPending) _ = ResumeVerificationAsync();
            }
            return false;
        }

        private bool ConfirmTrackedFileChanges()
        {
            _allowModifiedFiles.Clear();
            foreach (var pair in new[]
            {
                new KeyValuePair<string, GitHubAction>("Adjust", _adjustAction),
                new KeyValuePair<string, GitHubAction>("AppLovin", _applovinAction),
                new KeyValuePair<string, GitHubAction>("Facebook", _facebookAction)
            })
            {
                if (pair.Value == GitHubAction.None) continue;
                var record = UnityPackageTracker.GetInstallRecord(pair.Key);
                if (record == null) continue;
                var modified = UnityPackageTracker.GetModifiedFiles(record);
                if (modified.Count == 0 && record.HasFingerprints) continue;
                var description = modified.Count > 0
                    ? $"These {pair.Key} files have local modifications:\n" + string.Join("\n", modified)
                    : $"{pair.Key} has a legacy tracking record without file fingerprints. Review its tracked files; local modifications cannot be identified.";
                if (!EditorUtility.DisplayDialog("Review tracked SDK files", description +
                        "\n\nContinuing permits this queued update/removal to overwrite or delete tracked files.", "Apply tracked file changes", "Cancel"))
                    return false;
                _allowModifiedFiles.Add(pair.Key);
            }
            return true;
        }

        private List<PackageVerification> CreateVerificationPlan(string downloadPath)
        {
            var expectations = ResolveAllEntries().Select(entry => new PackageVerification
            {
                PackageName = entry.PackageName,
                Version = entry.Version,
                ManifestValue = "file:" + PackageManifestHelper.GetRelativeManifestPath(Path.Combine(downloadPath, GetDownloadFileName(entry)))
            }).ToList();
            expectations.AddRange(_queuedRemovals.Select(name => new PackageVerification { PackageName = name, Remove = true }));
            foreach (var pair in new[]
            {
                new KeyValuePair<string, GitHubAction>("Adjust", _adjustAction),
                new KeyValuePair<string, GitHubAction>("AppLovin", _applovinAction),
                new KeyValuePair<string, GitHubAction>("AppMetrica", _appMetricaAction),
                new KeyValuePair<string, GitHubAction>("Facebook", _facebookAction)
            })
            {
                if (pair.Value == GitHubAction.None) continue;
                var target = GetQueuedGitHubTarget(pair.Key);
                var record = UnityPackageTracker.GetInstallRecord(pair.Key);
                expectations.Add(new PackageVerification
                {
                    PackageName = pair.Key == "AppMetrica" ? AppMetricaPackageId : pair.Key,
                    Version = target?.Version,
                    ManifestValue = target?.GitUrl,
                    GitRevision = pair.Key == "AppMetrica" ? target?.TagName : null,
                    ReleaseTarget = target == null ? null : CloneRelease(target),
                    AssetsImport = pair.Key != "AppMetrica",
                    Remove = pair.Value == GitHubAction.Remove,
                    AssetPaths = record?.TrackedFiles?.Where(path =>
                        PackageManifestHelper.TryGetPathInsideProject(path, out var fullPath) &&
                        File.Exists(fullPath)).ToList() ?? new List<string>()
                });
            }
            return expectations;
        }

        private void RecordResult(string packageName, string message, bool succeeded)
        {
            var result = _session.Results.Find(item => item.PackageName == packageName);
            if (result == null)
            {
                result = new PackageOperationResult { PackageName = packageName };
                _session.Results.Add(result);
            }
            result.Message = message;
            result.Succeeded = succeeded;
            SaveSession();
        }

        private string VerifyAssetsImport(PackageVerification expectation)
        {
            var record = UnityPackageTracker.GetInstallRecord(expectation.PackageName);
            if (expectation.Remove)
            {
                if (record != null) return "The installation record still exists; removal is incomplete.";
                var remaining = expectation.AssetPaths.Where(path =>
                    PackageManifestHelper.TryGetPathInsideProject(path, out var fullPath) &&
                    (File.Exists(fullPath) || File.Exists(fullPath + ".meta"))).ToArray();
                return remaining.Length == 0 ? null : $"{remaining.Length} tracked files remain.";
            }
            if (record?.Version != expectation.Version) return "The installation record does not match the requested version.";
            var missing = UnityPackageTracker.GetMissingFiles(record);
            return missing.Count == 0 ? null : $"{missing.Count} tracked paths are missing. Queue an install to repair.";
        }

        private void RemoveVerifiedChange(PackageVerification expectation)
        {
            if (expectation.PackageName == "Adjust")
            {
                _adjustAction = GitHubAction.None;
                _queuedGitHubTargets.Remove("Adjust");
            }
            else if (expectation.PackageName == "AppLovin")
            {
                _applovinAction = GitHubAction.None;
                _queuedGitHubTargets.Remove("AppLovin");
            }
            else if (expectation.PackageName == AppMetricaPackageId)
            {
                _appMetricaAction = GitHubAction.None;
                _queuedGitHubTargets.Remove("AppMetrica");
            }
            else if (expectation.PackageName == "Facebook")
            {
                _facebookAction = GitHubAction.None;
                _queuedGitHubTargets.Remove("Facebook");
            }
            else
            {
                _queuedInstalls.Remove(expectation.PackageName);
                _queuedRemovals.Remove(expectation.PackageName);
                _packageVersions.Remove(expectation.PackageName);
            }
        }

        private void RestoreFailedVerificationChanges()
        {
            foreach (var expectation in _session.Verification.Where(item => item.Applied))
            {
                if (_session.Results.Any(result => result.PackageName == expectation.PackageName && result.Succeeded)) continue;
                if (expectation.ReleaseTarget != null)
                    _queuedGitHubTargets[expectation.PackageName == AppMetricaPackageId ? "AppMetrica" : expectation.PackageName] = expectation.ReleaseTarget;
                if (expectation.PackageName == "Adjust") _adjustAction = expectation.Remove ? GitHubAction.Remove : GitHubAction.Install;
                else if (expectation.PackageName == "AppLovin") _applovinAction = expectation.Remove ? GitHubAction.Remove : GitHubAction.Install;
                else if (expectation.PackageName == AppMetricaPackageId) _appMetricaAction = expectation.Remove ? GitHubAction.Remove : GitHubAction.Install;
                else if (expectation.PackageName == "Facebook") _facebookAction = expectation.Remove ? GitHubAction.Remove : GitHubAction.Install;
                else if (expectation.Remove) _queuedRemovals.Add(expectation.PackageName);
                else
                {
                    _queuedInstalls.Add(expectation.PackageName);
                    _packageVersions[expectation.PackageName] = expectation.Version;
                }
            }
            QueueChanged();
        }

        private async Task ResumeVerificationAsync()
        {
            if (DependencyInstallCoordinator.HasPendingWork) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
            {
                SetStatus("Wait for script compilation to finish successfully before verifying the installed state.", MessageType.Warning);
                return;
            }
            if (_isCommitting || !_session.VerificationPending || _activeOperationOwner != 0) return;
            _isCommitting = true;
            _activeOperationOwner = GetInstanceID();
            ResetCancellation();
            _canCancel = true;
            var applied = _session.Verification.Where(item => item.Applied).ToList();
            try
            {
                SetStatus("Verifying registered packages and tracked SDK files...", MessageType.Info);
                var timeout = DateTime.UtcNow.AddSeconds(90);
                var registered = new Dictionary<string, RegisteredDependency>();
                if (applied.Any(item => !item.AssetsImport))
                    Client.Resolve();
                bool allVerified;
                do
                {
                    _operationCancellation.Token.ThrowIfCancellationRequested();
                    if (applied.Any(item => !item.AssetsImport))
                    {
                        var request = Client.List(offlineMode: true, includeIndirectDependencies: true);
                        while (!request.IsCompleted)
                        {
                            if (DateTime.UtcNow >= timeout) throw new TimeoutException("UPM verification timed out. Reopen the window or verify installed state again.");
                            await Task.Delay(100);
                        }
                        _operationCancellation.Token.ThrowIfCancellationRequested();
                        if (request.Status != StatusCode.Success)
                            throw new InvalidOperationException("UPM could not list resolved packages: " + request.Error?.message);
                        registered = request.Result.ToDictionary(package => package.name, package => new RegisteredDependency
                        {
                            Version = package.version,
                            GitRevision = package.git?.revision,
                            Error = package.errors == null ? string.Empty : string.Join("; ", package.errors.Select(error => error.message))
                        });
                    }

                    allVerified = true;
                    foreach (var expectation in applied)
                    {
                        var failure = expectation.AssetsImport ? VerifyAssetsImport(expectation)
                            : DependencyInstallationVerifier.GetFailure(expectation, registered,
                                PackageManifestHelper.GetInstalledPackageVersion(expectation.PackageName));
                        RecordResult(expectation.PackageName,
                            failure ?? (expectation.Remove ? "Removal verified." : $"Installed {expectation.Version}; verified."), failure == null);
                        if (failure == null) RemoveVerifiedChange(expectation);
                        else allVerified = false;
                    }
                    SaveSession();
                    if (!allVerified && DateTime.UtcNow < timeout)
                        await Task.Delay(1000, _operationCancellation.Token);
                } while (!allVerified && DateTime.UtcNow < timeout);

                _session.VerificationPending = !allVerified;
                _session.Interrupted = false;
                foreach (var warning in _commitWarnings) RecordResult("Cleanup", warning, false);
                RefreshInstalledState();
                SetStatus(!allVerified ? "Some changes are still unverified. Review the results, then verify again or retry."
                    : QueuedChangeCount > 0 ? "Completed changes were verified. Failed or unapplied changes remain queued for retry."
                    : _commitWarnings.Count > 0 ? "Changes verified with cleanup warnings. See the results."
                    : "All changes completed and installed state verified.",
                    !allVerified || QueuedChangeCount > 0 || _commitWarnings.Count > 0 ? MessageType.Warning : MessageType.Info);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Verification paused. Applied changes and remaining queue are preserved; verify again when ready.", MessageType.Warning);
            }
            catch (Exception ex)
            {
                RecordResult("Verification", ex.Message, false);
                SetStatus(ex.Message, MessageType.Error);
            }
            finally
            {
                _canCancel = false;
                _isCommitting = false;
                _activeOperationOwner = 0;
                SaveSession();
                if (this != null) Repaint();
            }
        }

        private void SynchronizeInstallation()
        {
            var installation = DependencyInstallCoordinator.State;
            if (installation == null || _isCommitting || _activeOperationOwner != 0) return;
            var changed = _observedInstallationRevision != DependencyInstallCoordinator.Revision;
            if (changed)
            {
                RestoreSession();
                _observedInstallationRevision = DependencyInstallCoordinator.Revision;
                RefreshInstalledState(false);
            }
            if (changed || installation.HasPendingWork)
            {
                _statusMessage = installation.Status ?? "Dependency installation sequence ready.";
                _statusType = installation.Phase == DependencyInstallationPhase.Paused ? MessageType.Warning : MessageType.Info;
            }
        }
    }
}
