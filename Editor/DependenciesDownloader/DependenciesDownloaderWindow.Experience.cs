using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed partial class DependenciesDownloaderWindow
    {
        private enum PackageView { All, Installed, UpdatesAvailable, Pending }
        [SerializeField] private PackageView _packageView;
        [SerializeField] private float _paneFraction = 0.6f;
        [SerializeField] private bool _showSettings;
        private bool _resizingPane;
        private bool _showResults = true;
        private CancellationTokenSource _operationCancellation;
        private DownloaderSessionState _session;
        private float _operationProgress;
        private bool _canCancel;
        private int _observedInstallationRevision;
        private readonly Dictionary<string, GitHubReleaseInfo> _queuedGitHubTargets = new Dictionary<string, GitHubReleaseInfo>();
        private Dictionary<string, InstalledPackageState> _installedStates = new Dictionary<string, InstalledPackageState>();
        private string _installedStateError;
        private readonly Dictionary<string, string> _assetHealth = new Dictionary<string, string>();
        private readonly HashSet<string> _expandedFiles = new HashSet<string>();
        private readonly HashSet<string> _allowModifiedFiles = new HashSet<string>();

        private string ProjectDownloadPathKey => DownloadPathKey + "." + Application.dataPath;
        private int QueuedChangeCount => _queuedInstalls.Count + _queuedRemovals.Count +
            (_adjustAction == GitHubAction.None ? 0 : 1) +
            (_applovinAction == GitHubAction.None ? 0 : 1) +
            (_appMetricaAction == GitHubAction.None ? 0 : 1) +
            (_facebookAction == GitHubAction.None ? 0 : 1);
        private bool RequiresDownloadFolder => _queuedInstalls.Count > 0;

        private void RefreshInstalledState(bool refreshHealth = true)
        {
            try
            {
                _installedStates = PackageManifestHelper.GetInstalledPackageStates();
                foreach (var state in _installedStates.Values) state.IsRegistered = false;
                foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (_installedStates.TryGetValue(package.name, out var state))
                    {
                        state.Version = package.version;
                        state.Source = package.source.ToString();
                        state.IsRegistered = true;
                    }
                }
                _installedCache = _installedStates.Where(pair => pair.Key.StartsWith("com.google.", StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Version);
                _installedCacheTime = EditorApplication.timeSinceStartup;
                _installedStateError = string.Empty;
                RefreshGitHubInstalledVersions();
                if (!refreshHealth) return;
                _assetHealth.Clear();
                foreach (var name in new[] { "Adjust", "AppLovin", "Facebook" })
                {
                    var record = UnityPackageTracker.GetInstallRecord(name);
                    if (record == null) continue;
                    var missing = UnityPackageTracker.GetMissingFiles(record);
                    var modified = UnityPackageTracker.GetModifiedFiles(record);
                    _assetHealth[name] = missing.Count > 0 ? $"Incomplete installation: {missing.Count} tracked paths are missing. Queue an install to repair."
                        : modified.Count > 0 ? $"{modified.Count} tracked files have local modifications."
                        : !record.HasFingerprints ? "Legacy installation record: local modifications cannot be identified until the next installation."
                        : "Tracked files verified.";
                }
            }
            catch (Exception ex)
            {
                _installedStateError = ex.Message;
                _installedCacheTime = EditorApplication.timeSinceStartup;
            }
            Repaint();
        }

        private void RestoreSession()
        {
            _queuedInstalls.Clear();
            _queuedRemovals.Clear();
            _packageVersions.Clear();
            _queuedGitHubTargets.Clear();
            _session = DownloaderSessionState.Load();
            foreach (var change in _session.GoogleChanges)
            {
                if (change.Remove) _queuedRemovals.Add(change.PackageName);
                else
                {
                    _queuedInstalls.Add(change.PackageName);
                    _packageVersions[change.PackageName] = change.Version;
                }
            }
            _adjustAction = (GitHubAction)_session.AdjustAction;
            _applovinAction = (GitHubAction)_session.AppLovinAction;
            _appMetricaAction = (GitHubAction)_session.AppMetricaAction;
            _facebookAction = (GitHubAction)_session.FacebookAction;
            if (_session.AdjustTarget != null) _queuedGitHubTargets["Adjust"] = _session.AdjustTarget;
            if (_session.AppLovinTarget != null) _queuedGitHubTargets["AppLovin"] = _session.AppLovinTarget;
            if (_session.AppMetricaTarget != null) _queuedGitHubTargets["AppMetrica"] = _session.AppMetricaTarget;
            if (_session.FacebookTarget != null) _queuedGitHubTargets["Facebook"] = _session.FacebookTarget;
            _adjustVersionIdx = FindReleaseIndex(_adjustCache, _session.AdjustTarget);
            _applovinVersionIdx = FindReleaseIndex(_applovinCache, _session.AppLovinTarget);
            _appMetricaVersionIdx = FindReleaseIndex(_appMetricaCache, _session.AppMetricaTarget);
            _facebookVersionIdx = FindReleaseIndex(_facebookCache, _session.FacebookTarget);
            if (_session.Interrupted && !_session.VerificationPending && !DependencyInstallCoordinator.HasPendingWork)
                SetStatus("The previous operation was interrupted. Review the results and retry the pending changes.", MessageType.Warning);
        }

        private void SaveSession()
        {
            if (DependencyInstallCoordinator.HasPendingWork) return;
            if (!_isCommitting && _activeOperationOwner == 0) SynchronizeInstallation();
            if (_session == null || (_activeOperationOwner != 0 && _activeOperationOwner != GetInstanceID())) return;
            _session.GoogleChanges = _queuedInstalls.Select(name => new QueuedGoogleChange
            {
                PackageName = name,
                Version = _packageVersions.TryGetValue(name, out var version) ? version : "?"
            }).Concat(_queuedRemovals.Select(name => new QueuedGoogleChange { PackageName = name, Remove = true })).ToList();
            _session.AdjustAction = (int)_adjustAction;
            _session.AppLovinAction = (int)_applovinAction;
            _session.AppMetricaAction = (int)_appMetricaAction;
            _session.FacebookAction = (int)_facebookAction;
            _session.AdjustTarget = GetQueuedGitHubTarget("Adjust");
            _session.AppLovinTarget = GetQueuedGitHubTarget("AppLovin");
            _session.AppMetricaTarget = GetQueuedGitHubTarget("AppMetrica");
            _session.FacebookTarget = GetQueuedGitHubTarget("Facebook");
            _session.Save();
        }

        private void QueueChanged()
        {
            _dependencyConflictMessage = string.Empty;
            _installedDependencies.Clear();
            SaveSession();
            Repaint();
        }

        private string GetSelectedGoogleVersion(GooglePackageInfo package, string installedVersion)
        {
            if (_packageVersions.TryGetValue(package.Name, out var selected)) return selected;
            return !string.IsNullOrEmpty(installedVersion) ? installedVersion : package.Versions?.FirstOrDefault()?.Version ?? string.Empty;
        }

        private void QueueGoogleInstall(string name, string version, bool forceInstall = false)
        {
            _queuedRemovals.Remove(name);
            _packageVersions[name] = version;
            if (!forceInstall && InstalledPackages.TryGetValue(name, out var installed) && DependencyVersionUtility.Extract(installed) == version)
                _queuedInstalls.Remove(name);
            else
                _queuedInstalls.Add(name);
            QueueChanged();
        }

        private void UndoGoogleChange(string name)
        {
            _queuedInstalls.Remove(name);
            _queuedRemovals.Remove(name);
            _packageVersions.Remove(name);
            QueueChanged();
        }

        private static GitHubReleaseInfo CloneRelease(GitHubReleaseInfo release)
        {
            return JsonUtility.FromJson<GitHubReleaseInfo>(JsonUtility.ToJson(release));
        }

        private GitHubReleaseInfo GetQueuedGitHubTarget(string name)
        {
            return _queuedGitHubTargets.TryGetValue(name, out var release) ? release : null;
        }

        private static int FindReleaseIndex(GitHubReleaseCache cache, GitHubReleaseInfo target)
        {
            var index = target == null ? -1 : cache?.Releases?.FindIndex(release => release.TagName == target.TagName) ?? -1;
            return Math.Max(0, index);
        }

        private void ValidateGitHubQueue()
        {
            foreach (var pair in new[]
            {
                new KeyValuePair<string, GitHubAction>("Adjust", _adjustAction),
                new KeyValuePair<string, GitHubAction>("AppLovin", _applovinAction),
                new KeyValuePair<string, GitHubAction>("AppMetrica", _appMetricaAction),
                new KeyValuePair<string, GitHubAction>("Facebook", _facebookAction)
            })
            {
                if (pair.Value == GitHubAction.None) continue;
                var managedByUpm = pair.Key == "Adjust" ? _adjustManagedByUpm : pair.Key == "AppLovin" && _applovinManagedByUpm;
                if (managedByUpm)
                    _dependencyConflictMessage += $"\n{pair.Key} is now UPM-managed. Undo this change and use Package Manager.";
                if (pair.Value != GitHubAction.Install) continue;
                var target = GetQueuedGitHubTarget(pair.Key);
                var url = pair.Key == "AppMetrica" ? target?.GitUrl : target?.DownloadUrl;
                if (target == null || string.IsNullOrWhiteSpace(url))
                    _dependencyConflictMessage += $"\n{pair.Key} has no pinned download target. Undo and queue a release again.";
            }
            _dependencyConflictMessage = _dependencyConflictMessage.Trim();
        }

        private void DrawQueuedGoogleRow(string name, string version, bool remove)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(remove ? "− " + name : $"+ {name} {version}", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_session.VerificationPending))
                {
                    if (GUILayout.Button("Undo", GUILayout.Width(45f))) UndoGoogleChange(name);
                }
            }
        }

        private void DrawQueuedGitHubRow(string name, GitHubAction action)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(action == GitHubAction.Remove ? "− " + name : $"+ {name} {GetQueuedGitHubTarget(name)?.Version}", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_session.VerificationPending))
                {
                    if (!GUILayout.Button("Undo", GUILayout.Width(45f))) return;
                }
                if (name == "Adjust") _adjustAction = GitHubAction.None;
                else if (name == "AppLovin") _applovinAction = GitHubAction.None;
                else if (name == "Facebook") _facebookAction = GitHubAction.None;
                else _appMetricaAction = GitHubAction.None;
                _queuedGitHubTargets.Remove(name);
                QueueChanged();
            }
        }

        private void DrawApplyFooter()
        {
            var validPath = PackageManifestHelper.TryGetPathInsideProject(_downloadPath, out var path);
            var disabledReason = !string.IsNullOrEmpty(_installedStateError) ? "Refresh installed state to resolve the error."
                : RequiresDownloadFolder && !validPath ? "Choose a project-relative download folder in Settings."
                : !string.IsNullOrEmpty(_dependencyConflictMessage) ? "Resolve the planning errors above."
                : DependencyInstallCoordinator.HasPendingWork ? "Finish or stop the current installation sequence first."
                : _session.VerificationPending ? "Verify the previous operation before applying more changes." : string.Empty;
            if (!string.IsNullOrEmpty(disabledReason)) EditorGUILayout.HelpBox(disabledReason, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(QueuedChangeCount == 0 || _isFetching || _isCommitting || !string.IsNullOrEmpty(disabledReason)))
                {
                    if (GUILayout.Button($"Apply {QueuedChangeCount} change(s)", GUILayout.Height(32f)))
                        CommitAsync(validPath ? path : GetDefaultDownloadPath());
                }
                using (new EditorGUI.DisabledScope(QueuedChangeCount == 0 || _isCommitting || _session.VerificationPending))
                {
                    if (GUILayout.Button("Discard", GUILayout.Width(70f), GUILayout.Height(32f)))
                    {
                        ClearQueue();
                        SetStatus("Queued changes discarded.", MessageType.Info);
                    }
                }
            }
        }

        private void DrawOperationFooter()
        {
            var installation = DependencyInstallCoordinator.State;
            if (installation?.HasPendingWork == true)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(installation.Status, EditorStyles.wordWrappedLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (installation.Phase == DependencyInstallationPhase.Paused && GUILayout.Button("Resume sequence"))
                            DependencyInstallCoordinator.Resume();
                        if (GUILayout.Button(installation.Phase == DependencyInstallationPhase.Paused ? "Stop sequence" : "Cancel remaining sequence"))
                            DependencyInstallCoordinator.Cancel();
                    }
                }
                Repaint();
                return;
            }
            if (!_isCommitting && !_isFetching) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var rect = GUILayoutUtility.GetRect(100f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, _operationProgress, _statusMessage);
                using (new EditorGUI.DisabledScope(!_canCancel))
                {
                    if (GUILayout.Button("Cancel", GUILayout.Width(65f))) _operationCancellation.Cancel();
                }
            }
            Repaint();
        }

        private void DrawOperationResults()
        {
            if (_session.Results.Count == 0) return;
            _showResults = EditorGUILayout.Foldout(_showResults, "Last operation results", true);
            if (!_showResults) return;
            foreach (var result in _session.Results)
                EditorGUILayout.HelpBox($"{result.PackageName}: {result.Message}", result.Succeeded ? MessageType.Info : MessageType.Warning);
            if (GUILayout.Button("Copy details"))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", _session.Results.Select(result => result.PackageName + ": " + result.Message));
            if (_session.VerificationPending && !_isCommitting && GUILayout.Button("Verify installed state again"))
                _ = ResumeVerificationAsync();
            if (_session.VerificationPending && !_isCommitting && GUILayout.Button("Keep failed changes for retry"))
            {
                RestoreFailedVerificationChanges();
                _session.VerificationPending = false;
                SaveSession();
                SetStatus("Verification results retained. Review and retry the remaining queue.", MessageType.Warning);
            }
        }

        private void DrawDownloadSettings()
        {
            using (new EditorGUI.DisabledScope(_isCommitting || DependencyInstallCoordinator.HasPendingWork))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _downloadPath = EditorGUILayout.TextField("Tarball folder", _downloadPath);
                if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(ProjectDownloadPathKey, _downloadPath);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Browse"))
                    {
                        PackageManifestHelper.TryGetPathInsideProject(_downloadPath, out var initial);
                        var chosen = EditorUtility.OpenFolderPanel("Choose Package Download Folder", string.IsNullOrEmpty(initial) ? GetDefaultDownloadPath() : initial, string.Empty);
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            _downloadPath = PackageManifestHelper.GetProjectRelativePath(chosen);
                            EditorPrefs.SetString(ProjectDownloadPathKey, _downloadPath);
                        }
                    }
                    if (GUILayout.Button("Reset"))
                    {
                        _downloadPath = DefaultDownloadDir;
                        EditorPrefs.SetString(ProjectDownloadPathKey, _downloadPath);
                    }
                }
                EditorGUILayout.LabelField("Relative to this project. Queue and results survive window closure and assembly reloads during this Editor session.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("SDK .unitypackage files and ZIP wrappers are staged in Temp/DependenciesDownloader/Downloads.", EditorStyles.wordWrappedMiniLabel);
                if (!PackageManifestHelper.IsPathInsideProject(_downloadPath))
                    EditorGUILayout.HelpBox("The download folder must be inside this Unity project.", MessageType.Warning);
            }
        }

        private string GetCacheAgeLabel()
        {
            var fetched = _activeTab == Tab.Google ? _archive?.FetchedAt : _activeTab == Tab.Adjust ? _adjustCache?.FetchedAt
                : _activeTab == Tab.AppLovin ? _applovinCache?.FetchedAt : _activeTab == Tab.Facebook ? _facebookCache?.FetchedAt : _appMetricaCache?.FetchedAt;
            if (!DateTimeOffset.TryParse(fetched, out var timestamp)) return "No cached data";
            var age = DateTimeOffset.UtcNow - timestamp;
            return age.TotalHours >= 24 ? $"Cached {(int)age.TotalDays}d ago" : age.TotalMinutes >= 60 ? $"Cached {(int)age.TotalHours}h ago" : $"Cached {Math.Max(0, (int)age.TotalMinutes)}m ago";
        }

        private void DrawPaneDivider()
        {
            var rect = GUILayoutUtility.GetRect(5f, 5f, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            var current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                _resizingPane = true;
                current.Use();
            }
            if (_resizingPane && current.type == EventType.MouseDrag)
            {
                _paneFraction = Mathf.Clamp(current.mousePosition.x / position.width, 0.4f, 0.7f);
                current.Use();
                Repaint();
            }
            if (current.rawType == EventType.MouseUp) _resizingPane = false;
        }

        private void DrawTrackedPackageHealth(string name, bool managedByUpm)
        {
            var record = UnityPackageTracker.GetInstallRecord(name);
            if (record == null) return;
            if (managedByUpm)
                EditorGUILayout.HelpBox("An Assets installation record also exists. Inspect the tracked paths before reconciling the duplicate installation.", MessageType.Warning);
            if (_assetHealth.TryGetValue(name, out var health))
                EditorGUILayout.HelpBox(health, health == "Tracked files verified." ? MessageType.Info : MessageType.Warning);
            if (GUILayout.Button($"Inspect tracked paths ({record.TrackedFiles.Count})"))
            {
                if (!_expandedFiles.Add(name)) _expandedFiles.Remove(name);
            }
            if (_expandedFiles.Contains(name))
            {
                EditorGUILayout.TextArea(string.Join("\n", record.TrackedFiles.OrderBy(path => path)), GUILayout.MaxHeight(100f));
                if (GUILayout.Button("Copy tracked paths")) EditorGUIUtility.systemCopyBuffer = string.Join("\n", record.TrackedFiles);
            }
        }

        private void DrawFacebookDeliveryInfo()
        {
            EditorGUILayout.HelpBox(
                "Official delivery: Facebook SDK ZIP containing a .unitypackage. The tool extracts, inspects, imports, and tracks its SDK assets.",
                MessageType.Info
            );
            if (_installedStates.TryGetValue(FacebookSdkArchive.DependencyManagerPackageId, out var resolver))
            {
                EditorGUILayout.HelpBox(
                    $"EDM {resolver.Version} is UPM-managed. Its bundled Assets copy will be excluded; the installed resolver must meet the bundle's minimum version.",
                    MessageType.Info
                );
                if (GUILayout.Button("Open EDM in Package Manager"))
                    UnityEditor.PackageManager.UI.Window.Open(FacebookSdkArchive.DependencyManagerPackageId);
            }
            if (string.IsNullOrEmpty(_facebookInstalledVer) && Directory.Exists(Path.Combine(Application.dataPath, "FacebookSDK")))
                EditorGUILayout.HelpBox("Existing FacebookSDK assets are not tracked by this tool. Installing a release will track the imported paths for future updates/removal.", MessageType.Warning);
            if (GUILayout.Button("Official Facebook SDK downloads"))
                Application.OpenURL("https://developers.facebook.com/documentation/unity/downloads");
        }
    }
}
