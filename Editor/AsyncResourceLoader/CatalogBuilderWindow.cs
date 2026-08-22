using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    public class CatalogBuilderWindow : EditorWindow
    {
        private const float DropZoneHeight = 90f;

        private CatalogBuilderConfig _config;
        private Vector2 _scrollPosition;
        private bool _isDraggingOver;
        private List<CatalogBuilderConfig> _allConfigs;
        private int _selectedConfigIndex = -1;
        private string _searchFilter = "";
        private int _pageIndex;
        private int _pageSize = 20;

        // Performance caches
        private readonly Dictionary<string, CachedHash> _hashCache
            = new Dictionary<string, CachedHash>();
        private List<StagedCatalogEntry> _filteredEntries;
        private string _lastSearchFilter;
        private int _lastEntriesCount;

        // Column widths
        private float _colIdWidth = 100f;
        private float _colPathWidth = 220f;

        // S3 upload state
        private bool _validationFoldout = true;
        private bool _s3Foldout;
        private bool _isUploading;
        private bool _canCancelUpload;
        private bool _cancelUploadRequested;
        private float _uploadProgress;
        private string _uploadStatusMessage = "";
        private CatalogValidationResult _lastValidation;

        private struct CachedHash
        {
            public string Hash;
            public long LastWriteTicks;
        }

        #region Menu

        [MenuItem("Dev Menu/Catalog/Builder")]
        public static void Open()
        {
            CatalogBuilderWindow window = GetWindow<CatalogBuilderWindow>(
                false, "Catalog Builder", true);
            window.minSize = new Vector2(540, 440);
            window.Show();
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            RefreshConfigList();
            InvalidateFilters();
        }

        private void OnGUI()
        {
            DrawConfigSelector();
            GUILayout.Space(6);

            if (_config == null)
            {
                EditorGUILayout.HelpBox(
                    "Select or create a Catalog Builder Config to get started.",
                    MessageType.Info);
                return;
            }

            DrawHeader();
            DrawOutputSettings();
            GUILayout.Space(8);
            DrawRuntimeTargetGuidance();
            GUILayout.Space(8);
            DrawValidationSummary();
            GUILayout.Space(8);
            DrawDropZone();
            GUILayout.Space(8);
            DrawStagedEntries();
            GUILayout.Space(8);
            DrawActions();
            GUILayout.Space(8);
            DrawS3Section();
        }

        #endregion

        #region Config Selector

        private void DrawConfigSelector()
        {
            if (_allConfigs == null)
            {
                RefreshConfigList();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Config", GUILayout.Width(48));

                using (new EditorGUI.ChangeCheckScope())
                {
                    string[] names = new string[_allConfigs.Count];
                    for (int i = 0; i < _allConfigs.Count; i++)
                    {
                        names[i] = _allConfigs[i].name;
                    }

                    _selectedConfigIndex = EditorGUILayout.Popup(
                        _selectedConfigIndex,
                        names,
                        GUILayout.ExpandWidth(true));

                    if (EditorGUI.EndChangeCheck())
                    {
                        SelectConfig(_selectedConfigIndex);
                    }
                }

                if (GUILayout.Button("+", EditorStyles.miniButton,
                        GUILayout.Width(26)))
                {
                    CreateNewConfig();
                }

                using (new EditorGUI.DisabledGroupScope(_config == null))
                {
                    if (GUILayout.Button("\u2715", EditorStyles.miniButton,
                            GUILayout.Width(26)))
                    {
                        DeleteSelectedConfig();
                    }
                }
            }
        }

        private void RefreshConfigList()
        {
            string[] guids = AssetDatabase.FindAssets("t:CatalogBuilderConfig");
            _allConfigs = new List<CatalogBuilderConfig>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CatalogBuilderConfig cfg
                    = AssetDatabase.LoadAssetAtPath<CatalogBuilderConfig>(path);
                if (cfg != null)
                {
                    _allConfigs.Add(cfg);
                }
            }

            _allConfigs.Sort((a, b)
                => string.Compare(a.name, b.name, StringComparison.Ordinal));

            int previousIndex = _config == null
                ? -1
                : _allConfigs.IndexOf(_config);
            if (_allConfigs.Count > 0)
            {
                SelectConfig(previousIndex >= 0 ? previousIndex : 0);
            }
            else
            {
                _selectedConfigIndex = -1;
                _config = null;
            }
        }

        private void SelectConfig(int index)
        {
            if (index < 0 || index >= _allConfigs.Count)
            {
                _config = null;
                return;
            }

            // Flush previous config before switching
            if (_config != null)
            {
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
            }

            _selectedConfigIndex = index;
            _config = _allConfigs[index];
            EnsureConfigInitialized();
            _searchFilter = "";
            _pageIndex = 0;
            _hashCache.Clear();
            InvalidateFilters();
            _lastValidation = null;
        }

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

        private void CreateNewConfig()
        {
            string savePath = EditorUtility.SaveFilePanelInProject(
                "Create Catalog Config",
                "CatalogBuilderConfig",
                "asset",
                "Choose where to save the new catalog config.");

            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            CatalogBuilderConfig newConfig
                = CreateInstance<CatalogBuilderConfig>();
            newConfig.name = Path.GetFileNameWithoutExtension(savePath);
            AssetDatabase.CreateAsset(newConfig, savePath);
            AssetDatabase.SaveAssets();

            RefreshConfigList();

            _selectedConfigIndex = _allConfigs.IndexOf(newConfig);
            SelectConfig(_selectedConfigIndex);
        }

        private void DeleteSelectedConfig()
        {
            if (_config == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Delete Config",
                    $"Delete '{_config.name}' permanently?\n\n"
                    + "This will delete the config asset but not the staged files.",
                    "Delete", "Cancel"))
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_config);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();

            RefreshConfigList();
        }

        #endregion

        #region Header

        private void DrawHeader()
        {
            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Catalog Builder", EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.ChangeCheckScope())
                {
                    int newVersion = EditorGUILayout.IntField(
                        "Version", _config.Version, GUILayout.Width(120));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Version");
                        _config.Version = newVersion;
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssets();
                    }
                }
            }

            GUILayout.Space(2);

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        #endregion

        #region Output Settings

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField(
                "Output Settings", EditorStyles.miniBoldLabel);

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

        #endregion

        #region Runtime Target Guidance

        private void DrawRuntimeTargetGuidance()
        {
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

        private void DrawValidationSummary()
        {
            _validationFoldout = EditorGUILayout.Foldout(
                _validationFoldout,
                "Catalog Validation",
                true);
            if (!_validationFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_lastValidation == null)
                {
                    EditorGUILayout.LabelField(
                        "Run validation before generating or uploading.",
                        EditorStyles.miniLabel);
                }
                else if (_lastValidation.IsValid)
                {
                    string state = _config.LastGenerated?.ManifestHash
                        == _lastValidation.ManifestHash
                        ? "No manifest changes since the last generation."
                        : "Manifest changes detected.";
                    EditorGUILayout.HelpBox(
                        $"Valid: {_lastValidation.ValidEntries.Count} entries. {state}",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        string.Join("\n", _lastValidation.Errors),
                        MessageType.Error);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Validate", GUILayout.Width(90)))
                    {
                        ValidateCatalog(true);
                    }

                    if (_config.LastGenerated?.HasBuild == true)
                    {
                        string hash = _config.LastGenerated.ManifestHash;
                        EditorGUILayout.LabelField(
                            $"Last build v{_config.LastGenerated.Version} · "
                            + hash.Substring(0, Mathf.Min(12, hash.Length)),
                            EditorStyles.miniLabel);
                    }
                }
            }
        }

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
            Rect dropArea = GUILayoutUtility.GetRect(0f, DropZoneHeight,
                GUILayout.ExpandWidth(true));

            Event evt = Event.current;
            Color originalColor = GUI.color;

            if (evt.type == EventType.DragUpdated
                || evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    _isDraggingOver = true;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        StageFiles(DragAndDrop.paths);
                        _isDraggingOver = false;
                    }

                    Event.current.Use();
                }
                else
                {
                    _isDraggingOver = false;
                }
            }
            else if (evt.type == EventType.DragExited)
            {
                _isDraggingOver = false;
            }

            Color bgColor = _isDraggingOver
                ? new Color(0.2f, 0.5f, 0.2f, 0.3f)
                : new Color(0.3f, 0.3f, 0.3f, 0.2f);
            Color borderColor = _isDraggingOver
                ? new Color(0.3f, 0.7f, 0.3f, 0.8f)
                : new Color(0.5f, 0.5f, 0.5f, 0.5f);

            EditorGUI.DrawRect(dropArea, bgColor);

            Handles.BeginGUI();
            Handles.color = borderColor;
            Handles.DrawLine(
                new Vector2(dropArea.x, dropArea.y),
                new Vector2(dropArea.x + dropArea.width, dropArea.y));
            Handles.DrawLine(
                new Vector2(dropArea.x, dropArea.y + dropArea.height),
                new Vector2(dropArea.x + dropArea.width,
                    dropArea.y + dropArea.height));
            Handles.DrawLine(
                new Vector2(dropArea.x, dropArea.y),
                new Vector2(dropArea.x, dropArea.y + dropArea.height));
            Handles.DrawLine(
                new Vector2(dropArea.x + dropArea.width, dropArea.y),
                new Vector2(dropArea.x + dropArea.width,
                    dropArea.y + dropArea.height));
            Handles.EndGUI();

            string message = _isDraggingOver
                ? "Release to stage files"
                : "Drop level files here  (.json, .bytes, .bin)";

            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 12,
                normal = { textColor = _isDraggingOver
                    ? Color.white : Color.gray }
            };

            GUI.Label(dropArea, message, labelStyle);
            GUI.color = originalColor;
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

        private void DrawStagedEntries()
        {
            // --- Header: count + search + clear ---
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"Staged ({_config.Entries.Count})",
                    EditorStyles.miniBoldLabel, GUILayout.Width(100));

                _searchFilter = EditorGUILayout.TextField(
                    _searchFilter,
                    EditorStyles.toolbarSearchField,
                    GUILayout.ExpandWidth(true));

                if (GUILayout.Button(
                        "Clear All", EditorStyles.miniButton,
                        GUILayout.Width(60)))
                {
                    _config.Entries.Clear();
                    _searchFilter = "";
                    _pageIndex = 0;
            _hashCache.Clear();
            _lastValidation = null;
            InvalidateFilters();
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssets();
                }
            }

            if (_config.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No files staged. Drag .json, .bytes, or .bin files here.",
                    MessageType.Info);
                return;
            }

            // --- Get cached filtered list ---
            List<StagedCatalogEntry> filtered = GetFilteredEntries();

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No entries match the search filter.",
                    MessageType.Info);
                return;
            }

            // --- Pagination bar ---
            int totalPages = Mathf.Max(
                1, Mathf.CeilToInt((float)filtered.Count / _pageSize));
            _pageIndex = Mathf.Clamp(_pageIndex, 0, totalPages - 1);

            using (new EditorGUILayout.HorizontalScope(
                EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledGroupScope(_pageIndex <= 0))
                {
                    if (GUILayout.Button("<", EditorStyles.toolbarButton,
                            GUILayout.Width(26)))
                    {
                        _pageIndex--;
                    }
                }

                using (new EditorGUI.DisabledGroupScope(
                    _pageIndex >= totalPages - 1))
                {
                    if (GUILayout.Button(">", EditorStyles.toolbarButton,
                            GUILayout.Width(26)))
                    {
                        _pageIndex++;
                    }
                }

                EditorGUILayout.LabelField(
                    $"| Page {_pageIndex + 1} / {totalPages}",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(110));

                EditorGUILayout.LabelField(
                    "| Page size",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(55));

                _pageSize = EditorGUILayout.IntField(
                    _pageSize, GUILayout.Width(40));
                _pageSize = Mathf.Clamp(_pageSize, 5, 500);

                GUILayout.FlexibleSpace();
            }

            // --- Column header ---
            using (new EditorGUILayout.HorizontalScope(
                EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(
                    "ID", EditorStyles.boldLabel,
                    GUILayout.Width(_colIdWidth));
                EditorGUILayout.LabelField(
                    "Type", EditorStyles.boldLabel,
                    GUILayout.Width(38));
                EditorGUILayout.LabelField(
                    "Relative Path", EditorStyles.boldLabel,
                    GUILayout.Width(_colPathWidth));
                EditorGUILayout.LabelField(
                    "", EditorStyles.boldLabel, GUILayout.Width(20));
                EditorGUILayout.LabelField(
                    "Source", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("", GUILayout.Width(22));
            }

            // --- Entries scroll ---
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition);

            int start = _pageIndex * _pageSize;
            int end = Mathf.Min(start + _pageSize, filtered.Count);

            for (int i = start; i < end; i++)
            {
                StagedCatalogEntry entry = filtered[i];
                int realIndex = _config.Entries.IndexOf(entry);
                DrawStagedEntry(entry, realIndex);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStagedEntry(StagedCatalogEntry entry, int index)
        {
            using (new EditorGUILayout.HorizontalScope(
                EditorStyles.helpBox))
            {
                // Editable ID
                using (new EditorGUI.ChangeCheckScope())
                {
                    string newId = EditorGUILayout.TextField(
                        entry.Id, GUILayout.Width(_colIdWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_config, "Change Catalog Entry ID");
                        entry.Id = newId;
                        EditorUtility.SetDirty(_config);
                        InvalidateFilters();
                        _lastValidation = null;
                    }
                }

                // Type badge
                string typeLabel = entry.Type == DataType.Binary
                    ? "BIN" : "TXT";
                Color typeColor = entry.Type == DataType.Binary
                    ? new Color(0.8f, 0.3f, 0.8f)
                    : new Color(0.3f, 0.7f, 0.3f);
                Color prevContent = GUI.contentColor;
                GUI.contentColor = typeColor;
                EditorGUILayout.LabelField(
                    typeLabel, EditorStyles.boldLabel,
                    GUILayout.Width(38));
                GUI.contentColor = prevContent;

                // Editable relative path
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

                // Hash status icon
                string currentHash = GetCachedHash(entry.SourceFilePath);
                bool fileMissing = string.IsNullOrEmpty(currentHash);

                Color statusColor;
                string statusIcon;
                string statusTooltip;

                if (fileMissing)
                {
                    statusColor = Color.red;
                    statusIcon = "\u2715";
                    statusTooltip = "Source file missing";
                }
                else if (currentHash != entry.ContentHash)
                {
                    statusColor = new Color(0.9f, 0.7f, 0.1f);
                    statusIcon = "\u2022";
                    statusTooltip = "File changed since last bake";
                }
                else
                {
                    statusColor = new Color(0.4f, 0.8f, 0.4f);
                    statusIcon = "\u2713";
                    statusTooltip = "Up to date";
                }

                GUI.contentColor = statusColor;
                EditorGUILayout.LabelField(
                    new GUIContent(statusIcon, statusTooltip),
                    EditorStyles.boldLabel, GUILayout.Width(20));
                GUI.contentColor = prevContent;

                // Source filename
                string sourceName = Path.GetFileName(entry.SourceFilePath);
                EditorGUILayout.LabelField(
                    new GUIContent(sourceName, entry.SourceFilePath),
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                // Delete button
                if (GUILayout.Button("\u2715", EditorStyles.miniButton,
                        GUILayout.Width(22)))
                {
                    _config.Entries.RemoveAt(index);
                    _hashCache.Remove(entry.SourceFilePath);
                    InvalidateFilters();
                    _lastValidation = null;
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssets();
                }
            }
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
            Rect divRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(divRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            bool noEntries = _config.Entries.Count == 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(
                    noEntries || _isUploading))
                {
                    if (GUILayout.Button("Validate",
                            GUILayout.Height(30)))
                    {
                        ValidateCatalog(true);
                    }

                    if (GUILayout.Button("Generate Catalog",
                            GUILayout.Height(30)))
                    {
                        GenerateCatalog();
                    }
                }

                // Upload to S3
                using (new EditorGUI.DisabledGroupScope(
                    noEntries || _isUploading || !_config.S3.IsValid))
                {
                    if (GUILayout.Button("Upload to S3",
                            GUILayout.Height(30)))
                    {
                        if (GenerateCatalog())
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
            _s3Foldout = EditorGUILayout.Foldout(
                _s3Foldout, "S3 Upload Settings", true);

            if (!_s3Foldout)
            {
                return;
            }

            EditorGUI.indentLevel++;

            S3UploadSettings s3 = _config.S3;

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

            EditorGUI.indentLevel--;
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
                string id = ExtractId(filePath);
                DataType type = DetectType(filePath);
                string relativePath = $"{_config.SubfolderName}/{fileName}";
                string hash = GetCachedHash(filePath);

                _config.Entries.RemoveAll(
                    e => e.SourceFilePath == filePath);

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
                        generatedVersion);
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

        private static DataType DetectType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".json"
                ? DataType.Text
                : DataType.Binary;
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
    }
}
