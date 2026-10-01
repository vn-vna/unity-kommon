using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class DependenciesDownloaderWindow : EditorWindow
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
            AppMetrica = 3
        }

        private Tab _activeTab = Tab.Google;

        // ── Google tab state ────────────────────────────────

        private GoogleArchiveCache _archive;

        private Vector2 _googlePackageListScroll;
        private string _searchFilter = string.Empty;
        private string _selectedCategory = "All";
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

        private GitHubReleaseCache _adjustCache;
        private GitHubReleaseCache _applovinCache;
        private GitHubReleaseCache _appMetricaCache;
        private GitHubAction _adjustAction = GitHubAction.None;
        private GitHubAction _applovinAction = GitHubAction.None;
        private GitHubAction _appMetricaAction = GitHubAction.None;
        private int _adjustVersionIdx;
        private int _applovinVersionIdx;
        private int _appMetricaVersionIdx;
        private string _adjustInstalledVer;
        private string _applovinInstalledVer;
        private string _appMetricaInstalledVer;
        private bool _adjustManagedByUpm;
        private bool _applovinManagedByUpm;
        private Vector2 _adjustNotesScroll;
        private Vector2 _applovinNotesScroll;
        private Vector2 _appMetricaNotesScroll;

        // ── Properties ──────────────────────────────────────

        private Dictionary<string, string> InstalledPackages
        {
            get
            {
                var now = EditorApplication.timeSinceStartup;
                if (_installedCache == null || now - _installedCacheTime > 2.0)
                {
                    _installedCache =
                        PackageManifestHelper.GetCurrentlyInstalledGooglePackages();
                    _installedCacheTime = now;
                }

                return _installedCache;
            }
        }

        [MenuItem("Dev Menu/Tools/Dependencies Downloader")]
        public static void ShowWindow()
        {
            GetWindow<DependenciesDownloaderWindow>("Dependencies Downloader");
        }

        private void OnEnable()
        {
            _downloadPath = EditorPrefs.GetString(
                DownloadPathKey,
                GetDefaultDownloadPath()
            );
            if (string.IsNullOrWhiteSpace(_downloadPath))
            {
                _downloadPath = GetDefaultDownloadPath();
            }

            _archive = GoogleArchiveParser.GetCachedArchive();
            if (_archive?.Packages != null && _archive.Packages.Count > 0)
            {
                RebuildCategories();
            }
            else
            {
                _ = FetchGoogleArchiveAsync();
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

            RefreshGitHubInstalledVersions();
            minSize = new Vector2(750f, 500f);
        }

        private void OnDisable()
        {
            EditorUtility.ClearProgressBar();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4f);
            DrawStatusBox();
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(_isCommitting))
            {
                DrawBody();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── Toolbar ──────────────────────────────────────────

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var isBusy = _isFetching || _isCommitting;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isBusy))
                    {
                        var refreshLabel = _activeTab == Tab.Google
                            ? "Refresh Google Registry"
                            : "Refresh Releases";
                        if (GUILayout.Button(
                                refreshLabel,
                                GUILayout.Width(155f)
                            ))
                        {
                            FetchActiveTabAsync();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        "Download folder",
                        GUILayout.Width(100f)
                    );

                    using (new EditorGUI.DisabledScope(_isCommitting))
                    {
                        EditorGUI.BeginChangeCheck();
                        _downloadPath = EditorGUILayout.TextField(
                            _downloadPath ?? string.Empty
                        );
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorPrefs.SetString(
                                DownloadPathKey,
                                _downloadPath
                            );
                        }

                        if (GUILayout.Button("Browse", GUILayout.Width(60f)))
                        {
                            var initialPath =
                                PackageManifestHelper.IsPathInsideProject(
                                    _downloadPath
                                )
                                    ? _downloadPath
                                    : GetDefaultDownloadPath();
                            var chosen = EditorUtility.OpenFolderPanel(
                                "Choose Package Download Folder",
                                initialPath,
                                string.Empty
                            );
                            if (!string.IsNullOrEmpty(chosen))
                            {
                                _downloadPath = chosen;
                                EditorPrefs.SetString(
                                    DownloadPathKey,
                                    _downloadPath
                                );
                            }
                        }

                        if (GUILayout.Button("Reset", GUILayout.Width(50f)))
                        {
                            _downloadPath = GetDefaultDownloadPath();
                            EditorPrefs.SetString(
                                DownloadPathKey,
                                _downloadPath
                            );
                        }
                    }
                }

                if (!PackageManifestHelper.IsPathInsideProject(_downloadPath))
                {
                    EditorGUILayout.HelpBox(
                        "Choose a valid folder inside this Unity project. " +
                        "Local package links cannot target an external folder.",
                        MessageType.Warning
                    );
                }
            }
        }

        // ── Status ───────────────────────────────────────────

        private void DrawStatusBox()
        {
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        // ═══════════════════════════════════════════════════════════
        // ── Body ─────────────────────────────────────────────

        private void DrawBody()
        {
            DrawTabBar();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(
                    GUILayout.Width(Mathf.Max(position.width * 0.58f, 320f))))
                {
                    switch (_activeTab)
                    {
                        case Tab.Google: DrawGoogleTab(); break;
                        case Tab.Adjust:
                            DrawGitHubTab(
                                _adjustCache,
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
                                "AppMetrica SDK",
                                ref _appMetricaAction,
                                ref _appMetricaVersionIdx,
                                _appMetricaInstalledVer,
                                false,
                                ref _appMetricaNotesScroll
                            );
                            break;
                    }
                }

                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.VerticalScope(
                    GUILayout.ExpandWidth(true)))
                {
                    DrawQueueSummary();
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
                "AppMetrica"
            };
            var tabWidth = (position.width - 20f) / tabNames.Length;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                for (int i = 0; i < tabNames.Length; i++)
                {
                    var style = i == (int)_activeTab
                        ? EditorStyles.toolbarButton
                        : EditorStyles.toolbarButton;

                    GUI.backgroundColor = i == (int)_activeTab
                        ? new Color(0.4f, 0.6f, 0.9f, 0.6f)
                        : Color.white;

                    if (GUILayout.Button(tabNames[i], style,
                        GUILayout.Width(tabWidth)))
                    {
                        _activeTab = (Tab)i;
                        Repaint();
                    }

                    GUI.backgroundColor = Color.white;
                }
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

            var packages = GetFilteredPackages();

            DrawGoogleFilterBar();
            DrawGooglePackageList(packages);
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
            var checkState = (isInstalled && !isQueuedRemove) || isQueuedInstall;

            var installedVersion = isInstalled
                ? ExtractVersionFromManifestEntry(installed[pkg.Name])
                : string.Empty;

            if (!isQueuedRemove && isInstalled && !isQueuedInstall &&
                _packageVersions.TryGetValue(pkg.Name, out var savedVer) &&
                !string.IsNullOrEmpty(installedVersion) &&
                savedVer != installedVersion)
            {
                _queuedInstalls.Add(pkg.Name);
                isQueuedInstall = true;
            }

            var currentVer = isQueuedInstall &&
                             _packageVersions.TryGetValue(pkg.Name, out var cv)
                ? cv
                : isInstalled ? installedVersion : string.Empty;

            var bgColor = GUI.backgroundColor;
            if (isQueuedRemove)
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.3f);
            else if (isQueuedInstall)
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f, 0.3f);
            else if (isInstalled)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f, 0.25f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = bgColor;

                EditorGUI.BeginChangeCheck();
                var newCheck = EditorGUILayout.Toggle(
                    checkState, GUILayout.Width(16f));
                if (EditorGUI.EndChangeCheck())
                    GoogleTogglePackage(pkg, newCheck, installed);

                var label = pkg.DisplayName;
                if (isQueuedRemove)
                    label += " (will be removed)";
                else if (isQueuedInstall)
                {
                    var oldStr = !string.IsNullOrEmpty(installedVersion)
                        ? installedVersion : "none";
                    label += $" ({oldStr} -> {currentVer})";
                }
                else if (isInstalled)
                    label += $" ({installedVersion})";

                EditorGUILayout.LabelField(
                    new GUIContent(label, pkg.Description),
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    pkg.Name, EditorStyles.miniLabel, GUILayout.Width(200f));

                DrawGoogleVersionPopup(pkg, checkState,
                    isInstalled, installedVersion);
            }
        }

        private void DrawGoogleVersionPopup(
            GooglePackageInfo pkg, bool checkState,
            bool isInstalled, string installedVersion)
        {
            if (pkg.Versions == null || pkg.Versions.Count == 0) return;
            var latest = pkg.Versions[0].Version;

            if (checkState)
            {
                if (!_packageVersions.TryGetValue(pkg.Name, out var selVer) ||
                    pkg.Versions.All(v2 => v2.Version != selVer))
                {
                    selVer = !string.IsNullOrEmpty(installedVersion) &&
                             pkg.Versions.Any(v2 => v2.Version == installedVersion)
                        ? installedVersion : latest;
                    _packageVersions[pkg.Name] = selVer;
                }

                var names = pkg.Versions.Select(v2 => v2.Version).ToArray();
                var idx = Array.IndexOf(names, selVer);
                if (idx < 0) idx = 0;

                EditorGUI.BeginChangeCheck();
                var ni = EditorGUILayout.Popup(idx, names, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck() && ni >= 0)
                    _packageVersions[pkg.Name] = names[ni];
            }
            else
            {
                var displayVer = isInstalled && !string.IsNullOrEmpty(installedVersion)
                    ? installedVersion : latest;
                var names = pkg.Versions.Select(v2 => v2.Version).ToArray();
                var idx = Array.IndexOf(names, displayVer);
                if (idx < 0) idx = 0;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup(idx, names, GUILayout.Width(90f));
            }
        }

        private void GoogleTogglePackage(
            GooglePackageInfo pkg, bool check,
            Dictionary<string, string> installed)
        {
            var isInstalled = installed.ContainsKey(pkg.Name);
            _queuedInstalls.Remove(pkg.Name);
            _queuedRemovals.Remove(pkg.Name);

            if (isInstalled && !check)
            {
                _queuedRemovals.Add(pkg.Name);
                _packageVersions.Remove(pkg.Name);
            }
            else if (!isInstalled && check)
            {
                _queuedInstalls.Add(pkg.Name);
                if (!_packageVersions.ContainsKey(pkg.Name) &&
                    pkg.Versions != null && pkg.Versions.Count > 0)
                    _packageVersions[pkg.Name] = pkg.Versions[0].Version;
            }
            else if (isInstalled && check)
            {
                var instVer = installed.TryGetValue(pkg.Name, out var e)
                    ? ExtractVersionFromManifestEntry(e) : string.Empty;
                if (!string.IsNullOrEmpty(instVer) &&
                    pkg.Versions != null && pkg.Versions.Count > 0)
                    _packageVersions[pkg.Name] = instVer;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── GitHub tab (shared for Adjust & AppLovin) ───────

        private void DrawGitHubTab(
            GitHubReleaseCache cache,
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
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField(
                    "No release data. Click Refresh Registry to fetch.",
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
                action = GitHubAction.None;
            }

            var versionNames = releases.Select(r => r.Version).ToArray();
            EditorGUI.BeginChangeCheck();
            versionIdx = EditorGUILayout.Popup(
                "Version", versionIdx, versionNames);
            if (EditorGUI.EndChangeCheck())
            {
                if (action == GitHubAction.Install)
                    action = GitHubAction.None;
                selected = releases[versionIdx];
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
                        $"Installed: {installedVer} -> {selected.Version}",
                        EditorStyles.miniLabel);
                else if (installedVer == selected.Version)
                    EditorGUILayout.LabelField(
                        $"Installed: {installedVer} (up to date)",
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

            // action buttons
            using (new EditorGUI.DisabledScope(managedByUpm))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (action == GitHubAction.None)
                {
                    if (string.IsNullOrEmpty(installedVer) ||
                        installedVer != selected.Version)
                    {
                        var label = !string.IsNullOrEmpty(installedVer)
                            ? $"Upgrade to {selected.Version}"
                            : $"Install {selected.Version}";
                        if (GUILayout.Button(label))
                            action = GitHubAction.Install;
                    }

                    if (!string.IsNullOrEmpty(installedVer) &&
                        GUILayout.Button("Remove", GUILayout.Width(100f)))
                    {
                        action = GitHubAction.Remove;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(
                        action == GitHubAction.Install
                            ? $"Queued: install {selected.Version}"
                            : "Queued: remove",
                        EditorStyles.miniLabel);

                    if (GUILayout.Button("Cancel", GUILayout.Width(70f)))
                    {
                        action = GitHubAction.None;
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
            _summaryScroll = EditorGUILayout.BeginScrollView(
                _summaryScroll, GUILayout.ExpandHeight(true));

            var isPathValid =
                PackageManifestHelper.TryGetPathInsideProject(
                    _downloadPath,
                    out var downloadPath
                );

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Changes Pending", EditorStyles.boldLabel);

            // install section
            EditorGUILayout.LabelField("Install", EditorStyles.boldLabel);
            var installCount = 0;

            // Google installs
            if (_queuedInstalls.Count > 0)
            {
                foreach (var name in _queuedInstalls)
                {
                    var ver = _packageVersions.TryGetValue(name, out var v)
                        ? v : "?";
                    EditorGUILayout.LabelField(
                        $"  + {name} {ver}", EditorStyles.miniLabel);
                    installCount++;
                }

                var allResolved = ResolveAllEntries();
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
                var r = _adjustCache?.Releases;
                var v = r != null && _adjustVersionIdx >= 0 &&
                        _adjustVersionIdx < r.Count
                    ? r[_adjustVersionIdx].Version : "?";
                EditorGUILayout.LabelField(
                    $"  + Adjust SDK {v}", EditorStyles.miniLabel);
                installCount++;
            }

            if (_applovinAction == GitHubAction.Install)
            {
                var r = _applovinCache?.Releases;
                var v = r != null && _applovinVersionIdx >= 0 &&
                        _applovinVersionIdx < r.Count
                    ? r[_applovinVersionIdx].Version : "?";
                EditorGUILayout.LabelField(
                    $"  + AppLovin MAX {v}", EditorStyles.miniLabel);
                installCount++;
            }

            if (_appMetricaAction == GitHubAction.Install)
            {
                var r = _appMetricaCache?.Releases;
                var v = r != null && _appMetricaVersionIdx >= 0 &&
                        _appMetricaVersionIdx < r.Count
                    ? r[_appMetricaVersionIdx].Version : "?";
                EditorGUILayout.LabelField(
                    $"  + AppMetrica SDK {v}", EditorStyles.miniLabel);
                installCount++;
            }

            if (installCount == 0)
                EditorGUILayout.LabelField(
                    "  (none)", EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);

            // remove section
            EditorGUILayout.LabelField("Remove", EditorStyles.boldLabel);
            var removeCount = 0;

            foreach (var name in _queuedRemovals)
            {
                EditorGUILayout.LabelField(
                    $"  - {name}", EditorStyles.miniLabel);
                removeCount++;
            }

            if (_adjustAction == GitHubAction.Remove)
            {
                EditorGUILayout.LabelField(
                    "  - Adjust SDK", EditorStyles.miniLabel);
                removeCount++;
            }

            if (_applovinAction == GitHubAction.Remove)
            {
                EditorGUILayout.LabelField(
                    "  - AppLovin MAX", EditorStyles.miniLabel);
                removeCount++;
            }

            if (_appMetricaAction == GitHubAction.Remove)
            {
                EditorGUILayout.LabelField(
                    "  - AppMetrica SDK", EditorStyles.miniLabel);
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

            EditorGUILayout.Space(8f);

            var queuedChanges = _queuedInstalls.Count +
                                _queuedRemovals.Count +
                                (_adjustAction == GitHubAction.None ? 0 : 1) +
                                (_applovinAction == GitHubAction.None ? 0 : 1) +
                                 (_appMetricaAction == GitHubAction.None ? 0 : 1);
            var canCommit = queuedChanges > 0 && !_isCommitting &&
                            !_isFetching && isPathValid &&
                            string.IsNullOrEmpty(
                                _dependencyConflictMessage
                            );

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canCommit))
                {
                    if (GUILayout.Button(
                            $"Apply {queuedChanges} Queued Change(s)",
                            GUILayout.Height(34f)
                        ))
                    {
                        CommitAsync(downloadPath);
                    }
                }

                using (new EditorGUI.DisabledScope(
                           queuedChanges == 0 || _isCommitting
                       ))
                {
                    if (GUILayout.Button(
                            "Discard Queue",
                            GUILayout.Width(105f),
                            GUILayout.Height(34f)
                        ))
                    {
                        ClearQueue();
                        SetStatus("Queued changes discarded.", MessageType.Info);
                    }
                }
            }

            if (!isPathValid)
                EditorGUILayout.HelpBox(
                    "Download path must be inside the project folder.",
                    MessageType.Warning);

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
            GUI.contentColor = new Color(0.42f, 0.8f, 0.5f);
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
                    $"    ✓ {dependency.PackageName} — {versionLabel}" +
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
            if (_archive == null || _queuedInstalls.Count == 0)
                return new List<DownloadEntry>();

            var installed = InstalledPackages;
            var effectiveInstalled =
                new Dictionary<string, string>(installed);
            foreach (var packageName in _queuedRemovals)
                effectiveInstalled.Remove(packageName);
            foreach (var packageName in _queuedInstalls)
                effectiveInstalled.Remove(packageName);

            var all = new Dictionary<string, DownloadEntry>();
            foreach (var pkgName in _queuedInstalls.OrderBy(
                         name => name,
                         StringComparer.Ordinal
                     ))
            {
                var version =
                    _packageVersions.TryGetValue(pkgName, out var v) ? v : "?";
                var dependencyTree = GoogleDependencyResolver
                    .ResolveFullDependencyTree(
                        pkgName,
                        version,
                        _archive
                    );
                RecordInstalledDependencies(
                    dependencyTree,
                    effectiveInstalled
                );

                var entries = GoogleDependencyResolver
                    .ResolveFullDependencyTree(
                        pkgName,
                        version,
                        _archive,
                        effectiveInstalled
                    );
                foreach (var entry in entries)
                {
                    if (!all.TryGetValue(
                            entry.PackageName,
                            out var existing
                        ))
                    {
                        all[entry.PackageName] = entry;
                        continue;
                    }

                    if (existing.Version != entry.Version)
                    {
                        _dependencyConflictMessage =
                            $"Dependency conflict: {entry.PackageName} is " +
                            $"required as both {existing.Version} and " +
                            $"{entry.Version}. Adjust the queued package " +
                            "versions before applying changes.";
                    }
                }
            }

            var result = new List<DownloadEntry>(all.Values);
            result.Sort((a, b) =>
                string.CompareOrdinal(a.PackageName, b.PackageName));
            return result;
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
            }
        }

        private async Task FetchGoogleArchiveAsync()
        {
            _isFetching = true;
            SetStatus("Fetching Google package registry...", MessageType.Info);
            try
            {
                var result = await GoogleArchiveParser.FetchAndParseArchiveAsync();
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
                EditorUtility.ClearProgressBar();
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
                    delivery
                );

                if (owner == AdjustRepoOwner)
                {
                    _adjustCache = cache;
                    _adjustVersionIdx = 0;
                }
                else if (owner == AppLovinRepoOwner)
                {
                    _applovinCache = cache;
                    _applovinVersionIdx = 0;
                }
                else
                {
                    _appMetricaCache = cache;
                    _appMetricaVersionIdx = 0;
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

            _adjustManagedByUpm = !string.IsNullOrEmpty(adjustUpmVersion);
            _applovinManagedByUpm = !string.IsNullOrEmpty(applovinUpmVersion);

            if (_adjustManagedByUpm)
                UnityPackageTracker.DeleteTrackingFile("Adjust");
            if (_applovinManagedByUpm)
                UnityPackageTracker.DeleteTrackingFile("AppLovin");

            var adjustRecord =
                UnityPackageTracker.GetInstallRecord("Adjust");
            var applovinRecord =
                UnityPackageTracker.GetInstallRecord("AppLovin");

            _adjustInstalledVer = _adjustManagedByUpm
                ? adjustUpmVersion
                : adjustRecord?.Version ?? string.Empty;
            _applovinInstalledVer = _applovinManagedByUpm
                ? applovinUpmVersion
                : applovinRecord?.Version ?? string.Empty;
            _appMetricaInstalledVer = ExtractVersionFromManifestEntry(
                appMetricaManifestValue
            );

            if (_adjustManagedByUpm) _adjustAction = GitHubAction.None;
            if (_applovinManagedByUpm) _applovinAction = GitHubAction.None;
        }

        // ═══════════════════════════════════════════════════════════
        // ── Commit ───────────────────────────────────────────

        private async void CommitAsync(string downloadPath)
        {
            var confirmed = EditorUtility.DisplayDialog(
                "Apply Dependency Changes",
                "Downloads are validated before project files are changed. " +
                "Unity may recompile after packages are applied. Continue?",
                "Apply Changes",
                "Cancel"
            );
            if (!confirmed) return;

            _isCommitting = true;
            _commitWarnings.Clear();
            try
            {
                if (!PackageManifestHelper.TryGetPathInsideProject(
                        downloadPath,
                        out var normalizedDownloadPath
                    ))
                {
                    SetStatus(
                        "Download path must be inside the project folder.",
                        MessageType.Error
                    );
                    return;
                }

                Directory.CreateDirectory(normalizedDownloadPath);

                if (!await CommitInstallsAsync(normalizedDownloadPath))
                {
                    return;
                }

                if (!CommitRemovals())
                {
                    return;
                }

                ClearQueue();
                _installedCache = null;
                RefreshGitHubInstalledVersions();
                if (_commitWarnings.Count > 0)
                {
                    SetStatus(
                        "Changes were applied with warnings: " +
                        string.Join(" ", _commitWarnings),
                        MessageType.Warning
                    );
                }
                else
                {
                    SetStatus(
                        "All queued changes were applied.",
                        MessageType.Info
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus(
                    $"Could not apply changes: {ex.Message}",
                    MessageType.Error
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isCommitting = false;
                Repaint();
            }
        }

        private bool CommitRemovals()
        {
            var totalRemovals =
                (_adjustAction == GitHubAction.Remove ? 1 : 0) +
                (_applovinAction == GitHubAction.Remove ? 1 : 0);
            if (totalRemovals == 0) return true;

            SetStatus(
                $"Removing {totalRemovals} package(s)...",
                MessageType.Info
            );

            if (_adjustAction == GitHubAction.Remove)
            {
                if (!UnityPackageTracker.RemoveTrackedFiles("Adjust"))
                {
                    SetStatus(
                        "Adjust removal failed; its tracking record was preserved.",
                        MessageType.Error
                    );
                    return false;
                }

                _adjustAction = GitHubAction.None;
            }

            if (_applovinAction == GitHubAction.Remove)
            {
                if (!UnityPackageTracker.RemoveTrackedFiles("AppLovin"))
                {
                    SetStatus(
                        "AppLovin removal failed; its tracking record was preserved.",
                        MessageType.Error
                    );
                    return false;
                }

                _applovinAction = GitHubAction.None;
            }

            return true;
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
                _adjustCache,
                _adjustVersionIdx,
                "Adjust"
            );
            AddGitHubInstallEntry(
                allEntries,
                _applovinAction,
                _applovinCache,
                _applovinVersionIdx,
                "AppLovin"
            );

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

            if (allEntries.Count == 0)
            {
                if (!PackageManifestHelper.ApplyTarballChanges(
                        new List<DownloadEntry>(),
                        manifestRemovals,
                        downloadPath,
                        gitAdditions
                    ))
                {
                    return false;
                }

                _appMetricaAction = GitHubAction.None;
                return true;
            }

            SetStatus(
                $"Downloading {allEntries.Count} package(s)...",
                MessageType.Info
            );

            var packageFiles = new Dictionary<string, string>();
            for (int i = 0; i < allEntries.Count; i++)
            {
                var entry = allEntries[i];
                if (string.IsNullOrWhiteSpace(entry.TarballUrl))
                {
                    SetStatus(
                        $"No download URL is available for {entry.PackageName} " +
                        $"{entry.Version}.",
                        MessageType.Error
                    );
                    return false;
                }

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Downloading Dependencies",
                        $"({i + 1}/{allEntries.Count}) " +
                        $"{entry.PackageName} {entry.Version}",
                        (float)i / allEntries.Count
                    ))
                {
                    SetStatus(
                        "Download canceled. The queue was preserved.",
                        MessageType.Warning
                    );
                    return false;
                }

                var filePath = Path.Combine(
                    downloadPath,
                    GetDownloadFileName(entry)
                );
                packageFiles[GetEntryKey(entry)] = filePath;

                if (!IsDownloadedFileUsable(entry, filePath) &&
                    !await DownloadFileAsync(entry.TarballUrl, filePath))
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
            }

            EditorUtility.ClearProgressBar();

            var googleEntries = allEntries.Where(
                entry => !IsUnityPackageEntry(entry)
            ).ToList();
            var unityPackageEntries = allEntries.Where(
                IsUnityPackageEntry
            ).ToList();

            foreach (var entry in unityPackageEntries)
            {
                var filePath = packageFiles[GetEntryKey(entry)];
                var trackedFiles =
                    UnityPackageTracker.EnumerateFilesInPackage(filePath);
                if (trackedFiles.Count == 0)
                {
                    SetStatus(
                        $"Could not inspect {entry.PackageName}'s " +
                        ".unitypackage; import was not started.",
                        MessageType.Error
                    );
                    return false;
                }

                var previousRecord =
                    UnityPackageTracker.GetInstallRecord(entry.PackageName);

                SetStatus(
                    $"Importing {entry.PackageName} {entry.Version}...",
                    MessageType.Info
                );
                var importError = await ImportUnityPackageAsync(filePath);
                if (!string.IsNullOrEmpty(importError))
                {
                    SetStatus(
                        $"{entry.PackageName} import failed: {importError}",
                        MessageType.Error
                    );
                    return false;
                }

                var recordFiles = new List<string>(trackedFiles);
                if (previousRecord != null &&
                    !UnityPackageTracker.RemoveTrackedFiles(
                        entry.PackageName,
                        trackedFiles
                    ))
                {
                    var comparer = Path.DirectorySeparatorChar == '\\'
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal;
                    var mergedPaths = new HashSet<string>(
                        recordFiles,
                        comparer
                    );
                    foreach (var oldPath in previousRecord.TrackedFiles)
                    {
                        if (mergedPaths.Add(oldPath))
                            recordFiles.Add(oldPath);
                    }

                    _commitWarnings.Add(
                        $"Some stale {entry.PackageName} files could not be " +
                        "removed and remain tracked for a later cleanup."
                    );
                }

                if (!UnityPackageTracker.SaveInstallRecord(
                        entry.PackageName,
                        entry.Version,
                        recordFiles
                    ))
                {
                    SetStatus(
                        $"{entry.PackageName} imported, but its install " +
                        "record could not be saved.",
                        MessageType.Error
                    );
                    return false;
                }

                if (entry.PackageName == "Adjust")
                    _adjustAction = GitHubAction.None;
                else
                    _applovinAction = GitHubAction.None;
            }

            if (!PackageManifestHelper.ApplyTarballChanges(
                    googleEntries,
                    manifestRemovals,
                    downloadPath,
                    gitAdditions
                ))
            {
                SetStatus(
                    "Downloaded Google packages, but manifest.json could " +
                    "not be updated. The queue was preserved.",
                    MessageType.Error
                );
                return false;
            }

            _appMetricaAction = GitHubAction.None;
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
        }

        private static async Task<bool> DownloadFileAsync(
            string url,
            string filePath)
        {
            var temporaryPath = filePath + ".download";
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

                using var request = UnityWebRequest.Get(url);
                request.timeout = 120;
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[DependenciesDownloader] Download failed for " +
                        $"'{url}': {request.error}"
                    );
                    return false;
                }

                var data = request.downloadHandler.data;
                if (data == null || data.Length == 0)
                {
                    Debug.LogError(
                        $"[DependenciesDownloader] Download returned no data " +
                        $"for '{url}'."
                    );
                    return false;
                }

                File.WriteAllBytes(temporaryPath, data);
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(temporaryPath, filePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Download exception for " +
                    $"'{url}': {ex.Message}"
                );
                return false;
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
            gitUrl = string.Empty;
            var releases = _appMetricaCache?.Releases;
            if (releases == null || _appMetricaVersionIdx < 0 ||
                _appMetricaVersionIdx >= releases.Count)
            {
                return false;
            }

            gitUrl = releases[_appMetricaVersionIdx].GitUrl;
            return !string.IsNullOrWhiteSpace(gitUrl);
        }

        private static void AddGitHubInstallEntry(
            List<DownloadEntry> entries,
            GitHubAction action,
            GitHubReleaseCache cache,
            int versionIndex,
            string packageName)
        {
            if (action != GitHubAction.Install ||
                cache?.Releases == null ||
                versionIndex < 0 ||
                versionIndex >= cache.Releases.Count)
            {
                return;
            }

            var release = cache.Releases[versionIndex];
            entries.Add(new DownloadEntry
            {
                PackageName = packageName,
                Version = release.Version,
                TarballUrl = release.DownloadUrl,
                IsTransitive = false
            });
        }

        private static string GetDownloadFileName(DownloadEntry entry)
        {
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
                   entry.PackageName == "AppLovin";
        }

        private static bool IsDownloadedFileUsable(
            DownloadEntry entry,
            string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 2) return false;

                using var stream = File.OpenRead(filePath);
                if (stream.ReadByte() != 0x1F || stream.ReadByte() != 0x8B)
                {
                    return false;
                }

                return !IsUnityPackageEntry(entry) ||
                       UnityPackageTracker
                           .EnumerateFilesInPackage(filePath)
                           .Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<string> ImportUnityPackageAsync(
            string filePath)
        {
            var completion = new TaskCompletionSource<string>();

            string NormalizePackageName(string value)
            {
                const string packageExtension = ".unitypackage";
                var fileName = Path.GetFileName(value ?? string.Empty);
                return fileName.EndsWith(
                    packageExtension,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? fileName.Substring(
                        0,
                        fileName.Length - packageExtension.Length
                    )
                    : fileName;
            }

            var expectedPackageName = NormalizePackageName(filePath);

            bool IsExpectedPackage(string packageName)
            {
                return string.Equals(
                    NormalizePackageName(packageName),
                    expectedPackageName,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            void HandleCompleted(string packageName)
            {
                if (IsExpectedPackage(packageName))
                    completion.TrySetResult(string.Empty);
            }

            void HandleCancelled(string packageName)
            {
                if (IsExpectedPackage(packageName))
                    completion.TrySetResult("Import was canceled.");
            }

            void HandleFailed(string packageName, string error)
            {
                if (!IsExpectedPackage(packageName)) return;
                completion.TrySetResult(
                    string.IsNullOrWhiteSpace(error)
                        ? "Unity reported an unknown import error."
                        : error
                );
            }

            AssetDatabase.importPackageCompleted += HandleCompleted;
            AssetDatabase.importPackageCancelled += HandleCancelled;
            AssetDatabase.importPackageFailed += HandleFailed;

            try
            {
                AssetDatabase.ImportPackage(filePath, interactive: false);
                return await completion.Task;
            }
            finally
            {
                AssetDatabase.importPackageCompleted -= HandleCompleted;
                AssetDatabase.importPackageCancelled -= HandleCancelled;
                AssetDatabase.importPackageFailed -= HandleFailed;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ── Helpers ──────────────────────────────────────────

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            Repaint();
        }

        private static string ExtractVersionFromManifestEntry(
            string manifestValue)
        {
            if (string.IsNullOrEmpty(manifestValue)) return string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(
                manifestValue,
                @"(?<!\d)(\d+\.\d+\.\d+(?:\.\d+)?)(?!\d)"
            );
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
