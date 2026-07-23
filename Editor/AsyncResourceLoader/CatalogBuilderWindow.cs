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
        private bool _s3Foldout;
        private bool _isUploading;
        private float _uploadProgress;
        private string _uploadStatusMessage = "";

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

            if (_allConfigs.Count > 0)
            {
                _selectedConfigIndex = 0;
                SelectConfig(0);
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
            _searchFilter = "";
            _pageIndex = 0;
            _hashCache.Clear();
            InvalidateFilters();
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
            _config = newConfig;
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
                }
            }
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
                    entry.Id = EditorGUILayout.TextField(
                        entry.Id, GUILayout.Width(_colIdWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_config);
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
                    entry.RelativePath = EditorGUILayout.TextField(
                        entry.RelativePath,
                        EditorStyles.miniTextField,
                        GUILayout.Width(_colPathWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_config);
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
                // Generate Catalog (local only)
                using (new EditorGUI.DisabledGroupScope(
                    noEntries || _isUploading))
                {
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
                        GenerateCatalog();
                        UploadToS3();
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
                    s3.AccessKey = EditorGUILayout.TextField(
                        "Access Key", s3.AccessKey);

                    s3.SecretKey = EditorGUILayout.PasswordField(
                        "Secret Key", s3.SecretKey);

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
                    "All S3 fields are required. Fill in Endpoint, "
                    + "Region, Bucket, Access Key, and Secret Key.",
                    MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        private void TestS3Connection()
        {
            _isUploading = true;
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
            _uploadProgress = 0f;
            _uploadStatusMessage = "Preparing upload...";
            Repaint();

            S3Uploader.UploadDirectory(
                _config.S3,
                outputDir,
                () => false, // No cancel support for now
                (status, progress) =>
                {
                    _uploadStatusMessage = status;
                    _uploadProgress = progress;
                    Repaint();
                },
                result =>
                {
                    _isUploading = false;
                    _uploadProgress = 1f;
                    _uploadStatusMessage = "";

                    if (result.IsSuccess)
                    {
                        EditorUtility.DisplayDialog(
                            "Upload Complete",
                            result.Message,
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
                });
        }

        private string GetOutputDirectory()
        {
            return Path.Combine(
                _config.OutputFolder, _config.SubfolderName)
                .Replace('\\', '/');
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
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
                Repaint();
            }
        }

        #endregion

        #region Catalog Generation

        private void GenerateCatalog()
        {
            if (_config.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Files",
                    "No files are staged. Drag files first.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_config.OutputFolder))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Path",
                    "Output folder is not set.", "OK");
                return;
            }

            string targetFolder = Path.Combine(
                _config.OutputFolder, _config.SubfolderName)
                .Replace('\\', '/');

            // Detect changes and bump version
            bool anyChange = false;
            foreach (StagedCatalogEntry entry in _config.Entries)
            {
                string currentHash = ComputeHash(entry.SourceFilePath);
                if (currentHash != entry.ContentHash)
                {
                    anyChange = true;
                    entry.ContentHash = currentHash;
                }
            }

            if (anyChange)
            {
                Undo.RecordObject(_config, "Bump Catalog Version");
                _config.Version++;
            }

            // Ensure directories
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            if (!Directory.Exists(_config.OutputFolder))
            {
                Directory.CreateDirectory(_config.OutputFolder);
            }

            // Copy files
            int copied = 0;
            foreach (StagedCatalogEntry entry in _config.Entries)
            {
                if (!File.Exists(entry.SourceFilePath))
                {
                    Debug.LogWarning(
                        $"[Catalog] Source file missing: {entry.SourceFilePath}");
                    continue;
                }

                try
                {
                    string destPath = Path.Combine(
                        _config.OutputFolder, entry.RelativePath)
                        .Replace('\\', '/');

                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)
                        && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(entry.SourceFilePath, destPath, true);
                    copied++;
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[Catalog] Failed to copy '{entry.SourceFilePath}': {ex.Message}");
                }
            }

            // Generate catalog JSON
            string catalogJson = BuildCatalogJson();
            string catalogPath = Path.Combine(
                _config.OutputFolder, _config.CatalogFileName)
                .Replace('\\', '/');

            File.WriteAllText(catalogPath, catalogJson, Encoding.UTF8);

            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Catalog Generated",
                $"Version: {_config.Version}\n"
                + $"Files copied: {copied}/{_config.Entries.Count}\n"
                + $"Catalog: {catalogPath}",
                "OK");

            Repaint();
        }

        private string BuildCatalogJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"version\": {_config.Version},");
            sb.AppendLine("  \"entries\": [");

            for (int i = 0; i < _config.Entries.Count; i++)
            {
                StagedCatalogEntry entry = _config.Entries[i];
                string typeStr = entry.Type == DataType.Binary
                    ? "binary" : "text";
                string comma = i < _config.Entries.Count - 1
                    ? "," : "";

                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{EscapeJson(entry.Id)}\",");
                sb.AppendLine($"      \"type\": \"{typeStr}\",");
                sb.AppendLine(
                    $"      \"path\": \"{EscapeJson(entry.RelativePath)}\"");
                sb.Append($"    }}{comma}");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            return sb.ToString();
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
                        .ToLowerInvariant()
                        .Substring(0, 16);
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
