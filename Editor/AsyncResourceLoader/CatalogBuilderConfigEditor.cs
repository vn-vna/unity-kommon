using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Editor;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor.Validation;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    [CustomEditor(typeof(CatalogBuilderConfig))]
    public class CatalogBuilderConfigEditor : UnityEditor.Editor
    {
        private const int DefaultPageSize = 20;
        private const int MinimumPageSize = 5;
        private const int MaximumPageSize = 500;
        private const float DropZoneHeight = 54f;
        private const float SelectionColumnWidth = 22f;
        private const float TypeColumnWidth = 64f;
        private const float StatusColumnWidth = 84f;
        private const float SourceColumnWidth = 180f;
        private const float EntriesTableMinimumWidth = 720f;
        private const float EntriesTableMaximumHeight = 420f;
        private const float CompactToolbarWidth = 440f;
        private static readonly Regex EntryIdPlaceholderRegex = new Regex(
            @"(?<!\{)\{([^{}]+)\}(?!\})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string[] TabNames =
        {
            "Configuration",
            "Entries",
            "Upload"
        };
        private static readonly string[] SupportedDataTypeNames =
        {
            "Unknown",
            "Text",
            "Binary"
        };

        private CatalogBuilderConfig _config;
        [SerializeField]
        private CatalogBuilderTab _activeTab;
        private bool _isDraggingOver;
        private string _searchFilter = string.Empty;
        private int _pageIndex;
        private int _pageSize = DefaultPageSize;
        private bool _settingsFoldout = true;
        private readonly HashSet<StagedCatalogEntry> _selectedEntries
            = new HashSet<StagedCatalogEntry>();

        // Performance caches
        private readonly Dictionary<string, CachedHash> _hashCache
            = new Dictionary<string, CachedHash>();
        private readonly Dictionary<StagedCatalogEntry, CachedValidation>
            _validationCache = new Dictionary<StagedCatalogEntry, CachedValidation>();
        private readonly Queue<StagedCatalogEntry> _validationQueue
            = new Queue<StagedCatalogEntry>();
        private readonly HashSet<StagedCatalogEntry> _queuedValidationEntries
            = new HashSet<StagedCatalogEntry>();
        private List<StagedCatalogEntry> _filteredEntries;
        private string _lastSearchFilter;
        private int _lastEntriesCount;

        // Column widths
        private float _colIdWidth = 130f;
        private float _colPathWidth = 230f;
        private Vector2 _entriesScrollPosition;
        private bool _isValidationQueued;

        // S3 upload state
        private bool _s3Foldout;
        private bool _isUploading;
        private bool _canCancelUpload;
        private bool _cancelUploadRequested;
        private float _uploadProgress;
        private string _uploadStatusMessage = string.Empty;
        private CatalogValidationResult _lastValidation;

        #region Menu

        [MenuItem("Dev Menu/Catalog/Builder")]
        public static void Open()
        {
            CatalogBuilderConfig config = Selection.activeObject
                as CatalogBuilderConfig;
            if (config == null)
            {
                config = FindFirstConfig();
            }

            if (config == null)
            {
                EditorUtility.DisplayDialog(
                    "Catalog Builder Config Missing",
                    "Create a Catalog Builder Config asset via Assets/Create/Scheherazade/"
                    + "Async Resource Loader/Catalog Builder Config.",
                    "OK"
                );
                return;
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _config = target as CatalogBuilderConfig;
            if (_config == null)
            {
                return;
            }

            EnsureConfigInitialized();
            InvalidateFilters();
            _hashCache.Clear();
            ClearValidationCache();
        }

        public override void OnInspectorGUI()
        {
            if (_config == null)
            {
                EditorGUILayout.HelpBox(
                    "Catalog Builder Config is unavailable.",
                    MessageType.Info);
                return;
            }

            PruneSelection();
            DrawHeader();
            DrawTabs();
            DrawActiveTab();
        }

        #endregion

        #region Navigation

        private void DrawTabs()
        {
            int currentTabIndex = Mathf.Clamp(
                (int)_activeTab,
                0,
                TabNames.Length - 1
            );
            CatalogBuilderTab selectedTab = (CatalogBuilderTab)GUILayout.Toolbar(
                currentTabIndex,
                TabNames
            );
            if (selectedTab == _activeTab)
            {
                return;
            }

            _activeTab = selectedTab;
            _isDraggingOver = false;
        }

        private void DrawActiveTab()
        {
            EditorGUILayout.Space(6f);
            switch (_activeTab)
            {
                case CatalogBuilderTab.Configuration:
                    DrawCatalogSettings();
                    break;

                case CatalogBuilderTab.Entries:
                    DrawDropZone();
                    EditorGUILayout.Space(6f);
                    DrawStagedEntries();
                    break;

                case CatalogBuilderTab.Upload:
                    DrawActions();
                    DrawS3Section();
                    break;
            }
        }

        #endregion

        private void EnsureConfigInitialized()
        {
            bool changed = false;
            if (_config.Entries == null)
            {
                _config.Entries = new List<StagedCatalogEntry>();
                changed = true;
            }

            if (_config.LastGenerated == null)
            {
                _config.LastGenerated = new CatalogBuildState();
                changed = true;
            }

            if (_config.LastUploaded == null)
            {
                _config.LastUploaded = new CatalogBuildState();
                changed = true;
            }

            if (_config.S3 == null)
            {
                _config.S3 = new S3UploadSettings();
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(_config);
            }
        }

        private static CatalogBuilderConfig FindFirstConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:CatalogBuilderConfig");
            if (guids.Length == 0)
            {
                return null;
            }

            var configs = new List<CatalogBuilderConfig>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CatalogBuilderConfig config
                    = AssetDatabase.LoadAssetAtPath<CatalogBuilderConfig>(path);
                if (config != null)
                {
                    configs.Add(config);
                }
            }

            configs.Sort((left, right) => string.Compare(
                left.name,
                right.name,
                StringComparison.Ordinal));
            return configs.Count == 0 ? null : configs[0];
        }

        #region Header

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    _config.name,
                    EditorStyles.boldLabel
                );

                EditorGUILayout.LabelField(
                    $"{_config.Entries.Count} staged",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(74f)
                );

                GUILayout.FlexibleSpace();

                using (new EditorGUI.ChangeCheckScope())
                {
                    int newVersion = EditorGUILayout.IntField(
                        "Version",
                        _config.Version,
                        GUILayout.Width(112f)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Version");
                        _config.Version = newVersion;
                        _lastValidation = null;
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        #endregion

        #region Output Settings

        private void DrawCatalogSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _settingsFoldout = EditorGUILayout.Foldout(
                        _settingsFoldout,
                        "Catalog Settings",
                        true
                    );
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            "Ping Config",
                            EditorStyles.miniButton,
                            GUILayout.Width(76f)
                        ))
                    {
                        EditorGUIUtility.PingObject(_config);
                    }
                }

                if (!_settingsFoldout)
                {
                    return;
                }

                DrawOutputSettings();
                EditorGUILayout.Space(4f);
                DrawEntryIdSettings();
                EditorGUILayout.Space(4f);
                DrawRuntimeTargetGuidance();
            }
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField(
                "Output", EditorStyles.miniBoldLabel);

            using (new EditorGUI.ChangeCheckScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Folder", GUILayout.Width(60));
                    _config.OutputFolder = EditorGUILayout.TextField(
                        _config.OutputFolder);

                    if (GUILayout.Button("...", EditorStyles.miniButton,
                            GUILayout.Width(30)))
                    {
                        string selected = EditorUtility.OpenFolderPanel(
                            "Select Output Folder", "Assets", "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            string dataPath = Application.dataPath;
                            if (selected.StartsWith(dataPath))
                            {
                                _config.OutputFolder = "Assets"
                                    + selected.Substring(dataPath.Length)
                                        .Replace('\\', '/');
                            }
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Subdir", GUILayout.Width(60));
                    _config.SubfolderName = EditorGUILayout.TextField(
                        _config.SubfolderName);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "File", GUILayout.Width(60));
                    _config.CatalogFileName = EditorGUILayout.TextField(
                        _config.CatalogFileName);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssets();
                    _lastValidation = null;
                }
            }
        }

        private void DrawEntryIdSettings()
        {
            EditorGUILayout.LabelField("Entry IDs", EditorStyles.miniBoldLabel);
            bool enableEntryAutoId = _config.EnableEntryAutoId;
            bool enableEntryAutoIdRegex = _config.EnableEntryAutoIdRegex;
            string entryAutoIdTemplate = _config.EntryAutoIdTemplate;
            string entryAutoIdRegexPattern = _config.EntryAutoIdRegexPattern;

            using (new EditorGUI.ChangeCheckScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Auto ID",
                        GUILayout.Width(48f)
                    );
                    enableEntryAutoId = EditorGUILayout.Toggle(
                        _config.EnableEntryAutoId,
                        GUILayout.Width(18f)
                    );
                    using (new EditorGUI.DisabledGroupScope(
                               !enableEntryAutoId))
                    {
                        EditorGUILayout.LabelField(
                            "Template",
                            GUILayout.Width(58f)
                        );
                        entryAutoIdTemplate = EditorGUILayout.TextField(
                            _config.EntryAutoIdTemplate
                        );
                    }
                }

                using (new EditorGUI.DisabledGroupScope(!enableEntryAutoId))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Regex",
                            GUILayout.Width(48f)
                        );
                        enableEntryAutoIdRegex = EditorGUILayout.Toggle(
                            _config.EnableEntryAutoIdRegex,
                            GUILayout.Width(18f)
                        );
                        using (new EditorGUI.DisabledGroupScope(
                                   !enableEntryAutoIdRegex))
                        {
                            EditorGUILayout.LabelField(
                                "Pattern",
                                GUILayout.Width(58f)
                            );
                            entryAutoIdRegexPattern = EditorGUILayout.TextField(
                                _config.EntryAutoIdRegexPattern
                            );
                        }
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Change Catalog Entry ID Settings");
                    _config.EnableEntryAutoId = enableEntryAutoId;
                    _config.EntryAutoIdTemplate = entryAutoIdTemplate;
                    _config.EnableEntryAutoIdRegex = enableEntryAutoIdRegex;
                    _config.EntryAutoIdRegexPattern = entryAutoIdRegexPattern;
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssets();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledGroupScope(
                           !_config.EnableEntryAutoId
                           || string.IsNullOrWhiteSpace(
                               _config.EntryAutoIdTemplate)))
                {
                    if (GUILayout.Button("Refresh IDs", GUILayout.Width(88f)))
                    {
                        RefreshEntryIds();
                    }
                }
            }
        }

        private void RefreshEntryIds()
        {
            Undo.RecordObject(_config, "Refresh Catalog Entry IDs");
            for (int index = 0; index < _config.Entries.Count; index++)
            {
                StagedCatalogEntry entry = _config.Entries[index];
                if (entry == null)
                {
                    continue;
                }

                entry.Id = FormatEntryId(
                    _config.EntryAutoIdTemplate,
                    index,
                    Path.GetFileNameWithoutExtension(entry.SourceFilePath),
                    _config.EnableEntryAutoIdRegex,
                    _config.EntryAutoIdRegexPattern
                );
            }

            _lastValidation = null;
            InvalidateFilters();
            ClearValidationCache();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        #endregion

        #region Runtime Target Guidance

        private void DrawRuntimeTargetGuidance()
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Runtime Provider", GUILayout.Width(105));
                using (new EditorGUI.ChangeCheckScope())
                {
                    ScriptableObject provider = EditorGUILayout.ObjectField(
                        _config.RuntimeProvider,
                        typeof(ScriptableObject),
                        false
                    ) as ScriptableObject;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Set Catalog Runtime Provider");
                        _config.RuntimeProvider = provider;
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssets();
                    }
                }
            }

            if (!CatalogBuildUtility.TryGetOutputDirectory(
                    _config,
                    out string outputDirectory,
                    out _))
            {
                return;
            }

            string streamingAssetsDirectory = Path.GetFullPath(
                Application.streamingAssetsPath);
            bool isStreamingAssetsOutput = outputDirectory.StartsWith(
                streamingAssetsDirectory,
                StringComparison.OrdinalIgnoreCase);
            string catalogPath = _config.CatalogFileName;
            if (isStreamingAssetsOutput)
            {
                string outputRelativePath = outputDirectory.Substring(
                    streamingAssetsDirectory.Length)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                catalogPath = string.IsNullOrWhiteSpace(outputRelativePath)
                    ? _config.CatalogFileName
                    : $"{outputRelativePath}/{_config.CatalogFileName}";
            }

            int separatorIndex = catalogPath.LastIndexOf('/');
            string providerFolder = separatorIndex < 0
                ? "<root>"
                : catalogPath.Substring(0, separatorIndex);

            EditorGUILayout.HelpBox(
                isStreamingAssetsOutput
                    ? "Streaming provider setup: enable Use Catalog, set "
                        + $"Catalog File Name to '{catalogPath}', and set "
                        + $"Subfolder to '{providerFolder}'."
                    : "Online provider setup: enable Use Catalog, set Catalog "
                        + $"File Name to '{catalogPath}', and set Base URL to "
                        + "the S3/CDN prefix containing this catalog.",
                MessageType.Info);

            if (_config.RuntimeProvider == null)
            {
                return;
            }

            using (new EditorGUI.DisabledGroupScope(
                       !CanConfigureRuntimeProvider(_config.RuntimeProvider)))
            {
                if (GUILayout.Button("Apply Catalog Settings to Runtime Provider"))
                {
                    ApplyRuntimeProviderSettings(
                        _config.RuntimeProvider,
                        catalogPath,
                        providerFolder);
                }
            }
        }

        private static bool CanConfigureRuntimeProvider(ScriptableObject provider)
        {
            SerializedObject serializedProvider = new SerializedObject(provider);
            return serializedProvider.FindProperty("_catalogFileName") != null
                || serializedProvider.FindProperty("_catalogConfig") != null;
        }

        private static void ApplyRuntimeProviderSettings(
            ScriptableObject provider,
            string catalogPath,
            string providerFolder)
        {
            SerializedObject serializedProvider = new SerializedObject(provider);
            SerializedProperty downloadableCatalog = serializedProvider.FindProperty(
                "_catalogFileName");
            if (downloadableCatalog != null)
            {
                Undo.RecordObject(provider, "Configure Downloadable Catalog");
                serializedProvider.FindProperty("_useCatalog").boolValue = true;
                downloadableCatalog.stringValue = Path.GetFileName(catalogPath);
                serializedProvider.ApplyModifiedProperties();
                EditorUtility.SetDirty(provider);
                return;
            }

            SerializedProperty streamingCatalog = serializedProvider.FindProperty(
                "_catalogConfig");
            if (streamingCatalog == null)
            {
                return;
            }

            Undo.RecordObject(provider, "Configure Streaming Catalog");
            streamingCatalog.FindPropertyRelative("UseCatalog").boolValue = true;
            streamingCatalog.FindPropertyRelative("CatalogFileName").stringValue
                = catalogPath;
            serializedProvider.FindProperty("subFolder").stringValue
                = providerFolder == "<root>" ? string.Empty : providerFolder;
            serializedProvider.ApplyModifiedProperties();
            EditorUtility.SetDirty(provider);
        }

        #endregion

        #region Validation

        private bool ValidateCatalog(bool showDialog)
        {
            _lastValidation = CatalogBuildUtility.Validate(_config);
            if (_lastValidation.IsValid)
            {
                Repaint();
                return true;
            }

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Catalog Validation Failed",
                    string.Join("\n", _lastValidation.Errors),
                    "OK");
            }

            Repaint();
            return false;
        }

        #endregion

        #region Drop Zone

        private void DrawDropZone()
        {
            Rect dropArea = GUILayoutUtility.GetRect(
                GUIContent.none,
                EditorStyles.helpBox,
                GUILayout.Height(DropZoneHeight),
                GUILayout.ExpandWidth(true)
            );
            HandleDropZoneEvent(dropArea);

            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = _isDraggingOver
                ? new Color(0.45f, 0.85f, 0.5f)
                : previousColor;
            GUI.Box(dropArea, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = previousColor;

            GUI.Label(
                dropArea,
                _isDraggingOver
                    ? "Release to stage compatible files"
                    : "Drop .json, .bytes, or .bin files here to stage entries",
                EditorStyles.centeredGreyMiniLabel
            );
        }

        private void HandleDropZoneEvent(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.DragExited)
            {
                _isDraggingOver = false;
                Repaint();
                return;
            }

            if (currentEvent.type != EventType.DragUpdated
                && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                _isDraggingOver = false;
                return;
            }

            bool hasCompatibleFiles = HasCompatibleDraggedFiles();
            DragAndDrop.visualMode = hasCompatibleFiles
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
            _isDraggingOver = hasCompatibleFiles;
            if (currentEvent.type == EventType.DragPerform && hasCompatibleFiles)
            {
                DragAndDrop.AcceptDrag();
                StageFiles(DragAndDrop.paths);
                _isDraggingOver = false;
                currentEvent.Use();
                GUIUtility.ExitGUI();
                return;
            }

            currentEvent.Use();
        }

        private static bool HasCompatibleDraggedFiles()
        {
            string[] paths = DragAndDrop.paths;
            for (int index = 0; index < paths.Length; index++)
            {
                if (IsValidLevelFile(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Staged Entries

        private void InvalidateFilters()
        {
            _filteredEntries = null;
            _lastEntriesCount = -1;
        }

        private List<StagedCatalogEntry> GetFilteredEntries()
        {
            if (_config == null)
            {
                return new List<StagedCatalogEntry>();
            }

            int currentCount = _config.Entries.Count;
            if (_filteredEntries != null
                && _lastSearchFilter == _searchFilter
                && _lastEntriesCount == currentCount)
            {
                return _filteredEntries;
            }

            _filteredEntries = new List<StagedCatalogEntry>();
            _lastSearchFilter = _searchFilter;
            _lastEntriesCount = currentCount;

            foreach (StagedCatalogEntry entry in _config.Entries)
            {
                if (MatchesSearch(entry))
                {
                    _filteredEntries.Add(entry);
                }
            }

            return _filteredEntries;
        }

        private string GetCachedHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            long lastWrite = File.GetLastWriteTimeUtc(filePath).Ticks;

            if (_hashCache.TryGetValue(filePath, out CachedHash cached)
                && cached.LastWriteTicks == lastWrite)
            {
                return cached.Hash;
            }

            string hash = ComputeHash(filePath);
            _hashCache[filePath] = new CachedHash
            {
                Hash = hash,
                LastWriteTicks = lastWrite
            };
            return hash;
        }

        private bool TryGetCachedValidation(
            StagedCatalogEntry entry,
            out CachedValidation cachedValidation
        )
        {
            if (entry == null)
            {
                cachedValidation = default;
                return false;
            }

            long lastWriteTicks = GetLastWriteTicks(entry.SourceFilePath);
            if (_validationCache.TryGetValue(
                    entry,
                    out cachedValidation
                )
                && cachedValidation.LastWriteTicks == lastWriteTicks
                && cachedValidation.Type == entry.Type
                && string.Equals(
                    cachedValidation.Id,
                    entry.Id,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    cachedValidation.SourceFilePath,
                    entry.SourceFilePath,
                    StringComparison.Ordinal
                ))
            {
                return true;
            }

            cachedValidation = default;
            return false;
        }

        private void QueueValidation(StagedCatalogEntry entry)
        {
            if (entry == null || !_queuedValidationEntries.Add(entry))
            {
                return;
            }

            _validationQueue.Enqueue(entry);
            if (_isValidationQueued)
            {
                return;
            }

            _isValidationQueued = true;
            EditorApplication.delayCall += ProcessNextValidation;
        }

        private void ProcessNextValidation()
        {
            _isValidationQueued = false;
            if (_config == null || _validationQueue.Count == 0)
            {
                return;
            }

            StagedCatalogEntry entry = _validationQueue.Dequeue();
            _queuedValidationEntries.Remove(entry);
            if (_config.Entries.Contains(entry))
            {
                CacheValidation(entry);
            }

            Repaint();
            if (_validationQueue.Count == 0)
            {
                return;
            }

            _isValidationQueued = true;
            EditorApplication.delayCall += ProcessNextValidation;
        }

        private void CacheValidation(StagedCatalogEntry entry)
        {
            string contentHash = GetCachedHash(entry.SourceFilePath);
            long lastWriteTicks = GetLastWriteTicks(entry.SourceFilePath);
            PuzzleLevelValidationResult validation = string.IsNullOrEmpty(contentHash)
                ? CatalogBuildUtility.ValidateLevel(entry)
                : CatalogBuildUtility.ValidateLevel(entry, contentHash);
            _validationCache[entry] = new CachedValidation
            {
                Id = entry.Id,
                Type = entry.Type,
                SourceFilePath = entry.SourceFilePath,
                LastWriteTicks = lastWriteTicks,
                ContentHash = contentHash,
                Result = validation
            };
        }

        private void ClearValidationCache()
        {
            _validationCache.Clear();
            _validationQueue.Clear();
            _queuedValidationEntries.Clear();
        }

        private static long GetLastWriteTicks(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return 0;
            }

            return File.GetLastWriteTimeUtc(filePath).Ticks;
        }

        private void DrawStagedEntries()
        {
            DrawEntriesSearchToolbar();

            if (_config.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No files staged. Drag .json, .bytes, or .bin files here.",
                    MessageType.Info);
                return;
            }

            List<StagedCatalogEntry> filtered = GetFilteredEntries();
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No entries match the search filter.",
                    MessageType.Info);
                return;
            }

            int totalPages = Mathf.Max(
                1,
                Mathf.CeilToInt((float)filtered.Count / _pageSize)
            );
            _pageIndex = Mathf.Clamp(_pageIndex, 0, totalPages - 1);
            int startIndex = _pageIndex * _pageSize;
            int endIndex = Mathf.Min(startIndex + _pageSize, filtered.Count);
            DrawEntriesControlToolbar(
                filtered,
                totalPages
            );
            float tableHeight = Mathf.Min(
                EntriesTableMaximumHeight,
                (endIndex - startIndex + 2) * (EditorGUIUtility.singleLineHeight + 8f)
            );
            _entriesScrollPosition = EditorGUILayout.BeginScrollView(
                _entriesScrollPosition,
                GUILayout.Height(tableHeight)
            );
            using (new EditorGUILayout.VerticalScope(
                       GUILayout.MinWidth(EntriesTableMinimumWidth)
                   ))
            {
                DrawStagedColumnHeaders(filtered, startIndex, endIndex);
                for (int index = startIndex; index < endIndex; index++)
                {
                    DrawStagedEntry(filtered[index]);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntriesSearchToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(
                    $"Catalog Entries ({_config.Entries.Count})",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(132f)
                );
                string searchFilter = EditorGUILayout.TextField(
                    _searchFilter,
                    EditorStyles.toolbarSearchField
                );
                if (!string.Equals(
                        searchFilter,
                        _searchFilter,
                        StringComparison.Ordinal
                    ))
                {
                    _searchFilter = searchFilter;
                    _pageIndex = 0;
                }

                if (!string.IsNullOrEmpty(_searchFilter)
                    && GUILayout.Button(
                        "Clear",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(42f)
                    ))
                {
                    _searchFilter = string.Empty;
                    _pageIndex = 0;
                }

                if (GUILayout.Button(
                        "Validate All",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(76f)
                    ))
                {
                    ValidateCatalog(false);
                }
            }
        }

        private void DrawEntriesControlToolbar(
            IReadOnlyList<StagedCatalogEntry> filteredEntries,
            int totalPages)
        {
            if (EditorGUIUtility.currentViewWidth < CompactToolbarWidth)
            {
                DrawPaginationToolbar(totalPages);
                DrawSelectionToolbar(filteredEntries);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawPaginationControls(totalPages);
                GUILayout.FlexibleSpace();
                DrawSelectionControls(filteredEntries);
                DrawBatchEntryOperations();
            }
        }

        private void DrawPaginationToolbar(int totalPages)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawPaginationControls(totalPages);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSelectionToolbar(
            IReadOnlyList<StagedCatalogEntry> filteredEntries
        )
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawSelectionControls(filteredEntries);
                GUILayout.FlexibleSpace();
                DrawBatchEntryOperations();
            }
        }

        private void DrawPaginationControls(int totalPages)
        {
            using (new EditorGUI.DisabledGroupScope(_pageIndex == 0))
            {
                if (GUILayout.Button(
                        "<",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(24f)
                    ))
                {
                    _pageIndex--;
                }
            }

            using (new EditorGUI.DisabledGroupScope(
                       _pageIndex >= totalPages - 1))
            {
                if (GUILayout.Button(
                        ">",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(24f)
                    ))
                {
                    _pageIndex++;
                }
            }

            EditorGUILayout.LabelField(
                $"{_pageIndex + 1}/{totalPages}",
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(48f)
            );
            EditorGUILayout.LabelField(
                "Size",
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(28f)
            );
            _pageSize = Mathf.Clamp(
                EditorGUILayout.IntField(_pageSize, GUILayout.Width(36f)),
                MinimumPageSize,
                MaximumPageSize
            );
        }

        private void DrawSelectionControls(
            IReadOnlyList<StagedCatalogEntry> filteredEntries
        )
        {
            if (GUILayout.Button(
                    new GUIContent("All", "Select all filtered entries"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(28f)
                ))
            {
                SelectEntries(filteredEntries);
            }

            using (new EditorGUI.DisabledGroupScope(
                       _selectedEntries.Count == 0))
            {
                if (GUILayout.Button(
                        new GUIContent("None", "Clear entry selection"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(38f)
                    ))
                {
                    _selectedEntries.Clear();
                }
            }

            EditorGUILayout.LabelField(
                _selectedEntries.Count.ToString(),
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(24f)
            );
        }

        private void DrawBatchEntryOperations()
        {
            using (new EditorGUI.DisabledGroupScope(_selectedEntries.Count == 0))
            {
                if (GUILayout.Button(
                        new GUIContent(
                            "Refresh Hash",
                            "Update stored hashes for selected source files"
                        ),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(76f)
                    ))
                {
                    RefreshSelectedEntryHashes();
                }

                if (GUILayout.Button(
                        new GUIContent("Delete", "Delete selected entries"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(46f)
                    ))
                {
                    DeleteSelectedEntries();
                }
            }
        }

        private void DrawStagedColumnHeaders(
            IReadOnlyList<StagedCatalogEntry> filteredEntries,
            int startIndex,
            int endIndex)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool pageSelected = AreEntriesSelected(
                    filteredEntries,
                    startIndex,
                    endIndex
                );
                bool selectPage = GUILayout.Toggle(
                    pageSelected,
                    new GUIContent("✓", "Select entries on this page"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(SelectionColumnWidth)
                );
                if (selectPage != pageSelected)
                {
                    SetPageSelection(
                        filteredEntries,
                        startIndex,
                        endIndex,
                        selectPage
                    );
                }

                EditorGUILayout.LabelField(
                    "ID",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(_colIdWidth)
                );
                EditorGUILayout.LabelField(
                    "Type",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(TypeColumnWidth)
                );
                EditorGUILayout.LabelField(
                    "Relative Path",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(_colPathWidth)
                );
                EditorGUILayout.LabelField(
                    "Status",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(StatusColumnWidth)
                );
                EditorGUILayout.LabelField(
                    "Source",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(SourceColumnWidth)
                );
            }
        }

        private void DrawStagedEntry(StagedCatalogEntry entry)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            if (_selectedEntries.Contains(entry))
            {
                GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
            }

            EditorGUILayout.HorizontalScope rowScope = new EditorGUILayout.HorizontalScope(
                EditorStyles.helpBox
            );
            using (rowScope)
            {
                bool isSelected = _selectedEntries.Contains(entry);
                bool selectEntry = GUILayout.Toggle(
                    isSelected,
                    GUIContent.none,
                    GUILayout.Width(SelectionColumnWidth)
                );
                if (selectEntry != isSelected)
                {
                    SetEntrySelection(entry, selectEntry);
                }

                using (new EditorGUI.ChangeCheckScope())
                {
                    string newId = EditorGUILayout.TextField(
                        entry.Id,
                        GUILayout.Width(_colIdWidth)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Entry ID");
                        entry.Id = newId;
                        EditorUtility.SetDirty(_config);
                        InvalidateFilters();
                        _lastValidation = null;
                    }
                }

                using (new EditorGUI.ChangeCheckScope())
                {
                    int dataTypeIndex = Mathf.Clamp((int)entry.Type, 0, 2);
                    int newDataTypeIndex = EditorGUILayout.Popup(
                        dataTypeIndex,
                        SupportedDataTypeNames,
                        GUILayout.Width(TypeColumnWidth)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Entry Type");
                        entry.Type = (DataType)newDataTypeIndex;
                        EditorUtility.SetDirty(_config);
                        _lastValidation = null;
                    }
                }

                using (new EditorGUI.ChangeCheckScope())
                {
                    string newRelativePath = EditorGUILayout.TextField(
                        entry.RelativePath,
                        EditorStyles.miniTextField,
                        GUILayout.Width(_colPathWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Entry Path");
                        entry.RelativePath = newRelativePath;
                        EditorUtility.SetDirty(_config);
                        InvalidateFilters();
                        _lastValidation = null;
                    }
                }

                DrawEntryStatus(entry);

                string sourceName = Path.GetFileName(entry.SourceFilePath);
                EditorGUILayout.LabelField(
                    new GUIContent(sourceName, entry.SourceFilePath),
                    EditorStyles.miniLabel,
                    GUILayout.Width(SourceColumnWidth)
                );
            }

            GUI.backgroundColor = previousBackgroundColor;
            HandleEntryContextClick(rowScope.rect, entry);
        }

        private void DrawEntryStatus(StagedCatalogEntry entry)
        {
            if (!TryGetCachedValidation(
                    entry,
                    out CachedValidation cachedValidation
                ))
            {
                QueueValidation(entry);
                DrawPendingEntryStatus(entry);
                return;
            }

            PuzzleLevelValidationResult validation = cachedValidation.Result;
            bool hasError = HasSeverity(
                validation.Diagnostics,
                PuzzleLevelValidationSeverity.Error);
            bool hasWarning = HasSeverity(
                validation.Diagnostics,
                PuzzleLevelValidationSeverity.Warning);
            string tooltip = CreateValidationTooltip(validation);
            bool sourceChanged = !string.IsNullOrEmpty(cachedValidation.ContentHash)
                && !string.Equals(
                    cachedValidation.ContentHash,
                    entry.ContentHash,
                    StringComparison.Ordinal);
            if (sourceChanged)
            {
                tooltip += "\nSource file changed since its hash was staged.";
            }
            Color previousColor = GUI.contentColor;
            GUIContent status;
            if (hasError)
            {
                GUI.contentColor = new Color(0.88f, 0.3f, 0.3f);
                status = new GUIContent("● Invalid", tooltip);
            }
            else if (hasWarning || sourceChanged)
            {
                GUI.contentColor = new Color(0.92f, 0.7f, 0.2f);
                status = new GUIContent("● Warning", tooltip);
            }
            else
            {
                GUI.contentColor = new Color(0.35f, 0.8f, 0.4f);
                status = new GUIContent("● Valid", tooltip);
            }

            EditorGUILayout.LabelField(
                status,
                EditorStyles.miniBoldLabel,
                GUILayout.Width(StatusColumnWidth)
            );
            GUI.contentColor = previousColor;
        }

        private static void DrawPendingEntryStatus(StagedCatalogEntry entry)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = new Color(0.55f, 0.65f, 0.8f);
            EditorGUILayout.LabelField(
                new GUIContent(
                    "● Checking",
                    $"Validating '{Path.GetFileName(entry.SourceFilePath)}'."
                ),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(StatusColumnWidth)
            );
            GUI.contentColor = previousColor;
        }

        private static bool HasSeverity(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            PuzzleLevelValidationSeverity severity)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreateValidationTooltip(
            PuzzleLevelValidationResult validation)
        {
            var tooltip = new StringBuilder();
            if (!string.IsNullOrEmpty(validation.ValidatorName))
            {
                tooltip.Append("Validator: ");
                tooltip.AppendLine(validation.ValidatorName);
            }

            for (int index = 0; index < validation.Diagnostics.Count; index++)
            {
                PuzzleLevelValidationDiagnostic diagnostic
                    = validation.Diagnostics[index];
                tooltip.Append('[');
                tooltip.Append(diagnostic.Code);
                tooltip.Append("] ");
                tooltip.AppendLine(diagnostic.Message);
            }

            return tooltip.Length == 0 ? "Validated" : tooltip.ToString();
        }

        private void HandleEntryContextClick(
            Rect rowRect,
            StagedCatalogEntry entry)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.ContextClick
                || !rowRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            bool isSelected = _selectedEntries.Contains(entry);
            CustomPopupDropdown.Show(
                new Rect(currentEvent.mousePosition, Vector2.one),
                new CatalogEntryPopup(
                    entry,
                    () => DeleteEntry(entry),
                    () => RefreshEntryHash(entry),
                    () => SetEntrySelection(entry, !isSelected)
                )
            );
            currentEvent.Use();
        }

        private bool AreEntriesSelected(
            IReadOnlyList<StagedCatalogEntry> entries,
            int startIndex,
            int endIndex)
        {
            if (startIndex >= endIndex)
            {
                return false;
            }

            for (int index = startIndex; index < endIndex; index++)
            {
                if (!_selectedEntries.Contains(entries[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void SetPageSelection(
            IReadOnlyList<StagedCatalogEntry> entries,
            int startIndex,
            int endIndex,
            bool isSelected)
        {
            for (int index = startIndex; index < endIndex; index++)
            {
                SetEntrySelection(entries[index], isSelected);
            }
        }

        private void SelectEntries(IReadOnlyList<StagedCatalogEntry> entries)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                _selectedEntries.Add(entries[index]);
            }
        }

        private void SetEntrySelection(
            StagedCatalogEntry entry,
            bool isSelected)
        {
            if (isSelected)
            {
                _selectedEntries.Add(entry);
                return;
            }

            _selectedEntries.Remove(entry);
        }

        private void PruneSelection()
        {
            if (_selectedEntries.Count == 0)
            {
                return;
            }

            HashSet<StagedCatalogEntry> currentEntries
                = new HashSet<StagedCatalogEntry>(_config.Entries);
            _selectedEntries.RemoveWhere(
                entry => entry == null || !currentEntries.Contains(entry)
            );
        }

        private void RefreshEntryHash(StagedCatalogEntry entry)
        {
            string hash = GetCachedHash(entry.SourceFilePath);
            if (string.IsNullOrEmpty(hash))
            {
                EditorUtility.DisplayDialog(
                    "Source File Missing",
                    $"Unable to refresh the hash for '{entry.SourceFilePath}'.",
                    "OK"
                );
                return;
            }

            Undo.RecordObject(_config, "Refresh Catalog Entry Hash");
            entry.ContentHash = hash;
            _lastValidation = null;
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void DeleteEntry(StagedCatalogEntry entry)
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete Catalog Entry",
                    $"Remove '{entry.Id}' from this catalog?\n\n"
                    + "The source file will not be deleted.",
                    "Delete Entry",
                    "Cancel"
                ))
            {
                return;
            }

            Undo.RecordObject(_config, "Delete Catalog Entry");
            _config.Entries.Remove(entry);
            _selectedEntries.Remove(entry);
            _hashCache.Remove(entry.SourceFilePath);
            _validationCache.Remove(entry);
            _queuedValidationEntries.Remove(entry);
            _lastValidation = null;
            InvalidateFilters();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
            GUIUtility.ExitGUI();
        }

        private void RefreshSelectedEntryHashes()
        {
            if (_selectedEntries.Count == 0)
            {
                return;
            }

            Undo.RecordObject(_config, "Refresh Catalog Entry Hashes");
            foreach (StagedCatalogEntry entry in _selectedEntries)
            {
                string hash = GetCachedHash(entry.SourceFilePath);
                if (!string.IsNullOrEmpty(hash))
                {
                    entry.ContentHash = hash;
                }
            }

            _lastValidation = null;
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void DeleteSelectedEntries()
        {
            if (_selectedEntries.Count == 0
                || !EditorUtility.DisplayDialog(
                    "Delete Catalog Entries",
                    $"Remove {_selectedEntries.Count} selected entries?\n\n"
                    + "Source files will not be deleted.",
                    "Delete Entries",
                    "Cancel"
                ))
            {
                return;
            }

            Undo.RecordObject(_config, "Delete Catalog Entries");
            foreach (StagedCatalogEntry entry in _selectedEntries)
            {
                _hashCache.Remove(entry.SourceFilePath);
                _validationCache.Remove(entry);
                _queuedValidationEntries.Remove(entry);
            }

            _config.Entries.RemoveAll(_selectedEntries.Contains);

            _selectedEntries.Clear();
            _pageIndex = 0;
            _lastValidation = null;
            InvalidateFilters();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
            GUIUtility.ExitGUI();
        }

        private bool MatchesSearch(StagedCatalogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                return true;
            }

            string filter = _searchFilter.Trim();

            if (entry.Id != null
                && entry.Id.IndexOf(
                    filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (entry.RelativePath != null
                && entry.RelativePath.IndexOf(
                    filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string sourceName = Path.GetFileName(entry.SourceFilePath);
            if (sourceName != null
                && sourceName.IndexOf(
                    filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        #endregion

        #region Actions

        private void DrawActions()
        {
            bool noEntries = _config.Entries.Count == 0;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Build & Delivery",
                    EditorStyles.boldLabel
                );
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledGroupScope(
                               noEntries || _isUploading))
                    {
                        if (GUILayout.Button("Validate", GUILayout.Height(30f)))
                        {
                            ValidateCatalog(true);
                        }

                        if (GUILayout.Button(
                                "Generate Catalog",
                                GUILayout.Height(30f)
                            ))
                        {
                            GenerateCatalog();
                        }
                    }

                    using (new EditorGUI.DisabledGroupScope(
                               noEntries
                               || _isUploading
                               || !_config.S3.IsValid))
                    {
                        if (GUILayout.Button(
                                "Generate & Upload",
                                GUILayout.Height(30f)
                            )
                            && GenerateCatalog())
                        {
                            UploadToS3();
                        }
                    }
                }
            }
        }

        #endregion

        #region S3 Upload

        private void DrawS3Section()
        {
            S3UploadSettings s3 = _config.S3;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _s3Foldout = EditorGUILayout.Foldout(
                        _s3Foldout,
                        "S3 Upload Settings",
                        true
                    );
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        s3.IsValid ? "Ready" : s3.Enabled ? "Incomplete" : "Disabled",
                        EditorStyles.centeredGreyMiniLabel,
                        GUILayout.Width(64f)
                    );
                }

                if (!_s3Foldout)
                {
                    return;
                }

                using (new EditorGUI.ChangeCheckScope())
                {
                    s3.Enabled = EditorGUILayout.Toggle(
                        "Enabled", s3.Enabled);

                    using (new EditorGUI.DisabledGroupScope(!s3.Enabled))
                    {
                        s3.Endpoint = EditorGUILayout.TextField(
                            "Endpoint", s3.Endpoint);
                        s3.Region = EditorGUILayout.TextField(
                            "Region", s3.Region);
                        s3.Bucket = EditorGUILayout.TextField(
                            "Bucket", s3.Bucket);
                        s3.UseEnvironmentCredentials = EditorGUILayout.Toggle(
                            "Use Environment Credentials",
                            s3.UseEnvironmentCredentials);
                        s3.AccessKeyEnvironmentVariable = EditorGUILayout.TextField(
                            "Access Key Variable",
                            s3.AccessKeyEnvironmentVariable);
                        s3.SecretKeyEnvironmentVariable = EditorGUILayout.TextField(
                            "Secret Key Variable",
                            s3.SecretKeyEnvironmentVariable);

                        s3.BasePrefix = EditorGUILayout.TextField(
                            "Base Prefix", s3.BasePrefix);

                        s3.PublicRead = EditorGUILayout.Toggle(
                            "Public Read (x-amz-acl)", s3.PublicRead);
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssets();
                    }
                }

                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledGroupScope(
                               !s3.IsValid || _isUploading))
                    {
                        if (GUILayout.Button("Test Connection",
                                GUILayout.Width(120)))
                        {
                            TestS3Connection();
                        }
                    }

                    if (_isUploading)
                    {
                        GUILayout.Label(
                            _uploadStatusMessage,
                            EditorStyles.miniLabel);
                        if (_canCancelUpload
                            && GUILayout.Button("Cancel", GUILayout.Width(60)))
                        {
                            _cancelUploadRequested = true;
                        }
                    }
                }

                if (_isUploading)
                {
                    Rect progressRect = EditorGUILayout.GetControlRect(
                        false, 18f);
                    EditorGUI.ProgressBar(
                        progressRect, _uploadProgress,
                        _uploadStatusMessage);
                }

                if (!s3.Enabled)
                {
                    EditorGUILayout.HelpBox(
                        "Enable S3 upload and configure credentials "
                        + "to upload the catalog to S3-compatible storage.",
                        MessageType.Info);
                }
                else if (!s3.IsValid)
                {
                    EditorGUILayout.HelpBox(
                        "All S3 fields are required. Ensure Endpoint, Region, "
                        + "Bucket, and the configured credential environment "
                        + "variables are available to the Unity Editor.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        s3.UseEnvironmentCredentials
                            ? "Credentials are read from the Unity Editor process "
                                + "environment and are not serialized in this asset."
                            : "Legacy serialized credentials are enabled. Migrate "
                                + "to environment variables before committing this asset.",
                        s3.UseEnvironmentCredentials
                            ? MessageType.Info
                            : MessageType.Warning);
                }
            }
        }

        private void TestS3Connection()
        {
            _isUploading = true;
            _canCancelUpload = false;
            _uploadProgress = 0f;
            _uploadStatusMessage = "Testing connection...";
            Repaint();

            S3Uploader.TestConnection(_config.S3, result =>
            {
                _isUploading = false;
                _uploadStatusMessage = "";

                if (result.IsSuccess)
                {
                    EditorUtility.DisplayDialog(
                        "S3 Connection",
                        "Connection successful!",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "S3 Connection Failed",
                        $"[{result.HttpStatusCode}] {result.Message}",
                        "OK");
                }

                Repaint();
            });
        }

        private void UploadToS3()
        {
            string outputDir = GetOutputDirectory();

            if (!Directory.Exists(outputDir))
            {
                EditorUtility.DisplayDialog(
                    "Output Missing",
                    $"Output directory does not exist:\n{outputDir}\n\n"
                    + "Generate the catalog first.",
                    "OK");
                return;
            }

            _isUploading = true;
            _canCancelUpload = true;
            _cancelUploadRequested = false;
            _uploadProgress = 0f;
            _uploadStatusMessage = "Preparing upload...";
            Repaint();

            S3Uploader.UploadDirectory(
                _config.S3,
                outputDir,
                _config.LastGenerated.CatalogRelativePath,
                () => _cancelUploadRequested,
                (status, progress) =>
                {
                    _uploadStatusMessage = status;
                    _uploadProgress = progress;
                    Repaint();
                },
                HandleUploadComplete);
        }

        private void HandleUploadComplete(S3Result result)
        {
            if (!result.IsSuccess)
            {
                CompleteUpload(result);
                return;
            }

            List<string> stalePaths = CatalogBuildUtility.GetStaleRelativePaths(
                _config.LastUploaded,
                _config.LastGenerated);
            DeleteNextStaleRemoteFile(stalePaths, 0, result);
        }

        private void DeleteNextStaleRemoteFile(
            List<string> stalePaths,
            int index,
            S3Result uploadResult)
        {
            if (_cancelUploadRequested)
            {
                CompleteUpload(new S3Result
                {
                    Status = S3UploadStatus.Cancelled,
                    Message = "Upload cleanup was cancelled."
                });
                return;
            }

            if (index >= stalePaths.Count)
            {
                CompleteUpload(uploadResult);
                return;
            }

            string key = CombineS3Key(_config.S3.BasePrefix, stalePaths[index]);
            _uploadStatusMessage = $"Removing stale remote file: {stalePaths[index]}";
            _uploadProgress = Mathf.Clamp01(
                0.95f + ((float)index / Mathf.Max(1, stalePaths.Count)) * 0.05f);
            S3Uploader.DeleteObject(_config.S3, key, result =>
            {
                if (!result.IsSuccess)
                {
                    CompleteUpload(new S3Result
                    {
                        Status = result.Status,
                        HttpStatusCode = result.HttpStatusCode,
                        Message = $"Upload completed, but stale remote file "
                            + $"'{stalePaths[index]}' could not be removed: "
                            + result.Message
                    });
                    return;
                }

                DeleteNextStaleRemoteFile(stalePaths, index + 1, uploadResult);
            });
        }

        private void CompleteUpload(S3Result result)
        {
            _isUploading = false;
            _canCancelUpload = false;
            _cancelUploadRequested = false;
            _uploadProgress = 1f;
            _uploadStatusMessage = "";

            if (result.IsSuccess)
            {
                Undo.RecordObject(_config, "Record Catalog Upload");
                _config.LastUploaded = _config.LastGenerated;
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog(
                    "Upload Complete",
                    result.Message + "\nCatalog and level payloads uploaded.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Upload Failed",
                    result.Message,
                    "OK");
            }

            Repaint();
        }

        private static string CombineS3Key(string basePrefix, string relativePath)
        {
            return string.IsNullOrWhiteSpace(basePrefix)
                ? relativePath.TrimStart('/')
                : basePrefix.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }

        private string GetOutputDirectory()
        {
            return CatalogBuildUtility.TryGetOutputDirectory(
                _config,
                out string outputDirectory,
                out _)
                ? outputDirectory
                : null;
        }

        #endregion

        #region File Staging

        private void StageFiles(string[] filePaths)
        {
            bool modified = false;

            foreach (string filePath in filePaths)
            {
                if (!IsValidLevelFile(filePath))
                {
                    continue;
                }

                string fileName = Path.GetFileName(filePath);
                DataType type = DetectType(filePath);
                string relativePath = $"{_config.SubfolderName}/{fileName}";
                string hash = GetCachedHash(filePath);

                _config.Entries.RemoveAll(
                    e => e.SourceFilePath == filePath);
                string id = _config.EnableEntryAutoId
                    ? FormatEntryId(
                        _config.EntryAutoIdTemplate,
                        _config.Entries.Count,
                        Path.GetFileNameWithoutExtension(filePath),
                        _config.EnableEntryAutoIdRegex,
                        _config.EntryAutoIdRegexPattern
                    )
                    : ExtractId(filePath);

                _config.Entries.Add(new StagedCatalogEntry
                {
                    Id = id,
                    Type = type,
                    RelativePath = relativePath,
                    SourceFilePath = filePath,
                    ContentHash = hash
                });

                modified = true;
            }

            if (modified)
            {
                InvalidateFilters();
                ClearValidationCache();
                _lastValidation = null;
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
                Repaint();
            }
        }

        #endregion

        #region Catalog Generation

        private bool GenerateCatalog()
        {
            if (!ValidateCatalog(true))
            {
                return false;
            }

            CatalogValidationResult validation = _lastValidation;
            CatalogBuildState previousState = _config.LastGenerated;
            bool hasManifestChanged = previousState == null
                || !previousState.HasBuild
                || !string.Equals(
                    previousState.ManifestHash,
                    validation.ManifestHash,
                    StringComparison.Ordinal);
            int generatedVersion = hasManifestChanged && previousState?.HasBuild == true
                ? _config.Version + 1
                : _config.Version;

            try
            {
                Directory.CreateDirectory(validation.OutputDirectory);
                int copied = CopyValidatedEntries(validation);
                string catalogPath = Path.Combine(
                    validation.OutputDirectory,
                    validation.CatalogRelativePath);
                string catalogJson = CatalogBuildUtility.BuildCatalogJson(
                    generatedVersion,
                    validation.ValidEntries);
                File.WriteAllText(catalogPath, catalogJson, Encoding.UTF8);

                CatalogBuildState generatedState
                    = CatalogBuildUtility.CreateBuildState(
                        validation,
                        generatedVersion
                    );
                generatedState.OutputFolder = NormalizeOutputFolder(
                    _config.OutputFolder
                );
                DeleteStaleLocalFiles(
                    validation.OutputDirectory,
                    previousState,
                    generatedState);
                ApplyGeneratedState(generatedState, validation);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Catalog Generated",
                    $"Version: {_config.Version}\n"
                    + $"Files copied: {copied}/{validation.ValidEntries.Count}\n"
                    + $"Catalog: {catalogPath}",
                    "OK");
                Repaint();
                return true;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Catalog Generation Failed",
                    exception.Message,
                    "OK");
                return false;
            }
        }

        private int CopyValidatedEntries(CatalogValidationResult validation)
        {
            int copied = 0;
            foreach (ValidatedCatalogEntry entry in validation.ValidEntries)
            {
                string destinationPath = Path.Combine(
                    validation.OutputDirectory,
                    entry.RelativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(entry.Source.SourceFilePath, destinationPath, true);
                copied++;
            }

            return copied;
        }

        private void DeleteStaleLocalFiles(
            string outputDirectory,
            CatalogBuildState previousState,
            CatalogBuildState currentState)
        {
            foreach (string relativePath in CatalogBuildUtility.GetStaleRelativePaths(
                         previousState,
                         currentState))
            {
                string stalePath = Path.Combine(outputDirectory, relativePath);
                if (File.Exists(stalePath))
                {
                    File.Delete(stalePath);
                }
            }
        }

        private void ApplyGeneratedState(
            CatalogBuildState generatedState,
            CatalogValidationResult validation)
        {
            Undo.RecordObject(_config, "Generate Catalog");
            _config.Version = generatedState.Version;
            _config.LastGenerated = generatedState;
            foreach (ValidatedCatalogEntry entry in validation.ValidEntries)
            {
                entry.Source.Id = entry.Id;
                entry.Source.RelativePath = entry.RelativePath;
                entry.Source.ContentHash = entry.ContentHash;
            }

            EditorUtility.SetDirty(_config);
        }

        #endregion

        #region Utility

        private static bool IsValidLevelFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".json"
                || ext == ".bytes"
                || ext == ".bin";
        }

        private static string ExtractId(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }

        private static string FormatEntryId(
            string template,
            int index,
            string fileName,
            bool isRegexEnabled,
            string regexPattern)
        {
            string evaluatedTemplate = string.IsNullOrWhiteSpace(template)
                ? "{index}"
                : template;
            string result = EntryIdPlaceholderRegex.Replace(
                evaluatedTemplate,
                match => ResolveEntryIdTag(
                    match.Groups[1].Value,
                    index,
                    fileName,
                    isRegexEnabled,
                    regexPattern,
                    match.Value
                )
            );
            return result.Replace("{{", "{").Replace("}}", "}");
        }

        private static string ResolveEntryIdTag(
            string tag,
            int index,
            string fileName,
            bool isRegexEnabled,
            string regexPattern,
            string fallback)
        {
            tag = tag.Trim();
            if (tag.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveRegexGroup(
                    fileName,
                    tag.Substring("regex:".Length),
                    isRegexEnabled,
                    regexPattern,
                    fallback
                );
            }

            if (!tag.StartsWith("index", StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            int separatorIndex = tag.IndexOf(':');
            if (separatorIndex < 0)
            {
                return index.ToString();
            }

            string offsetText = tag.Substring(separatorIndex + 1).Trim()
                .Replace("+", "");
            return int.TryParse(offsetText, out int offset)
                ? (index + offset).ToString()
                : index.ToString();
        }

        private static string ResolveRegexGroup(
            string fileName,
            string groupDefinition,
            bool isRegexEnabled,
            string regexPattern,
            string fallback)
        {
            int fallbackSeparatorIndex = groupDefinition.IndexOf('|');
            string groupIdentifier = fallbackSeparatorIndex < 0
                ? groupDefinition.Trim()
                : groupDefinition.Substring(0, fallbackSeparatorIndex).Trim();
            string emptyValueFallback = fallbackSeparatorIndex < 0
                ? fallback
                : groupDefinition.Substring(fallbackSeparatorIndex + 1);
            if (!isRegexEnabled
                || string.IsNullOrWhiteSpace(regexPattern)
                || string.IsNullOrWhiteSpace(groupIdentifier))
            {
                return emptyValueFallback;
            }

            try
            {
                Match match = Regex.Match(fileName, regexPattern);
                if (!match.Success)
                {
                    return emptyValueFallback;
                }

                if (int.TryParse(groupIdentifier, out int groupIndex))
                {
                    if (groupIndex >= match.Groups.Count)
                    {
                        return emptyValueFallback;
                    }

                    string groupValue = match.Groups[groupIndex].Value;
                    return string.IsNullOrEmpty(groupValue)
                        ? emptyValueFallback
                        : groupValue;
                }

                Group group = match.Groups[groupIdentifier];
                return group.Success && !string.IsNullOrEmpty(group.Value)
                    ? group.Value
                    : emptyValueFallback;
            }
            catch (ArgumentException)
            {
                return emptyValueFallback;
            }
        }

        private static DataType DetectType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".json"
                ? DataType.Text
                : DataType.Binary;
        }

        private static string NormalizeOutputFolder(string outputFolder)
        {
            return string.IsNullOrWhiteSpace(outputFolder)
                ? string.Empty
                : outputFolder.Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string NormalizeCatalogFileName(string catalogFileName)
        {
            return string.IsNullOrWhiteSpace(catalogFileName)
                ? string.Empty
                : catalogFileName.Trim();
        }

        private static string ComputeHash(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash)
                        .Replace("-", "")
                        .ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        #endregion

        #region Nested Types

        private enum CatalogBuilderTab
        {
            Configuration,
            Entries,
            Upload
        }

        private struct CachedHash
        {
            public string Hash;
            public long LastWriteTicks;
        }

        private struct CachedValidation
        {
            public string Id;
            public DataType Type;
            public string SourceFilePath;
            public long LastWriteTicks;
            public string ContentHash;
            public PuzzleLevelValidationResult Result;
        }

        private sealed class CatalogEntryPopup : PopupWindowContent
        {
            private readonly StagedCatalogEntry _entry;
            private readonly Action _deleteEntry;
            private readonly Action _refreshHash;
            private readonly Action _toggleSelection;

            public CatalogEntryPopup(
                StagedCatalogEntry entry,
                Action deleteEntry,
                Action refreshHash,
                Action toggleSelection)
            {
                _entry = entry;
                _deleteEntry = deleteEntry;
                _refreshHash = refreshHash;
                _toggleSelection = toggleSelection;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(440f, 292f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Quick Tools", EditorStyles.boldLabel);
                DrawQuickTools();
                DrawSeparator();
                DrawPreviewInformation();
                DrawSeparator();
                DrawOptionalTools();
            }

            private void DrawQuickTools()
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawQuickToolButton(
                        "d_TreeEditor.Trash",
                        "Delete this catalog entry",
                        _deleteEntry
                    );
                    DrawQuickToolButton(
                        "d_Refresh",
                        "Refresh the stored source hash",
                        _refreshHash
                    );
                    DrawQuickToolButton(
                        "d_FolderOpened Icon",
                        "Reveal the source file",
                        RevealSource
                    );
                    DrawQuickToolButton(
                        "d_FilterSelectedOnly",
                        "Select or deselect this catalog entry",
                        _toggleSelection
                    );
                }
            }

            private void DrawPreviewInformation()
            {
                EditorGUILayout.LabelField(
                    "Preview Information",
                    EditorStyles.boldLabel
                );
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.TextField("ID", _entry.Id);
                    EditorGUILayout.EnumPopup("Data Type", _entry.Type);
                    EditorGUILayout.TextField("Relative Path", _entry.RelativePath);
                    EditorGUILayout.TextField(
                        "Source",
                        Path.GetFileName(_entry.SourceFilePath)
                    );
                }

                EditorGUILayout.LabelField(
                    "Hash",
                    string.IsNullOrEmpty(_entry.ContentHash)
                        ? "Unavailable"
                        : _entry.ContentHash,
                    EditorStyles.wordWrappedMiniLabel
                );
            }

            private void DrawOptionalTools()
            {
                EditorGUILayout.LabelField("Optional Tools", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy ID"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _entry.Id;
                    }

                    if (GUILayout.Button("Copy Output Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _entry.RelativePath;
                    }

                    if (GUILayout.Button("Copy Source Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _entry.SourceFilePath;
                    }
                }
            }

            private void RevealSource()
            {
                if (!string.IsNullOrWhiteSpace(_entry.SourceFilePath))
                {
                    EditorUtility.RevealInFinder(_entry.SourceFilePath);
                }
            }

            private void DrawQuickToolButton(
                string iconName,
                string tooltip,
                Action action)
            {
                GUIContent icon = new GUIContent(
                    EditorGUIUtility.IconContent(iconName)
                );
                icon.tooltip = tooltip;
                if (GUILayout.Button(
                        icon,
                        EditorStyles.miniButton,
                        GUILayout.Width(32f)
                    ))
                {
                    editorWindow.Close();
                    action?.Invoke();
                }
            }

            private static void DrawSeparator()
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    GUIContent.none,
                    GUI.skin.horizontalSlider
                );
                EditorGUILayout.Space(4f);
            }
        }

        #endregion
    }
}
