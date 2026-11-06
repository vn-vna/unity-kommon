using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed partial class DependenciesDownloaderWindow : EditorWindow
    {
        private const string DownloadPathKey = "Scheherazade.DepsDownloader.DownloadPath";
        private const string DefaultDownloadDir = "LocalPackages";
        private const string AdjustPackageId = "com.adjust.sdk";
        private const string AppLovinPackageId = "com.applovin.mediation.ads";
        private const string AppMetricaPackageId = "io.appmetrica.analytics";

        // ── Shared state ────────────────────────────────────

        private bool _isFetching;
        private bool _isCommitting;
        private string _statusMessage = "Ready.";
        private MessageType _statusType = MessageType.Info;

        private Vector2 _summaryScroll;
        private string _downloadPath;
        private readonly List<string> _commitWarnings = new List<string>();
        private readonly Dictionary<string, InstalledDependencyInfo>
            _installedDependencies = new Dictionary<string, InstalledDependencyInfo>();
        private string _dependencyConflictMessage = string.Empty;

        // ── Tab state ───────────────────────────────────────

        private enum Tab
        {
            Google = 0,
            Adjust = 1,
            AppLovin = 2,
            AppMetrica = 3,
            Facebook = 4
        }

        [SerializeField] private Tab _activeTab = Tab.Google;

        // ── Google tab state ────────────────────────────────

        private GoogleArchiveCache _archive;

        private Vector2 _googlePackageListScroll;
        [SerializeField] private string _searchFilter = string.Empty;
        [SerializeField] private string _selectedCategory = "All";
        private List<string> _categories = new List<string>();

        private HashSet<string> _queuedInstalls = new HashSet<string>();
        private HashSet<string> _queuedRemovals = new HashSet<string>();
        private Dictionary<string, string> _packageVersions = new Dictionary<string, string>();

        private Dictionary<string, string> _installedCache;
        private double _installedCacheTime;

        // ── GitHub tab state ────────────────────────────────

        private enum GitHubAction { None, Install, Remove }

        private sealed class InstalledDependencyInfo
        {
            public string PackageName;
            public string RequiredVersion;
            public string InstalledVersion;
        }

        private const string AdjustRepoOwner = "adjust";
        private const string AdjustRepoName = "unity_sdk";
        private const string AppLovinRepoOwner = "AppLovin";
        private const string AppLovinRepoName = "AppLovin-MAX-Unity-Plugin";
        private const string AppMetricaRepoOwner = "appmetrica";
        private const string AppMetricaRepoName = "appmetrica-unity-plugin";
        private const string FacebookRepoOwner = "facebook";
        private const string FacebookRepoName = "facebook-sdk-for-unity";

        private GitHubReleaseCache _adjustCache;
        private GitHubReleaseCache _applovinCache;
        private GitHubReleaseCache _appMetricaCache;
        private GitHubReleaseCache _facebookCache;
        private GitHubAction _adjustAction = GitHubAction.None;
        private GitHubAction _applovinAction = GitHubAction.None;
        private GitHubAction _appMetricaAction = GitHubAction.None;
        private GitHubAction _facebookAction = GitHubAction.None;
        private int _adjustVersionIdx;
        private int _applovinVersionIdx;
        private int _appMetricaVersionIdx;
        private int _facebookVersionIdx;
        private string _adjustInstalledVer;
        private string _applovinInstalledVer;
        private string _appMetricaInstalledVer;
        private string _facebookInstalledVer;
        private bool _adjustManagedByUpm;
        private bool _applovinManagedByUpm;
        private Vector2 _adjustNotesScroll;
        private Vector2 _applovinNotesScroll;
        private Vector2 _appMetricaNotesScroll;
        private Vector2 _facebookNotesScroll;

        // ── Properties ──────────────────────────────────────

        private Dictionary<string, string> InstalledPackages
        {
            get
            {
                var now = EditorApplication.timeSinceStartup;
                if (_installedCache == null || now - _installedCacheTime > 2.0)
                {
                    RefreshInstalledState(false);
                }

                return _installedCache ?? new Dictionary<string, string>();
            }
        }

        [MenuItem("Dev Menu/Tools/Dependencies Downloader")]
        public static void ShowWindow()
        {
            GetWindow<DependenciesDownloaderWindow>("Dependencies Downloader");
        }

        private void OnEnable()
        {
            _operationCancellation = new CancellationTokenSource();
            _downloadPath = EditorPrefs.GetString(ProjectDownloadPathKey,
                PackageManifestHelper.IsPathInsideProject(EditorPrefs.GetString(DownloadPathKey, string.Empty))
                    ? PackageManifestHelper.GetProjectRelativePath(EditorPrefs.GetString(DownloadPathKey))
                    : DefaultDownloadDir);
            if (string.IsNullOrWhiteSpace(_downloadPath))
            {
                _downloadPath = DefaultDownloadDir;
            }
            EditorPrefs.SetString(ProjectDownloadPathKey, _downloadPath);

            _archive = GoogleArchiveParser.GetCachedArchive();
            if (_archive?.Packages != null && _archive.Packages.Count > 0)
            {
                RebuildCategories();
            }

            _adjustCache = GitHubReleaseFetcher.GetCachedRelease(
                AdjustRepoOwner, AdjustRepoName
            );
            _applovinCache = GitHubReleaseFetcher.GetCachedRelease(
                AppLovinRepoOwner, AppLovinRepoName
            );
            _appMetricaCache = GitHubReleaseFetcher.GetCachedRelease(
                AppMetricaRepoOwner, AppMetricaRepoName
            );
            _facebookCache = GitHubReleaseFetcher.GetCachedRelease(FacebookRepoOwner, FacebookRepoName);

            RestoreSession();
            RefreshInstalledState();
            minSize = new Vector2(850f, 500f);
            SynchronizeInstallation();
            if (_session.VerificationPending && !DependencyInstallCoordinator.HasPendingWork) _ = ResumeVerificationAsync();
        }

        private void OnDisable()
        {
            SaveSession();
            _operationCancellation?.Cancel();
            if (!_isCommitting && !_isFetching) _operationCancellation?.Dispose();
        }

        private void OnFocus()
        {
            if (!_isCommitting) RefreshInstalledState();
        }

        private void OnGUI()
        {
            SynchronizeInstallation();
            if (HandleExternalOperation()) return;
            DrawToolbar();
            EditorGUILayout.Space(4f);
            DrawStatusBox();
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(_isCommitting || DependencyInstallCoordinator.HasPendingWork))
            {
                DrawBody();
            }
            DrawOperationFooter();
        }

        // ═══════════════════════════════════════════════════════════
        // ── Toolbar ──────────────────────────────────────────

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(_isFetching || _isCommitting || DependencyInstallCoordinator.HasPendingWork))
                {
                    if (GUILayout.Button(_activeTab == Tab.Google ? "Refresh Registry" : "Refresh Releases",
                            EditorStyles.toolbarButton, GUILayout.Width(130f)))
                        FetchActiveTabAsync();
                    if (GUILayout.Button("Refresh Installed", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                        RefreshInstalledState();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(GetCacheAgeLabel(), EditorStyles.miniLabel);
                _showSettings = GUILayout.Toggle(_showSettings, "Settings", EditorStyles.toolbarButton, GUILayout.Width(70f));
            }
            if (_showSettings) DrawDownloadSettings();
        }

        // ── Status ───────────────────────────────────────────

        private void DrawStatusBox()
        {
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
            if (!string.IsNullOrEmpty(_installedStateError))
                EditorGUILayout.HelpBox(_installedStateError, MessageType.Error);
        }

        // ═══════════════════════════════════════════════════════════
        // ── Body ─────────────────────────────────────────────

        private void DrawBody()
        {
            DrawTabBar();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(
                    GUILayout.Width(Mathf.Clamp(position.width * _paneFraction, 360f, position.width - 290f))))
                using (new EditorGUI.DisabledScope(_session.VerificationPending))
                {
                    switch (_activeTab)
                    {
                        case Tab.Google: DrawGoogleTab(); break;
                        case Tab.Adjust:
                            DrawGitHubTab(
                                _adjustCache,
                                "Adjust",
                                "Adjust SDK",
                                ref _adjustAction,
                                ref _adjustVersionIdx,
                                _adjustInstalledVer,
                                _adjustManagedByUpm,
                                ref _adjustNotesScroll
                            );
                            break;
                        case Tab.AppLovin:
                            DrawGitHubTab(
                                _applovinCache,
                                "AppLovin",
                                "AppLovin MAX",
                                ref _applovinAction,
                                ref _applovinVersionIdx,
                                _applovinInstalledVer,
                                _applovinManagedByUpm,
                                ref _applovinNotesScroll
                            );
                            break;
                        case Tab.AppMetrica:
                            DrawGitHubTab(
                                _appMetricaCache,
                                "AppMetrica",
                                "AppMetrica SDK",
                                ref _appMetricaAction,
                                ref _appMetricaVersionIdx,
                                _appMetricaInstalledVer,
                                false,
                                ref _appMetricaNotesScroll
                            );
                            break;
                        case Tab.Facebook:
                            DrawFacebookDeliveryInfo();
                            DrawGitHubTab(
                                _facebookCache, "Facebook", "Facebook SDK for Unity",
                                ref _facebookAction, ref _facebookVersionIdx,
                                _facebookInstalledVer, false, ref _facebookNotesScroll
                            );
                            break;
                    }
                }

                DrawPaneDivider();

                using (new EditorGUILayout.VerticalScope(
                    GUILayout.ExpandWidth(true)))
                {
                    DrawQueueSummary();
                    DrawApplyFooter();
                }
            }
        }

        // ── Tab bar ──────────────────────────────────────────

        private void DrawTabBar()
        {
            var tabNames = new[]
            {
                "Google Packages",
                "Adjust",
                "AppLovin MAX",
                "AppMetrica",
                "Facebook SDK"
            };
            EditorGUI.BeginChangeCheck();
            var selected = GUILayout.Toolbar((int)_activeTab, tabNames);
            if (EditorGUI.EndChangeCheck())
            {
                _activeTab = (Tab)selected;
                Repaint();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── Google tab ───────────────────────────────────────

        private void DrawGoogleTab()
        {
            if (_archive?.Packages == null)
            {
                EditorGUILayout.LabelField(
                    "No package data. Click Refresh Registry to fetch.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawGoogleFilterBar();
            DrawGooglePackageList(GetFilteredPackages());
        }

        private void DrawGoogleFilterBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(45f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter);

                var catIdx = _categories.IndexOf(_selectedCategory);
                if (catIdx < 0) catIdx = 0;
                var newIdx = EditorGUILayout.Popup(
                    catIdx, _categories.ToArray(), GUILayout.Width(130f));
                if (newIdx >= 0 && newIdx < _categories.Count)
                    _selectedCategory = _categories[newIdx];
            }
            _packageView = (PackageView)EditorGUILayout.EnumPopup("Show", _packageView);
        }

        private void DrawGooglePackageList(List<GooglePackageInfo> packages)
        {
            _googlePackageListScroll = EditorGUILayout.BeginScrollView(
                _googlePackageListScroll, GUILayout.ExpandHeight(true));

            if (packages.Count == 0)
                EditorGUILayout.LabelField(
                    "No packages match the filter.",
                    EditorStyles.centeredGreyMiniLabel);

            var installed = InstalledPackages;
            foreach (var pkg in packages)
                DrawPackageRow(pkg, installed);

            EditorGUILayout.EndScrollView();
        }

        private void DrawPackageRow(
            GooglePackageInfo pkg, Dictionary<string, string> installed)
        {
            var isInstalled = installed.ContainsKey(pkg.Name);
            var isQueuedInstall = _queuedInstalls.Contains(pkg.Name);
            var isQueuedRemove = _queuedRemovals.Contains(pkg.Name);
            var installedVersion = isInstalled
                ? ExtractVersionFromManifestEntry(installed[pkg.Name])
                : string.Empty;
            _installedStates.TryGetValue(pkg.Name, out var state);
            var selectedVersion = GetSelectedGoogleVersion(pkg, installedVersion);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(pkg.DisplayName, pkg.Description), EditorStyles.boldLabel);
                    if (isQueuedInstall || isQueuedRemove)
                    {
                        GUILayout.Label(isQueuedRemove ? "Removal queued" : $"Queued: {selectedVersion}", EditorStyles.miniBoldLabel);
                        if (GUILayout.Button("Undo", GUILayout.Width(50f))) UndoGoogleChange(pkg.Name);
                    }
                }
                EditorGUILayout.SelectableLabel(pkg.Name, EditorStyles.miniLabel, GUILayout.Height(16f));
                EditorGUILayout.LabelField(isInstalled
                    ? $"{(state?.IsRegistered == true ? "Installed" : "Declared (not registered)")}: {installedVersion} • {state?.Source ?? "UPM"} • {(state?.IsDirect == true ? "Direct" : "Transitive")}" 
                    : "Not installed", EditorStyles.miniLabel);
                if (state?.RequiredBy.Count > 0)
                    EditorGUILayout.LabelField("Required by: " + string.Join(", ", state.RequiredBy), EditorStyles.wordWrappedMiniLabel);

                DrawGoogleVersionPopup(pkg, installedVersion);
                selectedVersion = GetSelectedGoogleVersion(pkg, installedVersion);
                var metadata = pkg.Versions?.Find(version => version.Version == selectedVersion);
                if (metadata != null)
                    EditorGUILayout.LabelField($"Released {metadata.PublishDate} • Minimum Unity {metadata.MinUnityVersion}", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isQueuedRemove))
                    {
                        if (GUILayout.Button(selectedVersion == installedVersion ? "Queue reinstall" : DependencyVersionUtility.ActionLabel(installedVersion, selectedVersion)))
                            QueueGoogleInstall(pkg.Name, selectedVersion, true);
                    }
                    using (new EditorGUI.DisabledScope(!isInstalled || state?.IsDirect != true || isQueuedRemove))
                    {
                        if (GUILayout.Button("Queue removal", GUILayout.Width(105f)))
                        {
                            _queuedInstalls.Remove(pkg.Name);
                            _queuedRemovals.Add(pkg.Name);
                            QueueChanged();
                        }
                    }
                    if (isInstalled && GUILayout.Button("UPM", GUILayout.Width(45f)))
                        UnityEditor.PackageManager.UI.Window.Open(pkg.Name);
                }
                if (state != null && !state.IsDirect)
                    EditorGUILayout.LabelField("Removal is controlled by the parent dependencies.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawGoogleVersionPopup(
            GooglePackageInfo pkg, string installedVersion)
        {
            if (pkg.Versions == null || pkg.Versions.Count == 0) return;

            var selected = GetSelectedGoogleVersion(pkg, installedVersion);
            var names = pkg.Versions.Select(version => version.Version).ToList();
            if (!names.Contains(selected)) names.Insert(0, selected);
            var labels = names.Select(version => pkg.Versions.Any(item => item.Version == version)
                ? version : version + " (unavailable)").ToArray();
            EditorGUI.BeginChangeCheck();
            var index = EditorGUILayout.Popup("Target version", names.IndexOf(selected), labels);
            if (EditorGUI.EndChangeCheck() && index >= 0)
            {
                _packageVersions[pkg.Name] = names[index];
                if (_queuedInstalls.Contains(pkg.Name)) QueueGoogleInstall(pkg.Name, names[index]);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── GitHub tab (shared for Adjust & AppLovin) ───────

        private void DrawGitHubTab(
            GitHubReleaseCache cache,
            string packageName,
            string displayName,
            ref GitHubAction action,
            ref int versionIdx,
            string installedVer,
            bool managedByUpm,
            ref Vector2 notesScroll)
        {
            if (cache?.Releases == null || cache.Releases.Count == 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    displayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(installedVer) ? "Not installed" : "Installed: " + installedVer);
                DrawTrackedPackageHealth(packageName, managedByUpm);
                if (managedByUpm && GUILayout.Button("Open Package Manager"))
                    UnityEditor.PackageManager.UI.Window.Open(packageName == "Adjust" ? AdjustPackageId : AppLovinPackageId);
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField(
                    "No release data. Click Refresh Releases to fetch.",
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            var releases = cache.Releases;
            if (versionIdx < 0 || versionIdx >= releases.Count)
                versionIdx = 0;
            var selected = releases[versionIdx];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);

            if (managedByUpm)
            {
                EditorGUILayout.HelpBox(
                    $"{displayName} is managed by Unity Package Manager " +
                    $"({installedVer}). Use Package Manager to update or remove it.",
                    MessageType.Info
                );
                if (GUILayout.Button("Open Package Manager"))
                    UnityEditor.PackageManager.UI.Window.Open(packageName == "Adjust" ? AdjustPackageId : AppLovinPackageId);
            }

            DrawTrackedPackageHealth(packageName, managedByUpm);

            var versionNames = releases.Select(r => r.Version + (r.IsPrerelease ? " (prerelease)" : string.Empty)).ToArray();
            EditorGUI.BeginChangeCheck();
            versionIdx = EditorGUILayout.Popup(
                "Version", versionIdx, versionNames);
            if (EditorGUI.EndChangeCheck())
            {
                selected = releases[versionIdx];
                if (action == GitHubAction.Install)
                    _queuedGitHubTargets[packageName] = CloneRelease(selected);
                QueueChanged();
            }

            // status line
            EditorGUILayout.Space(4f);
            if (!string.IsNullOrEmpty(installedVer))
            {
                if (action == GitHubAction.Remove)
                    EditorGUILayout.LabelField(
                        $"Installed: {installedVer} (will be removed)",
                        EditorStyles.miniLabel);
                else if (action == GitHubAction.Install)
                    EditorGUILayout.LabelField(
                        $"Installed: {installedVer} -> {GetQueuedGitHubTarget(packageName)?.Version}",
                        EditorStyles.miniLabel);
                else if (installedVer == selected.Version)
                    EditorGUILayout.LabelField(
                        $"Installed: {installedVer} (selected version)",
                        EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(
                        $"Installed: {installedVer}",
                        EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Not installed", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Source: {(managedByUpm ? "Unity Package Manager" : packageName == "AppMetrica" ? "Git / UPM" : "Assets import")}", EditorStyles.miniLabel);
            var latestStable = releases.FirstOrDefault(release => !release.IsPrerelease);
            if (latestStable != null)
                EditorGUILayout.LabelField("Latest stable: " + latestStable.Version, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Released: " + selected.PublishedAt, EditorStyles.miniLabel);
            if (action == GitHubAction.Install && GetQueuedGitHubTarget(packageName) != null)
                EditorGUILayout.LabelField("Pinned release: " + GetQueuedGitHubTarget(packageName).TagName, EditorStyles.miniLabel);

            // action buttons
            using (new EditorGUI.DisabledScope(managedByUpm))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (action == GitHubAction.None)
                {
                    if (!managedByUpm)
                    {
                        var label = installedVer == selected.Version ? "Queue reinstall (repair)"
                            : DependencyVersionUtility.ActionLabel(installedVer, selected.Version);
                        if (GUILayout.Button(label))
                        {
                            action = GitHubAction.Install;
                            _queuedGitHubTargets[packageName] = CloneRelease(selected);
                            QueueChanged();
                        }
                    }

                    if (!string.IsNullOrEmpty(installedVer) &&
                        GUILayout.Button("Queue removal", GUILayout.Width(105f)))
                    {
                        action = GitHubAction.Remove;
                        _queuedGitHubTargets.Remove(packageName);
                        QueueChanged();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(
                        action == GitHubAction.Install
                            ? $"Queued: install {GetQueuedGitHubTarget(packageName)?.Version}"
                            : "Queued: remove",
                        EditorStyles.miniLabel);

                    if (GUILayout.Button("Undo", GUILayout.Width(70f)))
                    {
                        action = GitHubAction.None;
                        _queuedGitHubTargets.Remove(packageName);
                        QueueChanged();
                        if (!string.IsNullOrEmpty(installedVer))
                        {
                            var instIdx = releases.FindIndex(
                                r => r.Version == installedVer);
                            if (instIdx >= 0) versionIdx = instIdx;
                        }
                    }
                }
            }

            // release notes
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Release Notes", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(selected.ReleaseUrl) && GUILayout.Button("Open release on GitHub"))
                Application.OpenURL(selected.ReleaseUrl);
            var bodyText = selected.Body ?? string.Empty;
            notesScroll = EditorGUILayout.BeginScrollView(
                notesScroll,
                GUILayout.ExpandHeight(true)
            );
            EditorGUILayout.HelpBox(bodyText, MessageType.None);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════
        // ── Queue summary ────────────────────────────────────

        private void DrawQueueSummary()
        {
            var allResolved = ResolveAllEntries();
            _summaryScroll = EditorGUILayout.BeginScrollView(
                _summaryScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Changes Pending", EditorStyles.boldLabel);

            // install section
            EditorGUILayout.LabelField("Install", EditorStyles.boldLabel);
            var installCount = 0;

            // Google installs
            if (_queuedInstalls.Count > 0)
            {
                foreach (var name in _queuedInstalls.OrderBy(name => name).ToArray())
                {
                    var ver = _packageVersions.TryGetValue(name, out var v)
                        ? v : "?";
                    DrawQueuedGoogleRow(name, ver, false);
                    installCount++;
                }

                var transitive = allResolved
                    .Where(e => !_queuedInstalls.Contains(e.PackageName))
                    .ToList();
                foreach (var dep in transitive)
                {
                    EditorGUILayout.LabelField(
                        $"    > {dep.PackageName} {dep.Version}",
                        EditorStyles.miniLabel);
                    installCount++;
                }

                DrawInstalledDependencies();
            }

            // GitHub installs
            if (_adjustAction == GitHubAction.Install)
            {
                DrawQueuedGitHubRow("Adjust", _adjustAction);
                installCount++;
            }

            if (_applovinAction == GitHubAction.Install)
            {
                DrawQueuedGitHubRow("AppLovin", _applovinAction);
                installCount++;
            }

            if (_appMetricaAction == GitHubAction.Install)
            {
                DrawQueuedGitHubRow("AppMetrica", _appMetricaAction);
                installCount++;
            }
            if (_facebookAction == GitHubAction.Install)
            {
                DrawQueuedGitHubRow("Facebook", _facebookAction);
                installCount++;
            }

            if (installCount == 0)
                EditorGUILayout.LabelField(
                    "  (none)", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);

            // remove section
            EditorGUILayout.LabelField("Remove", EditorStyles.boldLabel);
            var removeCount = 0;

            foreach (var name in _queuedRemovals.OrderBy(name => name).ToArray())
            {
                DrawQueuedGoogleRow(name, string.Empty, true);
                removeCount++;
            }

            if (_adjustAction == GitHubAction.Remove)
            {
                DrawQueuedGitHubRow("Adjust", _adjustAction);
                removeCount++;
            }

            if (_applovinAction == GitHubAction.Remove)
            {
                DrawQueuedGitHubRow("AppLovin", _applovinAction);
                removeCount++;
            }

            if (_appMetricaAction == GitHubAction.Remove)
            {
                DrawQueuedGitHubRow("AppMetrica", _appMetricaAction);
                removeCount++;
            }
            if (_facebookAction == GitHubAction.Remove)
            {
                DrawQueuedGitHubRow("Facebook", _facebookAction);
                removeCount++;
            }

            if (removeCount == 0)
                EditorGUILayout.LabelField(
                    "  (none)", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_dependencyConflictMessage))
            {
                EditorGUILayout.HelpBox(
                    _dependencyConflictMessage,
                    MessageType.Error
                );
            }

            DrawOperationResults();
            EditorGUILayout.EndScrollView();
        }

        private void DrawInstalledDependencies()
        {
            if (_installedDependencies.Count == 0) return;

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(
                "Already Installed (Package Manager)",
                EditorStyles.miniBoldLabel
            );

            var originalColor = GUI.contentColor;
            foreach (var dependency in _installedDependencies.Values.OrderBy(
                         info => info.PackageName,
                         StringComparer.Ordinal
                     ))
            {
                var versionLabel = string.IsNullOrEmpty(
                    dependency.InstalledVersion
                )
                    ? "installed"
                    : $"installed {dependency.InstalledVersion}";
                var requiredLabel =
                    dependency.RequiredVersion == dependency.InstalledVersion ||
                    string.IsNullOrEmpty(dependency.RequiredVersion)
                        ? string.Empty
                        : $" (requires {dependency.RequiredVersion})";
                EditorGUILayout.LabelField(
                    $"    {(DependencyVersionUtility.Satisfies(dependency.PackageName, dependency.InstalledVersion, dependency.RequiredVersion) ? "Satisfied" : "Incompatible")}: {dependency.PackageName} — {versionLabel}" +
                    requiredLabel,
                    EditorStyles.miniLabel
                );
            }

            GUI.contentColor = originalColor;
        }

        private void RecordInstalledDependencies(
            List<DownloadEntry> dependencyTree,
            Dictionary<string, string> installedPackages)
        {
            if (dependencyTree == null || installedPackages == null) return;

            foreach (var dependency in dependencyTree)
            {
                if (!dependency.IsTransitive ||
                    !installedPackages.TryGetValue(
                        dependency.PackageName,
                        out var installedValue
                    ))
                {
                    continue;
                }

                if (_installedDependencies.ContainsKey(
                        dependency.PackageName
                    ))
                {
                    continue;
                }

                _installedDependencies[dependency.PackageName] =
                    new InstalledDependencyInfo
                    {
                        PackageName = dependency.PackageName,
                        RequiredVersion = dependency.Version,
                        InstalledVersion = ExtractVersionFromManifestEntry(
                            installedValue
                        )
                    };
            }
        }

        private List<DownloadEntry> ResolveAllEntries()
        {
            _dependencyConflictMessage = string.Empty;
            _installedDependencies.Clear();
            var installed = InstalledPackages;
            var states = new Dictionary<string, InstalledPackageState>(_installedStates);
            foreach (var pair in installed)
            {
                if (!states.ContainsKey(pair.Key))
                    states[pair.Key] = new InstalledPackageState { Name = pair.Key, Version = ExtractVersionFromManifestEntry(pair.Value) };
            }
            var installs = _queuedInstalls.ToDictionary(name => name,
                name => _packageVersions.TryGetValue(name, out var version) ? version : "?");
            var plan = DependencyChangePlanner.Build(_archive, installs, _queuedRemovals, states, Application.unityVersion);
            _dependencyConflictMessage = string.Join("\n", plan.Errors);
            RecordInstalledDependencies(plan.ExistingDependencies.Values.ToList(), installed);
            ValidateGitHubQueue();
            return plan.Downloads;
        }

        // ═══════════════════════════════════════════════════════════
        // ── Filters ──────────────────────────────────────────

        private List<GooglePackageInfo> GetFilteredPackages()
        {
            if (_archive?.Packages == null) return new List<GooglePackageInfo>();
            return _archive.Packages.Where(p =>
            {
                if (_selectedCategory != "All" && p.Category != _selectedCategory)
                    return false;
                var installed = InstalledPackages.ContainsKey(p.Name);
                if (_packageView == PackageView.Installed && !installed) return false;
                if (_packageView == PackageView.Pending && !_queuedInstalls.Contains(p.Name) && !_queuedRemovals.Contains(p.Name)) return false;
                if (_packageView == PackageView.UpdatesAvailable && (!installed || p.Versions.Count == 0 ||
                    !DependencyVersionUtility.TryCompare(p.Versions[0].Version, InstalledPackages[p.Name], out var comparison) || comparison <= 0)) return false;
                if (string.IsNullOrWhiteSpace(_searchFilter)) return true;
                var f = _searchFilter.Trim();
                return p.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       p.DisplayName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();
        }

        private void RebuildCategories()
        {
            _categories = new List<string> { "All" };
            if (_archive?.Packages == null) return;
            foreach (var pkg in _archive.Packages)
                if (!string.IsNullOrEmpty(pkg.Category) &&
                    !_categories.Contains(pkg.Category))
                    _categories.Add(pkg.Category);

            if (!_categories.Contains(_selectedCategory))
                _selectedCategory = "All";
        }

        // ═══════════════════════════════════════════════════════════
        // ── Fetch ────────────────────────────────────────────

        private async void FetchActiveTabAsync()
        {
            if (_isFetching || _isCommitting || _activeOperationOwner != 0) return;
            switch (_activeTab)
            {
                case Tab.Google:
                    await FetchGoogleArchiveAsync();
                    break;
                case Tab.Adjust:
                    await FetchGitHubTabAsync(
                        AdjustRepoOwner, AdjustRepoName);
                    break;
                case Tab.AppLovin:
                    await FetchGitHubTabAsync(
                        AppLovinRepoOwner,
                        AppLovinRepoName,
                        GitHubReleaseDelivery.UnityPackageAsset
                    );
                    break;
                case Tab.AppMetrica:
                    await FetchGitHubTabAsync(
                        AppMetricaRepoOwner,
                        AppMetricaRepoName,
                        GitHubReleaseDelivery.GitPackage
                    );
                    break;
                case Tab.Facebook:
                    await FetchGitHubTabAsync(FacebookRepoOwner, FacebookRepoName, GitHubReleaseDelivery.UnityPackageZipAsset);
                    break;
            }
        }

        private async Task FetchGoogleArchiveAsync()
        {
            ResetCancellation();
            _canCancel = true;
            _isFetching = true;
            SetStatus("Fetching Google package registry...", MessageType.Info);
            try
            {
                var result = await GoogleArchiveParser.FetchAndParseArchiveAsync(
                    _operationCancellation.Token, progress => _operationProgress = progress);
                if (result?.Packages == null || result.Packages.Count == 0)
                {
                    SetStatus(
                        "Google registry refresh failed and no usable cache is available. " +
                        GoogleArchiveParser.LastFetchError,
                        MessageType.Error
                    );
                    return;
                }

                _archive = result;
                _installedCache = null;
                RebuildCategories();

                if (GoogleArchiveParser.LastFetchCanceled)
                {
                    SetStatus(
                        "Refresh canceled. Showing the previously cached registry.",
                        MessageType.Warning
                    );
                }
                else if (GoogleArchiveParser.LastFetchUsedCache)
                {
                    SetStatus(
                        $"Refresh failed ({GoogleArchiveParser.LastFetchError}). " +
                        $"Showing cached data from {_archive.FetchedAt}.",
                        MessageType.Warning
                    );
                }
                else
                {
                    SetStatus(
                        $"Registry loaded. {_archive.Packages.Count} packages " +
                        $"across {_categories.Count - 1} categories.",
                        MessageType.Info
                    );
                }
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Google registry refresh failed: {ex.Message}",
                    MessageType.Error
                );
            }
            finally
            {
                _isFetching = false;
                _canCancel = false;
                Repaint();
            }
        }

        private async Task FetchGitHubTabAsync(
            string owner,
            string name,
            GitHubReleaseDelivery delivery =
                GitHubReleaseDelivery.UnityPackageAsset
        )
        {
            ResetCancellation();
            _canCancel = true;
            _isFetching = true;
            SetStatus(
                $"Fetching releases from {owner}/{name}...",
                MessageType.Info
            );
            try
            {
                var cache = await GitHubReleaseFetcher.FetchReleasesAsync(
                    owner,
                    name,
                    delivery,
                    _operationCancellation.Token,
                    progress => _operationProgress = progress
                );

                if (owner == AdjustRepoOwner)
                {
                    var previous = GetQueuedGitHubTarget("Adjust") ?? _adjustCache?.Releases?.ElementAtOrDefault(_adjustVersionIdx);
                    _adjustCache = cache;
                    _adjustVersionIdx = FindReleaseIndex(cache, previous);
                }
                else if (owner == AppLovinRepoOwner)
                {
                    var previous = GetQueuedGitHubTarget("AppLovin") ?? _applovinCache?.Releases?.ElementAtOrDefault(_applovinVersionIdx);
                    _applovinCache = cache;
                    _applovinVersionIdx = FindReleaseIndex(cache, previous);
                }
                else if (owner == FacebookRepoOwner)
                {
                    var previous = GetQueuedGitHubTarget("Facebook") ?? _facebookCache?.Releases?.ElementAtOrDefault(_facebookVersionIdx);
                    _facebookCache = cache;
                    _facebookVersionIdx = FindReleaseIndex(cache, previous);
                }
                else
                {
                    var previous = GetQueuedGitHubTarget("AppMetrica") ?? _appMetricaCache?.Releases?.ElementAtOrDefault(_appMetricaVersionIdx);
                    _appMetricaCache = cache;
                    _appMetricaVersionIdx = FindReleaseIndex(cache, previous);
                }

                RefreshGitHubInstalledVersions();

                if (cache?.Releases == null || cache.Releases.Count == 0)
                {
                    SetStatus(
                        "Release refresh failed. " +
                        GitHubReleaseFetcher.LastFetchError,
                        MessageType.Error
                    );
                }
                else if (GitHubReleaseFetcher.LastFetchUsedCache)
                {
                    SetStatus(
                        $"Refresh failed ({GitHubReleaseFetcher.LastFetchError}). " +
                        $"Showing {cache.Releases.Count} cached releases from " +
                        $"{cache.FetchedAt}.",
                        MessageType.Warning
                    );
                }
                else
                {
                    SetStatus(
                        $"Loaded {cache.Releases.Count} releases.",
                        MessageType.Info
                    );
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("Release refresh canceled. Existing cached releases and queued targets were preserved.", MessageType.Warning);
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Release refresh failed: {ex.Message}",
                    MessageType.Error
                );
            }
            finally
            {
                _isFetching = false;
                _canCancel = false;
                Repaint();
            }
        }

        private void RefreshGitHubInstalledVersions()
        {
            var adjustUpmVersion =
                PackageManifestHelper.GetInstalledPackageVersion(AdjustPackageId);
            var applovinUpmVersion =
                PackageManifestHelper.GetInstalledPackageVersion(AppLovinPackageId);
            var appMetricaManifestValue =
                PackageManifestHelper.GetInstalledPackageVersion(AppMetricaPackageId);

            _adjustManagedByUpm = _installedStates.ContainsKey(AdjustPackageId);
            _applovinManagedByUpm = _installedStates.ContainsKey(AppLovinPackageId);

            var adjustRecord =
                UnityPackageTracker.GetInstallRecord("Adjust");
            var applovinRecord =
                UnityPackageTracker.GetInstallRecord("AppLovin");

            _adjustInstalledVer = _adjustManagedByUpm
                ? GetResolvedVersion(AdjustPackageId, adjustUpmVersion)
                : adjustRecord?.Version ?? string.Empty;
            _applovinInstalledVer = _applovinManagedByUpm
                ? GetResolvedVersion(AppLovinPackageId, applovinUpmVersion)
                : applovinRecord?.Version ?? string.Empty;
            _appMetricaInstalledVer = string.IsNullOrEmpty(appMetricaManifestValue) ? string.Empty
                : GetResolvedVersion(AppMetricaPackageId, appMetricaManifestValue);
            _facebookInstalledVer = UnityPackageTracker.GetInstallRecord("Facebook")?.Version ?? string.Empty;

        }

        // ═══════════════════════════════════════════════════════════
        // ── Commit ───────────────────────────────────────────

        private async void CommitAsync(string downloadPath)
        {
            if (_isCommitting || _isFetching || _session.VerificationPending || _activeOperationOwner != 0 || DependencyInstallCoordinator.HasPendingWork) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
            {
                SetStatus("Wait for compilation to finish and resolve Console compilation errors before applying dependencies.", MessageType.Warning);
                return;
            }
            RefreshInstalledState();
            ResolveAllEntries();
            if (!string.IsNullOrEmpty(_dependencyConflictMessage) || !string.IsNullOrEmpty(_installedStateError))
            {
                SetStatus(_dependencyConflictMessage + "\n" + _installedStateError, MessageType.Error);
                return;
            }
            var confirmed = EditorUtility.DisplayDialog(
                "Apply Dependency Changes",
                $"Apply {QueuedChangeCount} queued change(s)? Tarballs and Git packages are applied in one UPM batch first. " +
                "SDK packages are staged in Temp and opened one at a time, with compilation completing between imports.",
                "Apply Changes",
                "Cancel"
            );
            if (!confirmed) return;
            try
            {
                if (!ConfirmTrackedFileChanges()) return;
            }
            catch (Exception ex)
            {
                SetStatus("Could not inspect tracked files: " + ex.Message, MessageType.Error);
                return;
            }

            _isCommitting = true;
            _activeOperationOwner = GetInstanceID();
            ResetCancellation();
            _canCancel = true;
            _session.Results.Clear();
            _session.Installation = null;
            _session.Verification = CreateVerificationPlan(downloadPath);
            _session.Interrupted = true;
            SaveSession();
            _commitWarnings.Clear();
            try
            {
                var validPath = PackageManifestHelper.TryGetPathInsideProject(downloadPath, out var normalizedDownloadPath);
                if (RequiresDownloadFolder && !validPath)
                {
                    SetStatus(
                        "Download path must be inside the project folder.",
                        MessageType.Error
                    );
                    return;
                }

                if (!validPath) normalizedDownloadPath = GetDefaultDownloadPath();
                if (RequiresDownloadFolder) Directory.CreateDirectory(normalizedDownloadPath);

                if (!await CommitInstallsAsync(normalizedDownloadPath))
                {
                    RecordResult(_currentOperationPackage, _statusMessage, false);
                    return;
                }

            }
            catch (OperationCanceledException)
            {
                SetStatus("Download canceled. Pending changes were preserved.", MessageType.Warning);
                RecordResult(_currentOperationPackage, "Canceled before application.", false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus(
                    $"Could not apply changes: {ex.Message}",
                    MessageType.Error
                );
                RecordResult(_currentOperationPackage, ex.Message, false);
            }
            finally
            {
                _canCancel = false;
                if (!DependencyInstallCoordinator.HasPendingWork)
                {
                    _session.VerificationPending = _session.Verification.Any(item => item.Applied);
                    if (!_session.VerificationPending) _session.Interrupted = false;
                    SaveSession();
                }
                _isCommitting = false;
                _activeOperationOwner = 0;
                if (this != null) Repaint();
                if (DependencyInstallCoordinator.HasPendingWork) SynchronizeInstallation();
            }
        }

        private async Task<bool> CommitInstallsAsync(string downloadPath)
        {
            var allEntries = new List<DownloadEntry>();
            allEntries.AddRange(ResolveAllEntries());
            if (!string.IsNullOrEmpty(_dependencyConflictMessage))
            {
                SetStatus(
                    _dependencyConflictMessage,
                    MessageType.Error
                );
                return false;
            }

            AddGitHubInstallEntry(
                allEntries,
                _adjustAction,
                GetQueuedGitHubTarget("Adjust"),
                "Adjust"
            );
            AddGitHubInstallEntry(
                allEntries,
                _applovinAction,
                GetQueuedGitHubTarget("AppLovin"),
                "AppLovin"
            );
            AddGitHubInstallEntry(allEntries, _facebookAction, GetQueuedGitHubTarget("Facebook"), "Facebook");

            var manifestRemovals = new HashSet<string>(_queuedRemovals);
            var gitAdditions = new Dictionary<string, string>();
            if (_appMetricaAction == GitHubAction.Install)
            {
                if (!TryGetSelectedAppMetricaGitUrl(out var gitUrl))
                {
                    SetStatus(
                        "No Git URL is available for the selected AppMetrica release.",
                        MessageType.Error
                    );
                    return false;
                }

                gitAdditions[AppMetricaPackageId] = gitUrl;
            }
            else if (_appMetricaAction == GitHubAction.Remove)
            {
                manifestRemovals.Add(AppMetricaPackageId);
            }

            SetStatus(
                $"Downloading {allEntries.Count} package(s)...",
                MessageType.Info
            );

            var packageFiles = new Dictionary<string, string>();
            for (int i = 0; i < allEntries.Count; i++)
            {
                var entry = allEntries[i];
                _currentOperationPackage = entry.PackageName;
                _operationCancellation.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.TarballUrl))
                {
                    SetStatus(
                        $"No download URL is available for {entry.PackageName} " +
                        $"{entry.Version}.",
                        MessageType.Error
                    );
                    return false;
                }

                var filePath = DependencyInstallationState.GetArchivePath(entry, downloadPath);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                packageFiles[GetEntryKey(entry)] = filePath;

                if (!IsDownloadedFileUsable(entry, filePath) &&
                    !await DownloadFileAsync(entry.TarballUrl, filePath, i, allEntries.Count))
                {
                    SetStatus(
                        $"Download failed for {entry.PackageName} " +
                        $"{entry.Version}. No project changes were applied.",
                        MessageType.Error
                    );
                    return false;
                }

                if (!IsDownloadedFileUsable(entry, filePath))
                {
                    SetStatus(
                        "Downloaded file validation failed for " +
                        $"{entry.PackageName} {entry.Version}.",
                        MessageType.Error
                    );
                    return false;
                }
                RecordResult(entry.PackageName, "Download validated; awaiting application.", false);
            }

            var googleEntries = allEntries.Where(
                entry => !IsUnityPackageEntry(entry)
            ).ToList();
            var unityPackageEntries = allEntries.Where(
                IsUnityPackageEntry
            ).ToList();

            RefreshInstalledState(false);
            var refreshedEntries = ResolveAllEntries();
            if (!string.IsNullOrEmpty(_dependencyConflictMessage) || !string.IsNullOrEmpty(_installedStateError))
                throw new InvalidOperationException(_dependencyConflictMessage + "\n" + _installedStateError);
            if (!new HashSet<string>(googleEntries.Select(GetEntryKey)).SetEquals(refreshedEntries.Select(GetEntryKey)))
                throw new InvalidOperationException("Installed dependencies changed during download. Review the updated plan and retry before applying project changes.");

            _operationCancellation.Token.ThrowIfCancellationRequested();
            var installation = new DependencyInstallationState
            {
                TarballDirectory = downloadPath,
                Tarballs = googleEntries,
                ManifestRemovals = manifestRemovals.ToList(),
                GitAdditions = gitAdditions.Select(pair => new ManifestGitAddition { PackageName = pair.Key, Url = pair.Value }).ToList(),
                Imports = unityPackageEntries.Select(entry => new StagedSdkImport
                {
                    Entry = entry,
                    ArchivePath = packageFiles[GetEntryKey(entry)],
                    AllowModifiedFiles = _allowModifiedFiles.Contains(entry.PackageName)
                }).ToList()
            };
            foreach (var pair in new[]
            {
                new KeyValuePair<string, GitHubAction>("Adjust", _adjustAction),
                new KeyValuePair<string, GitHubAction>("AppLovin", _applovinAction),
                new KeyValuePair<string, GitHubAction>("Facebook", _facebookAction)
            })
            {
                if (pair.Value == GitHubAction.Remove)
                    installation.Removals.Add(new StagedSdkRemoval { PackageName = pair.Key, AllowModifiedFiles = _allowModifiedFiles.Contains(pair.Key) });
            }
            SaveSession();
            DependencyInstallCoordinator.Start(_session, installation);
            return true;
        }

        private void ClearQueue()
        {
            _queuedInstalls.Clear();
            _queuedRemovals.Clear();
            _packageVersions.Clear();
            _adjustAction = GitHubAction.None;
            _applovinAction = GitHubAction.None;
            _appMetricaAction = GitHubAction.None;
            _facebookAction = GitHubAction.None;
            _queuedGitHubTargets.Clear();
            QueueChanged();
        }

        private async Task<bool> DownloadFileAsync(
            string url,
            string filePath,
            int index,
            int total)
        {
            _operationCancellation.Token.ThrowIfCancellationRequested();
            var temporaryPath = filePath + ".download";
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

                using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerFile(temporaryPath) { removeFileOnAbort = true };
                request.timeout = 120;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (_operationCancellation.IsCancellationRequested)
                    {
                        request.Abort();
                        _operationCancellation.Token.ThrowIfCancellationRequested();
                    }
                    _operationProgress = (index + request.downloadProgress) / total;
                    SetStatus($"Downloading ({index + 1}/{total}) {_currentOperationPackage} • {request.downloadedBytes / (1024f * 1024f):0.0} MB", MessageType.Info);
                    await Task.Delay(50);
                }
                _operationCancellation.Token.ThrowIfCancellationRequested();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new IOException($"Download failed (HTTP {request.responseCode}): {request.error}. Retry the pending change. URL: {url}");
                }

                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                {
                    Debug.LogError(
                        $"[DependenciesDownloader] Download returned no data " +
                        $"for '{url}'."
                    );
                    return false;
                }

                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(temporaryPath, filePath);
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception)
                {
                    // The next download attempt will retry this temporary file.
                }
            }
        }

        private static string GetDefaultDownloadPath()
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                DefaultDownloadDir
            ));
        }

        private bool TryGetSelectedAppMetricaGitUrl(out string gitUrl)
        {
            gitUrl = GetQueuedGitHubTarget("AppMetrica")?.GitUrl ?? string.Empty;
            return !string.IsNullOrWhiteSpace(gitUrl);
        }

        private static void AddGitHubInstallEntry(
            List<DownloadEntry> entries,
            GitHubAction action,
            GitHubReleaseInfo release,
            string packageName)
        {
            if (action != GitHubAction.Install || release == null)
            {
                return;
            }

            entries.Add(new DownloadEntry
            {
                PackageName = packageName,
                Version = release.Version,
                TarballUrl = release.DownloadUrl,
                IsZipArchive = release.IsZipArchive,
                IsTransitive = false
            });
        }

        private static string GetDownloadFileName(DownloadEntry entry)
        {
            if (entry.IsZipArchive) return $"{entry.PackageName}_v{entry.Version}.zip";
            return IsUnityPackageEntry(entry)
                ? $"{entry.PackageName}_v{entry.Version}.unitypackage"
                : $"{entry.PackageName}-{entry.Version}.tgz";
        }

        private static string GetEntryKey(DownloadEntry entry)
        {
            return $"{entry.PackageName}@{entry.Version}";
        }

        private static bool IsUnityPackageEntry(DownloadEntry entry)
        {
            return entry.PackageName == "Adjust" ||
                   entry.PackageName == "AppLovin" ||
                   entry.PackageName == "Facebook";
        }

        private static bool IsDownloadedFileUsable(
            DownloadEntry entry,
            string filePath)
        {
            try
            {
                if (entry.IsZipArchive) return FacebookSdkArchive.IsUsable(filePath);
                if (!File.Exists(filePath)) return false;
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 2) return false;

                using var stream = File.OpenRead(filePath);
                if (stream.ReadByte() != 0x1F || stream.ReadByte() != 0x8B)
                {
                    return false;
                }

                return IsUnityPackageEntry(entry)
                    ? UnityPackageTracker.EnumerateFilesInPackage(filePath).Count > 0
                    : UnityPackageTracker.ValidateTarballPackage(filePath, entry.PackageName, entry.Version);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── Helpers ──────────────────────────────────────────

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            if (this != null) Repaint();
        }

        private static string ExtractVersionFromManifestEntry(
            string manifestValue)
        {
            return DependencyVersionUtility.Extract(manifestValue);
        }
    }
}
