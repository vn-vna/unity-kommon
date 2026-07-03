using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class DependenciesDownloaderWindow : EditorWindow
    {
        private const string DownloadPathKey = "Scheherazade.DepsDownloader.DownloadPath";
        private const string DefaultDownloadDir = "LocalPackages";
        private const double AutoFetchCooldownSeconds = 30.0;

        private static double s_lastAutoFetchTime;

        // ── Shared state ────────────────────────────────────

        private bool _isFetching;
        private bool _isCommitting;
        private string _statusMessage = "Ready.";
        private MessageType _statusType = MessageType.Info;

        private Vector2 _summaryScroll;

        // ── Tab state ───────────────────────────────────────

        private enum Tab { Google = 0, Adjust = 1, AppLovin = 2 }

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

        private const string AdjustRepoOwner = "adjust";
        private const string AdjustRepoName = "unity_sdk";
        private const string AppLovinRepoOwner = "AppLovin";
        private const string AppLovinRepoName = "AppLovin-MAX-Unity-Plugin";

        private GitHubReleaseCache _adjustCache;
        private GitHubReleaseCache _applovinCache;
        private GitHubAction _adjustAction = GitHubAction.None;
        private GitHubAction _applovinAction = GitHubAction.None;
        private int _adjustVersionIdx;
        private int _applovinVersionIdx;
        private string _adjustInstalledVer;
        private string _applovinInstalledVer;

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

        // ── Auto-refresh ─────────────────────────────────────

        static DependenciesDownloaderWindow()
        {
#if UNITY_2020_3_OR_NEWER
            PlayerSettings.insecureHttpOption =
                InsecureHttpOption.AlwaysAllowed;
#endif
            AssemblyReloadEvents.afterAssemblyReload += TryAutoFetch;
        }

        private static void TryAutoFetch()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - s_lastAutoFetchTime < AutoFetchCooldownSeconds) return;
            s_lastAutoFetchTime = now;

            EditorApplication.delayCall += () =>
            {
                if (GoogleArchiveParser.GetCachedArchive() == null)
                    GoogleArchiveParser.FetchAndParseArchiveAsync();
            };
        }

        [MenuItem("Dev Menu/Tools/Dependencies Downloader")]
        public static void ShowWindow()
        {
            GetWindow<DependenciesDownloaderWindow>("Dependencies Downloader");
        }

        private void OnEnable()
        {
            _archive = GoogleArchiveParser.GetCachedArchive();
            if (_archive?.Packages != null) RebuildCategories();
            else TryAutoFetch();

            _adjustCache = GitHubReleaseFetcher.GetCachedRelease(
                AdjustRepoOwner, AdjustRepoName);
            _applovinCache = GitHubReleaseFetcher.GetCachedRelease(
                AppLovinRepoOwner, AppLovinRepoName);

            RefreshGitHubInstalledVersions();
            minSize = new Vector2(750f, 500f);
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4f);
            DrawStatusBox();
            EditorGUILayout.Space(4f);
            DrawBody();
        }

        // ═══════════════════════════════════════════════════════════
        // ── Toolbar ──────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var downloadPath =
                EditorPrefs.GetString(DownloadPathKey, string.Empty);
            if (string.IsNullOrEmpty(downloadPath))
                downloadPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", DefaultDownloadDir));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_isFetching))
                {
                    if (GUILayout.Button("Refresh Registry",
                        GUILayout.Width(120f)))
                        FetchActiveTabAsync();
                }

                GUILayout.FlexibleSpace();

                var isPathValid =
                    PackageManifestHelper.IsPathInsideProject(downloadPath);
                var pathLabel = isPathValid ? "Download:" : "Download (!):";
                EditorGUILayout.LabelField(pathLabel, GUILayout.Width(70f));
                EditorGUI.BeginChangeCheck();
                downloadPath = EditorGUILayout.TextField(downloadPath);
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(DownloadPathKey, downloadPath);

                if (GUILayout.Button("Browse", GUILayout.Width(60f)))
                {
                    var chosen = EditorUtility.OpenFolderPanel(
                        "Download Path", downloadPath, string.Empty);
                    if (!string.IsNullOrEmpty(chosen))
                        EditorPrefs.SetString(DownloadPathKey, chosen);
                }
            }

            if (!PackageManifestHelper.IsPathInsideProject(downloadPath))
                EditorGUILayout.HelpBox(
                    "Download path must be inside the project folder " +
                    "for file:../ relative linking.",
                    MessageType.Warning);

            EditorGUILayout.EndVertical();
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
                                _adjustCache, "Adjust SDK",
                                AdjustRepoOwner, AdjustRepoName,
                                ref _adjustAction, ref _adjustVersionIdx,
                                _adjustInstalledVer,
                                cacheRef: ref _adjustCache);
                            break;
                        case Tab.AppLovin:
                            DrawGitHubTab(
                                _applovinCache, "AppLovin MAX",
                                AppLovinRepoOwner, AppLovinRepoName,
                                ref _applovinAction, ref _applovinVersionIdx,
                                _applovinInstalledVer,
                                cacheRef: ref _applovinCache);
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
            var tabNames = new[] { "Google Packages", "Adjust", "AppLovin MAX" };
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
            string repoOwner,
            string repoName,
            ref GitHubAction action,
            ref int versionIdx,
            string installedVer,
            ref GitHubReleaseCache cacheRef)
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

            var versionNames = releases.Select(r => r.Version).ToArray();
            EditorGUI.BeginChangeCheck();
            versionIdx = EditorGUILayout.Popup(
                "Version", versionIdx, versionNames);
            if (EditorGUI.EndChangeCheck())
            {
                if (action == GitHubAction.Install)
                    action = GitHubAction.None;
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
            using (new EditorGUILayout.HorizontalScope())
            {
                if (action == GitHubAction.None)
                {
                    if (!string.IsNullOrEmpty(installedVer) &&
                        installedVer == selected.Version)
                    {
                        if (GUILayout.Button("Remove", GUILayout.Width(100f)))
                            action = GitHubAction.Remove;
                    }
                    else
                    {
                        var label = !string.IsNullOrEmpty(installedVer)
                            ? $"Upgrade to {selected.Version}"
                            : $"Install {selected.Version}";
                        if (GUILayout.Button(label))
                            action = GitHubAction.Install;
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
            var notesScroll = EditorGUILayout.BeginScrollView(
                Vector2.zero, GUILayout.ExpandHeight(true));
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

            var downloadPath =
                EditorPrefs.GetString(DownloadPathKey, string.Empty);
            if (string.IsNullOrEmpty(downloadPath))
                downloadPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", DefaultDownloadDir));

            var isPathValid =
                PackageManifestHelper.IsPathInsideProject(downloadPath);

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

            if (removeCount == 0)
                EditorGUILayout.LabelField(
                    "  (none)", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);

            var totalChanges = installCount + removeCount;
            var canCommit = totalChanges > 0 && !_isCommitting &&
                            !_isFetching && isPathValid;

            using (new EditorGUI.DisabledScope(!canCommit))
            {
                if (GUILayout.Button($"Commit Changes ({totalChanges})",
                    GUILayout.Height(34f)))
                    CommitAsync(downloadPath);
            }

            if (!isPathValid)
                EditorGUILayout.HelpBox(
                    "Download path must be inside the project folder.",
                    MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        private List<DownloadEntry> ResolveAllEntries()
        {
            if (_archive == null || _queuedInstalls.Count == 0)
                return new List<DownloadEntry>();

            var installed = InstalledPackages;
            var effectiveInstalled =
                new Dictionary<string, string>(installed);
            foreach (var r in _queuedRemovals)
                effectiveInstalled.Remove(r);

            var all = new Dictionary<string, DownloadEntry>();
            foreach (var pkgName in _queuedInstalls)
            {
                var version =
                    _packageVersions.TryGetValue(pkgName, out var v) ? v : "?";
                var entries = GoogleDependencyResolver
                    .ResolveFullDependencyTree(
                        pkgName, version, _archive, effectiveInstalled);
                foreach (var e in entries)
                    if (!all.ContainsKey(e.PackageName))
                        all[e.PackageName] = e;
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
                        AppLovinRepoOwner, AppLovinRepoName);
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
                _isFetching = false;
                if (result?.Packages != null)
                {
                    _archive = result;
                    _installedCache = null;
                    RebuildCategories();
                    SetStatus(
                        $"Registry loaded. {_archive.Packages.Count} packages " +
                        $"across {_categories.Count - 1} categories.",
                        MessageType.Info);
                }
                else
                    SetStatus("Failed to fetch registry.", MessageType.Error);
            }
            catch (Exception ex)
            {
                _isFetching = false;
                SetStatus($"Failed: {ex.Message}", MessageType.Error);
            }

            Repaint();
        }

        private async Task FetchGitHubTabAsync(
            string owner, string name)
        {
            _isFetching = true;
            SetStatus($"Fetching releases from {owner}/{name}...",
                MessageType.Info);
            try
            {
                var cache = await GitHubReleaseFetcher.FetchReleasesAsync(
                    owner, name);
                _isFetching = false;

                if (owner == AdjustRepoOwner)
                {
                    _adjustCache = cache;
                    _adjustVersionIdx = 0;
                }
                else
                {
                    _applovinCache = cache;
                    _applovinVersionIdx = 0;
                }

                RefreshGitHubInstalledVersions();

                if (cache?.Releases != null && cache.Releases.Count > 0)
                    SetStatus(
                        $"Loaded {cache.Releases.Count} releases.",
                        MessageType.Info);
                else
                    SetStatus("Failed to fetch releases.", MessageType.Error);
            }
            catch (Exception ex)
            {
                _isFetching = false;
                SetStatus($"Failed: {ex.Message}", MessageType.Error);
            }

            Repaint();
        }

        private void RefreshGitHubInstalledVersions()
        {
            var adjustRecord =
                UnityPackageTracker.GetInstallRecord("Adjust");
            var applovinRecord =
                UnityPackageTracker.GetInstallRecord("AppLovin");
            _adjustInstalledVer = adjustRecord?.Version ?? string.Empty;
            _applovinInstalledVer = applovinRecord?.Version ?? string.Empty;
        }

        // ═══════════════════════════════════════════════════════════
        // ── Commit ───────────────────────────────────────────

        private async void CommitAsync(string downloadPath)
        {
            _isCommitting = true;

            if (!PackageManifestHelper.IsPathInsideProject(downloadPath))
            {
                _isCommitting = false;
                SetStatus("Download path must be inside the project folder.",
                    MessageType.Error);
                return;
            }

            if (!Directory.Exists(downloadPath))
            {
                try { Directory.CreateDirectory(downloadPath); }
                catch (Exception ex)
                {
                    _isCommitting = false;
                    SetStatus($"Cannot create download path: {ex.Message}",
                        MessageType.Error);
                    return;
                }
            }

            // phase 1: removals
            CommitRemovals();

            // phase 2: installs
            await CommitInstallsAsync(downloadPath);

            // phase 3: cleanup
            CommitCleanup();
            _isCommitting = false;
            _installedCache = null;
            RefreshGitHubInstalledVersions();
            Repaint();
        }

        private void CommitRemovals()
        {
            var totalRemovals = _queuedRemovals.Count +
                                (_adjustAction == GitHubAction.Remove ? 1 : 0) +
                                (_applovinAction == GitHubAction.Remove ? 1 : 0);
            if (totalRemovals == 0) return;

            SetStatus($"Removing {totalRemovals} package(s)...",
                MessageType.Info);

            // Google removals
            if (_queuedRemovals.Count > 0)
                PackageManifestHelper.RemoveTarballEntries(_queuedRemovals);

            // GitHub removals
            if (_adjustAction == GitHubAction.Remove)
            {
                UnityPackageTracker.RemoveTrackedFiles("Adjust");
                _adjustAction = GitHubAction.None;
            }

            if (_applovinAction == GitHubAction.Remove)
            {
                UnityPackageTracker.RemoveTrackedFiles("AppLovin");
                _applovinAction = GitHubAction.None;
            }
        }

        private async Task CommitInstallsAsync(string downloadPath)
        {
            // collect all install entries
            var allEntries = new List<DownloadEntry>();

            // Google installs (direct + transitive)
            var googleResolved = ResolveAllEntries();
            allEntries.AddRange(googleResolved);

            // GitHub installs — wrap as DownloadEntry
            if (_adjustAction == GitHubAction.Install &&
                _adjustCache?.Releases != null &&
                _adjustVersionIdx >= 0 &&
                _adjustVersionIdx < _adjustCache.Releases.Count)
            {
                var r = _adjustCache.Releases[_adjustVersionIdx];
                allEntries.Add(new DownloadEntry
                {
                    PackageName = "Adjust",
                    Version = r.Version,
                    TarballUrl = r.DownloadUrl,
                    IsTransitive = false
                });
            }

            if (_applovinAction == GitHubAction.Install &&
                _applovinCache?.Releases != null &&
                _applovinVersionIdx >= 0 &&
                _applovinVersionIdx < _applovinCache.Releases.Count)
            {
                var r = _applovinCache.Releases[_applovinVersionIdx];
                allEntries.Add(new DownloadEntry
                {
                    PackageName = "AppLovin",
                    Version = r.Version,
                    TarballUrl = r.DownloadUrl,
                    IsTransitive = false
                });
            }

            if (allEntries.Count == 0) return;

            SetStatus($"Downloading {allEntries.Count} package(s)...",
                MessageType.Info);

            var failed = new List<string>();
            var downloaded = new List<DownloadEntry>();

            for (int i = 0; i < allEntries.Count; i++)
            {
                var entry = allEntries[i];
                var canceled = EditorUtility.DisplayCancelableProgressBar(
                    "Committing Packages",
                    $"({i + 1}/{allEntries.Count}) " +
                    $"{entry.PackageName} {entry.Version}",
                    (float)i / allEntries.Count);

                if (canceled)
                {
                    EditorUtility.ClearProgressBar();
                    _isCommitting = false;
                    SetStatus(
                        $"Canceled after {i} downloads.",
                        MessageType.Warning);
                    Repaint();
                    return;
                }

                var fileName = entry.PackageName == "Adjust" ||
                               entry.PackageName == "AppLovin"
                    ? $"{entry.PackageName}_v{entry.Version}.unitypackage"
                    : $"{entry.PackageName}-{entry.Version}.tgz";

                var filePath = Path.Combine(downloadPath, fileName);

                if (File.Exists(filePath))
                {
                    downloaded.Add(entry);
                    continue;
                }

                var success =
                    await DownloadFileAsync(entry.TarballUrl, filePath);
                if (success) downloaded.Add(entry);
                else failed.Add(entry.PackageName);
            }

            EditorUtility.ClearProgressBar();

            if (failed.Count > 0)
                SetStatus($"Commit complete. {downloaded.Count} downloaded, " +
                          $"{failed.Count} failed: {string.Join(", ", failed)}",
                    MessageType.Warning);
            else
                SetStatus($"Downloaded {downloaded.Count} package(s).",
                    MessageType.Info);

            // install phase
            var googleDownloaded = new List<DownloadEntry>();
            foreach (var e in downloaded)
            {
                if (e.PackageName == "Adjust" || e.PackageName == "AppLovin")
                {
                    var fileName =
                        $"{e.PackageName}_v{e.Version}.unitypackage";
                    var filePath = Path.Combine(downloadPath, fileName);

                    // remove old tracked files before installing new version
                    UnityPackageTracker.RemoveTrackedFiles(e.PackageName);

                    // parse .unitypackage for tracking
                    var files =
                        UnityPackageTracker.EnumerateFilesInPackage(filePath);
                    UnityPackageTracker.SaveInstallRecord(
                        e.PackageName, e.Version, files);

                    // import interactively
                    AssetDatabase.ImportPackage(
                        filePath, interactive: true);

                    if (e.PackageName == "Adjust")
                        _adjustAction = GitHubAction.None;
                    else
                        _applovinAction = GitHubAction.None;
                }
                else
                {
                    googleDownloaded.Add(e);
                }
            }

            // Google manifest update
            if (googleDownloaded.Count > 0)
            {
                var manifestOk = PackageManifestHelper.AddTarballEntries(
                    googleDownloaded, downloadPath);
                if (manifestOk)
                {
                    var msg = $"Committed {googleDownloaded.Count} google packages.";
                    if (failed.Count > 0)
                        msg += $" {failed.Count} failed.";
                    SetStatus(msg,
                        failed.Count > 0
                            ? MessageType.Warning : MessageType.Info);
                }
                else
                    SetStatus("Manifest update failed.", MessageType.Error);
            }
        }

        private void CommitCleanup()
        {
            _queuedInstalls.Clear();
            _queuedRemovals.Clear();
            _packageVersions.Clear();
        }

        private static async Task<bool> DownloadFileAsync(
            string url, string filePath)
        {
            try
            {
                using var request = UnityWebRequest.Get(url);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[DependenciesDownloader] Download failed: " +
                        $"{request.error}");
                    return false;
                }

                File.WriteAllBytes(filePath, request.downloadHandler.data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DependenciesDownloader] Download exception: " +
                    $"{ex.Message}");
                return false;
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
                manifestValue, @"-(\d+\.\d+\.\d+(?:\.\d+)?)\.tgz");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
