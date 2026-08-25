using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Editor;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor.Validation;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor
{
    [CustomEditor(typeof(PuzzleLevelReferenceTable))]
    internal sealed class PuzzleLevelReferenceTableEditor : UnityEditor.Editor
    {
        #region Constants

        private const int DefaultPageSize = 20;
        private const int MinimumPageSize = 5;
        private const int MaximumPageSize = 500;
        private const float SelectionColumnWidth = 22f;
        private const float IdColumnWidth = 150f;
        private const float TypeColumnWidth = 76f;
        private const float ValidColumnWidth = 84f;
        private const float DropAreaHeight = 46f;
        private const int MaximumLevelDataLength = 262144;

        #endregion

        #region Serialized Fields

        [SerializeField]
        private string _searchFilter = string.Empty;

        [SerializeField]
        private int _pageIndex;

        [SerializeField]
        private int _pageSize = DefaultPageSize;

        [SerializeField]
        private Vector2 _scrollPosition;

        #endregion

        #region Private Fields

        private SerializedProperty _entriesProperty;
        private SerializedProperty _enableEntryAutoIdProperty;
        private SerializedProperty _entryAutoIdTemplateProperty;
        private int _focusedEntryIndex = -1;
        private int _batchMoveTargetIndex = 1;
        private readonly HashSet<int> _selectedEntryIndices = new HashSet<int>();

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            _entriesProperty = serializedObject.FindProperty("_entries");
            _enableEntryAutoIdProperty = serializedObject.FindProperty(
                "_enableEntryAutoId");
            _entryAutoIdTemplateProperty = serializedObject.FindProperty(
                "_entryAutoIdTemplate");
            Undo.undoRedoPerformed += HandleProjectStateChanged;
            EditorApplication.projectChanged += HandleProjectStateChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleProjectStateChanged;
            EditorApplication.projectChanged -= HandleProjectStateChanged;
        }

        public override void OnInspectorGUI()
        {
            if (_entriesProperty == null)
            {
                return;
            }

            serializedObject.Update();
            PruneSelectedEntries();
            bool refreshIdsRequested = DrawAutoIdControls();
            DrawDropToAddArea();
            DrawSearchAndFilterToolbar();

            List<int> filteredIndices = GetFilteredIndices();
            Dictionary<string, List<int>> idIndices = GetIdIndices();
            DrawEntries(filteredIndices, idIndices);

            bool propertiesChanged = serializedObject.ApplyModifiedProperties();
            if (propertiesChanged)
            {
                PuzzleLevelValidationService.InvalidateAll();
                Repaint();
            }

            if (refreshIdsRequested)
            {
                RefreshEntryIds();
            }
        }

        #endregion

        #region Private Methods

        private bool DrawAutoIdControls()
        {
            if (_enableEntryAutoIdProperty == null
                || _entryAutoIdTemplateProperty == null)
            {
                return false;
            }

            bool refreshRequested = false;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(
                        _enableEntryAutoIdProperty,
                        new GUIContent("Entry Auto Id"),
                        GUILayout.Width(170f));

                    using (new EditorGUI.DisabledGroupScope(
                               !_enableEntryAutoIdProperty.boolValue))
                    {
                        EditorGUILayout.PropertyField(
                            _entryAutoIdTemplateProperty,
                            new GUIContent("Template"));
                        refreshRequested = GUILayout.Button(
                            "Refresh IDs",
                            GUILayout.Width(100f));
                    }
                }
            }

            return refreshRequested;
        }

        private void DrawSearchAndFilterToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(
                    $"Levels ({_entriesProperty.arraySize})",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(86f));

                string newFilter = EditorGUILayout.TextField(
                    _searchFilter,
                    EditorStyles.toolbarSearchField);
                if (!string.Equals(newFilter, _searchFilter, StringComparison.Ordinal))
                {
                    _searchFilter = newFilter;
                    _pageIndex = 0;
                }

                if (!string.IsNullOrEmpty(_searchFilter)
                    && GUILayout.Button("Clear", EditorStyles.toolbarButton,
                        GUILayout.Width(42f)))
                {
                    _searchFilter = string.Empty;
                    _pageIndex = 0;
                }

                if (GUILayout.Button("Validate All", EditorStyles.toolbarButton,
                        GUILayout.Width(76f)))
                {
                    ValidateAllEntries();
                }

                if (GUILayout.Button("+", EditorStyles.toolbarButton,
                        GUILayout.Width(24f)))
                {
                    AddEntry();
                }
            }
        }

        private void DrawDropToAddArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(
                GUIContent.none,
                EditorStyles.helpBox,
                GUILayout.Height(DropAreaHeight),
                GUILayout.ExpandWidth(true)
            );
            GUI.Box(dropArea, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(
                dropArea,
                "Drop TextAsset files here to add level entries",
                EditorStyles.centeredGreyMiniLabel
            );

            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = HasDraggedTextAssets()
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            List<TextAsset> assets = GetDraggedTextAssets();
            if (assets.Count == 0)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            AddEntries(assets);
            currentEvent.Use();
        }

        private void DrawEntries(
            List<int> filteredIndices,
            Dictionary<string, List<int>> idIndices)
        {
            if (_entriesProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add level TextAssets to create reference entries.",
                    MessageType.Info);
                return;
            }

            if (filteredIndices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No level entries match the current filter.",
                    MessageType.Info);
                return;
            }

            int totalPages = Mathf.Max(
                1,
                Mathf.CeilToInt((float)filteredIndices.Count / _pageSize));
            _pageIndex = Mathf.Clamp(_pageIndex, 0, totalPages - 1);
            int startIndex = _pageIndex * _pageSize;
            int endIndex = Mathf.Min(startIndex + _pageSize, filteredIndices.Count);
            DrawPagination(totalPages, filteredIndices, startIndex, endIndex);
            DrawColumnHeaders(filteredIndices, startIndex, endIndex);
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.MaxHeight(420f));
            for (int i = startIndex; i < endIndex; i++)
            {
                DrawEntryRow(filteredIndices[i], idIndices);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPagination(
            int totalPages,
            IReadOnlyList<int> filteredIndices,
            int startIndex,
            int endIndex)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledGroupScope(_pageIndex == 0))
                {
                    if (GUILayout.Button("<", EditorStyles.toolbarButton,
                            GUILayout.Width(24f)))
                    {
                        _pageIndex--;
                    }
                }

                using (new EditorGUI.DisabledGroupScope(
                           _pageIndex >= totalPages - 1))
                {
                    if (GUILayout.Button(">", EditorStyles.toolbarButton,
                            GUILayout.Width(24f)))
                    {
                        _pageIndex++;
                    }
                }

                EditorGUILayout.LabelField(
                    $"{_pageIndex + 1}/{totalPages}",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(48f));
                EditorGUILayout.LabelField(
                    "Size",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(28f));
                _pageSize = Mathf.Clamp(
                    EditorGUILayout.IntField(_pageSize, GUILayout.Width(36f)),
                    MinimumPageSize,
                    MaximumPageSize);

                if (GUILayout.Button(
                        new GUIContent("All", "Select all filtered entries"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(28f)
                    ))
                {
                    SelectEntries(filteredIndices);
                }

                using (new EditorGUI.DisabledGroupScope(
                           _selectedEntryIndices.Count == 0))
                {
                    if (GUILayout.Button(
                            new GUIContent("None", "Clear the entry selection"),
                            EditorStyles.toolbarButton,
                            GUILayout.Width(38f)
                        ))
                    {
                        _selectedEntryIndices.Clear();
                    }
                }

                EditorGUILayout.LabelField(
                    _selectedEntryIndices.Count.ToString(),
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(20f)
                );
                DrawBatchOperations();
            }
        }

        private void DrawColumnHeaders(
            IReadOnlyList<int> filteredIndices,
            int startIndex,
            int endIndex)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool pageSelected = AreEntriesSelected(
                    filteredIndices,
                    startIndex,
                    endIndex
                );
                bool updatedPageSelected = GUILayout.Toggle(
                    pageSelected,
                    new GUIContent("✓", "Select entries on this page"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(SelectionColumnWidth)
                );
                if (updatedPageSelected != pageSelected)
                {
                    SetPageSelection(
                        filteredIndices,
                        startIndex,
                        endIndex,
                        updatedPageSelected
                    );
                }

                EditorGUILayout.LabelField(
                    "Id",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(IdColumnWidth));
                EditorGUILayout.LabelField(
                    "Asset",
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    "Type",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(TypeColumnWidth));
                EditorGUILayout.LabelField(
                    "Valid",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(ValidColumnWidth));
            }
        }

        private void DrawEntryRow(
            int index,
            IReadOnlyDictionary<string, List<int>> idIndices)
        {
            SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(
                index);
            SerializedProperty idProperty = entryProperty.FindPropertyRelative("Id");
            SerializedProperty assetProperty = entryProperty.FindPropertyRelative("Asset");
            SerializedProperty dataTypeProperty = entryProperty.FindPropertyRelative(
                "DataType");
            string levelId = idProperty.stringValue;
            TextAsset asset = assetProperty.objectReferenceValue as TextAsset;
            DataType dataType = (DataType)dataTypeProperty.intValue;
            PuzzleLevelValidationRequest request
                = PuzzleLevelValidationService.CreateRequest(levelId, asset, dataType);
            PuzzleLevelValidationResult validation
                = PuzzleLevelValidationService.Validate(request);
            List<PuzzleLevelValidationDiagnostic> diagnostics
                = new List<PuzzleLevelValidationDiagnostic>(validation.Diagnostics);
            AddDuplicateIdDiagnostic(levelId, index, idIndices, diagnostics);

            Color previousColor = GUI.backgroundColor;
            if (_focusedEntryIndex == index)
            {
                GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
            }

            EditorGUILayout.HorizontalScope rowScope = new EditorGUILayout.HorizontalScope(
                EditorStyles.helpBox
            );
            using (rowScope)
            {
                bool isSelected = _selectedEntryIndices.Contains(index);
                bool updatedIsSelected = GUILayout.Toggle(
                    isSelected,
                    GUIContent.none,
                    GUILayout.Width(SelectionColumnWidth)
                );
                if (updatedIsSelected != isSelected)
                {
                    SetEntrySelection(index, updatedIsSelected);
                }

                EditorGUILayout.PropertyField(
                    idProperty,
                    GUIContent.none,
                    GUILayout.Width(IdColumnWidth));
                EditorGUILayout.PropertyField(assetProperty, GUIContent.none);
                EditorGUILayout.PropertyField(
                    dataTypeProperty,
                    GUIContent.none,
                    GUILayout.Width(TypeColumnWidth));
                DrawValidationStatus(validation, diagnostics);
            }
            GUI.backgroundColor = previousColor;
            HandleEntryContextClick(
                rowScope.rect,
                index,
                levelId,
                asset,
                dataType
            );

        }

        private void DrawValidationStatus(
            PuzzleLevelValidationResult validation,
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
        {
            bool hasError = HasSeverity(
                diagnostics,
                PuzzleLevelValidationSeverity.Error);
            bool hasWarning = HasSeverity(
                diagnostics,
                PuzzleLevelValidationSeverity.Warning);
            string label = GetValidationLabel(hasError, hasWarning, diagnostics);
            Color previousColor = GUI.contentColor;
            GUI.contentColor = hasError
                ? new Color(0.88f, 0.3f, 0.3f)
                : hasWarning
                    ? new Color(0.92f, 0.7f, 0.2f)
                    : new Color(0.35f, 0.8f, 0.4f);
            GUIContent content = new GUIContent(
                $"● {label}",
                CreateValidationTooltip(validation, diagnostics)
            );
            Rect statusRect = GUILayoutUtility.GetRect(
                content,
                EditorStyles.miniBoldLabel,
                GUILayout.Width(ValidColumnWidth)
            );
            EditorGUI.LabelField(statusRect, content, EditorStyles.miniBoldLabel);
            EditorGUIUtility.AddCursorRect(statusRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && statusRect.Contains(Event.current.mousePosition))
            {
                CustomPopupDropdown.Show(
                    statusRect,
                    new PuzzleLevelValidationPopup(validation, diagnostics)
                );
                Event.current.Use();
            }
            GUI.contentColor = previousColor;
        }

        private static string GetValidationLabel(
            bool hasError,
            bool hasWarning,
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
        {
            if (hasError)
            {
                return "Error";
            }

            if (!hasWarning)
            {
                return "Valid";
            }

            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Code == "VALIDATOR_UNAVAILABLE")
                {
                    return "No validator";
                }
            }

            return "Warning";
        }

        private List<int> GetFilteredIndices()
        {
            List<int> filteredIndices = new List<int>();
            string filter = _searchFilter?.Trim();
            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(i);
                string id = entry.FindPropertyRelative("Id").stringValue;
                TextAsset asset = entry.FindPropertyRelative("Asset")
                    .objectReferenceValue as TextAsset;
                if (MatchesFilter(filter, id, asset))
                {
                    filteredIndices.Add(i);
                }
            }

            return filteredIndices;
        }

        private Dictionary<string, List<int>> GetIdIndices()
        {
            Dictionary<string, List<int>> idIndices
                = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                string id = _entriesProperty.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Id").stringValue;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!idIndices.TryGetValue(id, out List<int> indices))
                {
                    indices = new List<int>();
                    idIndices[id] = indices;
                }

                indices.Add(i);
            }

            return idIndices;
        }

        private static bool MatchesFilter(
            string filter,
            string levelId,
            TextAsset asset)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(levelId)
                && levelId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return asset != null
                && asset.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddDuplicateIdDiagnostic(
            string levelId,
            int index,
            IReadOnlyDictionary<string, List<int>> idIndices,
            ICollection<PuzzleLevelValidationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(levelId)
                || !idIndices.TryGetValue(levelId, out List<int> indices)
                || indices.Count < 2)
            {
                return;
            }

            StringBuilder rowNumbers = new StringBuilder();
            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] == index)
                {
                    continue;
                }

                if (rowNumbers.Length > 0)
                {
                    rowNumbers.Append(", ");
                }

                rowNumbers.Append(indices[i] + 1);
            }

            diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                "ENTRY_ID_DUPLICATE",
                PuzzleLevelValidationSeverity.Error,
                $"Level ID '{levelId}' is also used by row(s) {rowNumbers}."));
        }

        private static bool HasSeverity(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            PuzzleLevelValidationSeverity severity)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreateValidationTooltip(
            PuzzleLevelValidationResult validation,
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
        {
            StringBuilder tooltip = new StringBuilder();
            if (!string.IsNullOrEmpty(validation.ValidatorName))
            {
                tooltip.Append("Validator: ");
                tooltip.AppendLine(validation.ValidatorName);
            }

            if (!string.IsNullOrEmpty(validation.ContentHash))
            {
                int hashLength = Mathf.Min(12, validation.ContentHash.Length);
                tooltip.Append("Hash: ");
                tooltip.AppendLine(validation.ContentHash.Substring(0, hashLength));
            }

            for (int i = 0; i < diagnostics.Count; i++)
            {
                tooltip.Append('[');
                tooltip.Append(diagnostics[i].Code);
                tooltip.Append("] ");
                tooltip.AppendLine(diagnostics[i].Message);
            }

            return tooltip.Length == 0 ? "Validated" : tooltip.ToString();
        }

        private void HandleEntryContextClick(
            Rect rowRect,
            int index,
            string levelId,
            TextAsset asset,
            DataType dataType)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.ContextClick
                || !rowRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            OpenEntryContextPopup(
                new Rect(currentEvent.mousePosition, Vector2.one),
                index,
                levelId,
                asset,
                dataType
            );
            currentEvent.Use();
        }

        private void OpenEntryContextPopup(
            Rect position,
            int index,
            string levelId,
            TextAsset asset,
            DataType dataType)
        {
            bool isSelected = _selectedEntryIndices.Contains(index);
            bool canMoveSelection = _selectedEntryIndices.Count > 0 && !isSelected;
            CustomPopupDropdown.Show(
                position,
                new PuzzleLevelReferenceTableEntryPopup(
                    levelId,
                    asset,
                    dataType,
                    index + 1,
                    _entriesProperty.arraySize,
                    targetIndex => MoveEntry(index, targetIndex),
                    () => DeleteEntry(index, levelId, asset),
                    () => RevalidateEntry(index),
                    asset == null ? null : () => { AssetDatabase.OpenAsset(asset); },
                    () => SetEntrySelection(index, !isSelected),
                    canMoveSelection ? () => MoveSelectedEntriesAbove(index) : null,
                    canMoveSelection ? () => MoveSelectedEntriesBelow(index) : null,
                    () => OpenLevelDataEditor(position, levelId, asset, dataType)
                )
            );
        }

        private static void OpenLevelDataEditor(
            Rect position,
            string levelId,
            TextAsset asset,
            DataType dataType)
        {
            EditorApplication.delayCall += () =>
            {
                PuzzleLevelValidationRequest request
                    = PuzzleLevelValidationService.CreateRequest(
                        levelId,
                        asset,
                        dataType
                    );
                PuzzleLevelValidationResult validation
                    = PuzzleLevelValidationService.Validate(request);
                CustomPopupDropdown.Show(
                    position,
                    new PuzzleLevelDataEditorPopup(request, validation)
                );
            };
        }

        private void RevalidateEntry(int index)
        {
            FocusEntry(index);
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void MoveEntry(int sourceIndex, int oneBasedTargetIndex)
        {
            serializedObject.Update();
            int targetIndex = Mathf.Clamp(
                oneBasedTargetIndex - 1,
                0,
                _entriesProperty.arraySize - 1);
            if (sourceIndex == targetIndex)
            {
                return;
            }

            Undo.RecordObject(target, "Move Puzzle Level Entry");
            _entriesProperty.MoveArrayElement(sourceIndex, targetIndex);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            UpdateSelectedEntriesAfterMove(sourceIndex, targetIndex);
            UpdateFocusedEntryAfterMove(sourceIndex, targetIndex);
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void AddEntry()
        {
            AddEntries(null);
        }

        private void AddEntries(IReadOnlyList<TextAsset> assets)
        {
            serializedObject.Update();
            Undo.RecordObject(
                target,
                assets == null
                    ? "Add Puzzle Level Entry"
                    : "Add Puzzle Level Entries"
            );
            int firstAddedIndex = _entriesProperty.arraySize;
            int entryCount = assets?.Count ?? 1;
            for (int index = 0; index < entryCount; index++)
            {
                _entriesProperty.arraySize++;
                SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(
                    _entriesProperty.arraySize - 1
                );
                entry.FindPropertyRelative("Id").stringValue = string.Empty;
                entry.FindPropertyRelative("Asset").objectReferenceValue = assets?[index];
                entry.FindPropertyRelative("DataType").intValue = (int)DetectDataType(
                    assets?[index]
                );
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            FocusEntry(firstAddedIndex);
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void DeleteEntry(int index, string levelId, TextAsset asset)
        {
            string assetName = asset == null ? "missing asset" : asset.name;
            if (!EditorUtility.DisplayDialog(
                    "Delete Puzzle Level Entry",
                    $"Remove '{levelId}' ({assetName}) from this reference table?\n\n"
                    + "The source TextAsset will not be deleted.",
                    "Delete Entry",
                    "Cancel"))
            {
                return;
            }

            serializedObject.Update();
            Undo.RecordObject(target, "Delete Puzzle Level Entry");
            _entriesProperty.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _selectedEntryIndices.Remove(index);
            UpdateSelectedEntriesAfterDelete(index);
            UpdateFocusedEntryAfterDelete(index);
            _pageIndex = 0;
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void FocusEntry(int index)
        {
            _focusedEntryIndex = index;
            _searchFilter = string.Empty;
            _pageIndex = index / _pageSize;
            _scrollPosition = Vector2.zero;
        }

        private static bool HasDraggedTextAssets()
        {
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            for (int index = 0; index < objects.Length; index++)
            {
                if (objects[index] is TextAsset)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<TextAsset> GetDraggedTextAssets()
        {
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            List<TextAsset> assets = new List<TextAsset>(objects.Length);
            for (int index = 0; index < objects.Length; index++)
            {
                if (objects[index] is TextAsset asset)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        private static DataType DetectDataType(TextAsset asset)
        {
            if (asset == null)
            {
                return DataType.Unknown;
            }

            string extension = Path.GetExtension(AssetDatabase.GetAssetPath(asset))
                .ToLowerInvariant();
            switch (extension)
            {
                case ".json":
                case ".txt":
                case ".xml":
                case ".csv":
                case ".yaml":
                case ".yml":
                case ".ini":
                    return DataType.Text;
                default:
                    return DataType.Binary;
            }
        }

        private void DrawBatchOperations()
        {
            int selectedEntryCount = _selectedEntryIndices.Count;
            int maximumMoveTarget = Mathf.Max(
                1,
                _entriesProperty.arraySize - selectedEntryCount + 1
            );
            using (new EditorGUI.DisabledGroupScope(selectedEntryCount == 0))
            {
                EditorGUILayout.LabelField(
                    "To",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(20f)
                );
                _batchMoveTargetIndex = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        _batchMoveTargetIndex,
                        GUILayout.Width(34f)
                    ),
                    1,
                    maximumMoveTarget
                );
                if (GUILayout.Button(
                        new GUIContent(
                            "Go",
                            "Move selected entries to the specified row"
                        ),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(24f)
                    ))
                {
                    MoveSelectedEntries(_batchMoveTargetIndex);
                }

                if (GUILayout.Button(
                        new GUIContent("Delete", "Delete selected entries"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(44f)
                    ))
                {
                    DeleteSelectedEntries();
                }
            }
        }

        private bool AreEntriesSelected(
            IReadOnlyList<int> entries,
            int startIndex,
            int endIndex)
        {
            if (startIndex >= endIndex)
            {
                return false;
            }

            for (int index = startIndex; index < endIndex; index++)
            {
                if (!_selectedEntryIndices.Contains(entries[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void SetPageSelection(
            IReadOnlyList<int> entries,
            int startIndex,
            int endIndex,
            bool isSelected)
        {
            for (int index = startIndex; index < endIndex; index++)
            {
                SetEntrySelection(entries[index], isSelected);
            }
        }

        private void SelectEntries(IReadOnlyList<int> entries)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                _selectedEntryIndices.Add(entries[index]);
            }
        }

        private void SetEntrySelection(int index, bool isSelected)
        {
            if (isSelected)
            {
                _selectedEntryIndices.Add(index);
                return;
            }

            _selectedEntryIndices.Remove(index);
        }

        private void MoveSelectedEntries(int oneBasedTargetIndex)
        {
            List<int> selectedIndices = GetSelectedEntryIndices();
            if (selectedIndices.Count == 0)
            {
                return;
            }

            int targetIndex = Mathf.Clamp(
                oneBasedTargetIndex - 1,
                0,
                _entriesProperty.arraySize - selectedIndices.Count
            );
            MoveSelectedEntriesToIndex(selectedIndices, targetIndex);
        }

        private void MoveSelectedEntriesAbove(int targetIndex)
        {
            MoveSelectedEntriesRelativeToEntry(targetIndex, false);
        }

        private void MoveSelectedEntriesBelow(int targetIndex)
        {
            MoveSelectedEntriesRelativeToEntry(targetIndex, true);
        }

        private void MoveSelectedEntriesRelativeToEntry(
            int targetIndex,
            bool moveBelow)
        {
            List<int> selectedIndices = GetSelectedEntryIndices();
            if (selectedIndices.Count == 0 || selectedIndices.Contains(targetIndex))
            {
                return;
            }

            int movedEntriesBeforeTarget = CountSelectedEntriesBefore(
                selectedIndices,
                targetIndex
            );
            int destinationIndex = targetIndex - movedEntriesBeforeTarget;
            if (moveBelow)
            {
                destinationIndex++;
            }

            MoveSelectedEntriesToIndex(selectedIndices, destinationIndex);
        }

        private static int CountSelectedEntriesBefore(
            IReadOnlyList<int> selectedIndices,
            int targetIndex)
        {
            int count = 0;
            for (int index = 0; index < selectedIndices.Count; index++)
            {
                if (selectedIndices[index] >= targetIndex)
                {
                    return count;
                }

                count++;
            }

            return count;
        }

        private void MoveSelectedEntriesToIndex(
            IReadOnlyList<int> selectedIndices,
            int targetIndex)
        {
            targetIndex = Mathf.Clamp(
                targetIndex,
                0,
                _entriesProperty.arraySize - selectedIndices.Count
            );
            serializedObject.Update();
            Undo.RecordObject(target, "Move Puzzle Level Entries");
            MoveSelectedEntriesToEnd(selectedIndices);
            MoveSelectedEntriesToTarget(selectedIndices.Count, targetIndex);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            SetSelectionAfterMove(targetIndex, selectedIndices.Count);
            FocusEntry(targetIndex);
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void MoveSelectedEntriesToEnd(IReadOnlyList<int> selectedIndices)
        {
            int lastIndex = _entriesProperty.arraySize - 1;
            for (int index = 0; index < selectedIndices.Count; index++)
            {
                _entriesProperty.MoveArrayElement(
                    selectedIndices[index] - index,
                    lastIndex
                );
            }
        }

        private void MoveSelectedEntriesToTarget(int selectedEntryCount, int targetIndex)
        {
            int selectedBlockStart = _entriesProperty.arraySize - selectedEntryCount;
            for (int index = 0; index < selectedEntryCount; index++)
            {
                _entriesProperty.MoveArrayElement(
                    selectedBlockStart + index,
                    targetIndex + index
                );
            }
        }

        private void DeleteSelectedEntries()
        {
            List<int> selectedIndices = GetSelectedEntryIndices();
            if (selectedIndices.Count == 0
                || !EditorUtility.DisplayDialog(
                    "Delete Puzzle Level Entries",
                    $"Remove {selectedIndices.Count} selected entry or entries?\n\n"
                    + "The source TextAssets will not be deleted.",
                    "Delete Entries",
                    "Cancel"
                ))
            {
                return;
            }

            serializedObject.Update();
            Undo.RecordObject(target, "Delete Puzzle Level Entries");
            for (int index = selectedIndices.Count - 1; index >= 0; index--)
            {
                _entriesProperty.DeleteArrayElementAtIndex(selectedIndices[index]);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _selectedEntryIndices.Clear();
            _focusedEntryIndex = -1;
            _pageIndex = 0;
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private List<int> GetSelectedEntryIndices()
        {
            PruneSelectedEntries();
            List<int> selectedIndices = new List<int>(_selectedEntryIndices);
            selectedIndices.Sort();
            return selectedIndices;
        }

        private void SetSelectionAfterMove(int startIndex, int selectedEntryCount)
        {
            _selectedEntryIndices.Clear();
            for (int index = 0; index < selectedEntryCount; index++)
            {
                _selectedEntryIndices.Add(startIndex + index);
            }
        }

        private void UpdateSelectedEntriesAfterDelete(int deletedIndex)
        {
            if (_selectedEntryIndices.Count == 0)
            {
                return;
            }

            List<int> selectedIndices = new List<int>(_selectedEntryIndices);
            _selectedEntryIndices.Clear();
            for (int index = 0; index < selectedIndices.Count; index++)
            {
                int selectedIndex = selectedIndices[index];
                if (selectedIndex > deletedIndex)
                {
                    selectedIndex--;
                }

                _selectedEntryIndices.Add(selectedIndex);
            }
        }

        private void UpdateSelectedEntriesAfterMove(int sourceIndex, int targetIndex)
        {
            if (_selectedEntryIndices.Count == 0)
            {
                return;
            }

            List<int> selectedIndices = new List<int>(_selectedEntryIndices);
            _selectedEntryIndices.Clear();
            for (int index = 0; index < selectedIndices.Count; index++)
            {
                _selectedEntryIndices.Add(GetMovedEntryIndex(
                    selectedIndices[index],
                    sourceIndex,
                    targetIndex
                ));
            }
        }

        private static int GetMovedEntryIndex(
            int index,
            int sourceIndex,
            int targetIndex)
        {
            if (index == sourceIndex)
            {
                return targetIndex;
            }

            if (sourceIndex < targetIndex
                && index > sourceIndex
                && index <= targetIndex)
            {
                return index - 1;
            }

            if (sourceIndex > targetIndex
                && index >= targetIndex
                && index < sourceIndex)
            {
                return index + 1;
            }

            return index;
        }

        private void PruneSelectedEntries()
        {
            _selectedEntryIndices.RemoveWhere(
                index => index < 0 || index >= _entriesProperty.arraySize
            );
        }

        private void UpdateFocusedEntryAfterMove(int sourceIndex, int targetIndex)
        {
            if (_focusedEntryIndex == sourceIndex)
            {
                _focusedEntryIndex = targetIndex;
                return;
            }

            if (sourceIndex < targetIndex
                && _focusedEntryIndex > sourceIndex
                && _focusedEntryIndex <= targetIndex)
            {
                _focusedEntryIndex--;
                return;
            }

            if (sourceIndex > targetIndex
                && _focusedEntryIndex >= targetIndex
                && _focusedEntryIndex < sourceIndex)
            {
                _focusedEntryIndex++;
            }
        }

        private void UpdateFocusedEntryAfterDelete(int deletedIndex)
        {
            if (_focusedEntryIndex == deletedIndex)
            {
                _focusedEntryIndex = -1;
                return;
            }

            if (_focusedEntryIndex > deletedIndex)
            {
                _focusedEntryIndex--;
            }
        }

        private void RefreshEntryIds()
        {
            PuzzleLevelReferenceTable table = target as PuzzleLevelReferenceTable;
            if (table == null)
            {
                return;
            }

            table.RefreshEntryIds();
            serializedObject.Update();
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void ValidateAllEntries()
        {
            int errors = 0;
            int warnings = 0;
            Dictionary<string, List<int>> idIndices = GetIdIndices();
            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(i);
                string levelId = entry.FindPropertyRelative("Id").stringValue;
                TextAsset asset = entry.FindPropertyRelative("Asset")
                    .objectReferenceValue as TextAsset;
                DataType dataType = (DataType)entry.FindPropertyRelative("DataType")
                    .intValue;
                PuzzleLevelValidationResult result
                    = PuzzleLevelValidationService.Validate(
                        PuzzleLevelValidationService.CreateRequest(
                            levelId,
                            asset,
                            dataType));
                List<PuzzleLevelValidationDiagnostic> diagnostics
                    = new List<PuzzleLevelValidationDiagnostic>(result.Diagnostics);
                AddDuplicateIdDiagnostic(levelId, i, idIndices, diagnostics);
                if (HasSeverity(diagnostics, PuzzleLevelValidationSeverity.Error))
                {
                    errors++;
                }
                else if (HasSeverity(diagnostics, PuzzleLevelValidationSeverity.Warning))
                {
                    warnings++;
                }
            }

            EditorUtility.DisplayDialog(
                "Puzzle Level Validation",
                $"Validated {_entriesProperty.arraySize} entries.\n\n"
                + $"Errors: {errors}\nWarnings: {warnings}\n"
                + $"Valid: {_entriesProperty.arraySize - errors - warnings}",
                "OK");
        }

        private void HandleProjectStateChanged()
        {
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        #endregion

        #region Nested Types

        private sealed class PuzzleLevelReferenceTableEntryPopup : PopupWindowContent
        {
            private readonly string _levelId;
            private readonly TextAsset _asset;
            private readonly DataType _dataType;
            private readonly Action<int> _moveEntry;
            private readonly Action _deleteEntry;
            private readonly Action _revalidateEntry;
            private readonly Action _openAsset;
            private readonly Action _toggleEntrySelection;
            private readonly Action _moveSelectedAbove;
            private readonly Action _moveSelectedBelow;
            private readonly Action _openLevelDataEditor;
            private int _targetIndex;
            private readonly int _entryCount;
            private readonly int _currentIndex;
            private bool _isMoveEntryExpanded;

            public PuzzleLevelReferenceTableEntryPopup(
                string levelId,
                TextAsset asset,
                DataType dataType,
                int currentIndex,
                int entryCount,
                Action<int> moveEntry,
                Action deleteEntry,
                Action revalidateEntry,
                Action openAsset,
                Action toggleEntrySelection,
                Action moveSelectedAbove,
                Action moveSelectedBelow,
                Action openLevelDataEditor)
            {
                _levelId = levelId;
                _asset = asset;
                _dataType = dataType;
                _targetIndex = currentIndex;
                _currentIndex = currentIndex;
                _entryCount = entryCount;
                _moveEntry = moveEntry;
                _deleteEntry = deleteEntry;
                _revalidateEntry = revalidateEntry;
                _openAsset = openAsset;
                _toggleEntrySelection = toggleEntrySelection;
                _moveSelectedAbove = moveSelectedAbove;
                _moveSelectedBelow = moveSelectedBelow;
                _openLevelDataEditor = openLevelDataEditor;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(400f, 322f);
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
                        "Delete this level entry",
                        _deleteEntry
                    );
                    DrawQuickToolButton(
                        "d_Refresh",
                        "Revalidate this level entry",
                        _revalidateEntry
                    );
                    DrawQuickToolButton(
                        "d_FolderOpened Icon",
                        "Open this asset in its external application",
                        _openAsset
                    );
                    DrawMoveEntryToolButton();
                    DrawQuickToolButton(
                        "d_FilterSelectedOnly",
                        "Select or deselect this level entry",
                        _toggleEntrySelection
                    );
                }

                if (_isMoveEntryExpanded)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _targetIndex = EditorGUILayout.IntField(
                            "Move entry to",
                            Mathf.Clamp(_targetIndex, 1, _entryCount)
                        );
                        if (GUILayout.Button("Move", GUILayout.Width(58f)))
                        {
                            editorWindow.Close();
                            _moveEntry?.Invoke(_targetIndex);
                        }
                    }
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
                    EditorGUILayout.TextField("Id", _levelId);
                    EditorGUILayout.ObjectField("Asset", _asset, typeof(TextAsset), false);
                    EditorGUILayout.EnumPopup("Data Type", _dataType);
                }
                EditorGUILayout.LabelField("Row", _currentIndex.ToString());
            }

            private void DrawOptionalTools()
            {
                EditorGUILayout.LabelField("Optional Tools", EditorStyles.boldLabel);
                if (GUILayout.Button("Show Level Data"))
                {
                    editorWindow.Close();
                    _openLevelDataEditor?.Invoke();
                }

                using (new EditorGUI.DisabledGroupScope(_moveSelectedAbove == null))
                {
                    if (GUILayout.Button("Move Selected Above This Entry"))
                    {
                        editorWindow.Close();
                        _moveSelectedAbove?.Invoke();
                    }
                }

                using (new EditorGUI.DisabledGroupScope(_moveSelectedBelow == null))
                {
                    if (GUILayout.Button("Move Selected Below This Entry"))
                    {
                        editorWindow.Close();
                        _moveSelectedBelow?.Invoke();
                    }
                }
            }

            private void DrawQuickToolButton(
                string iconName,
                string tooltip,
                Action action)
            {
                using (new EditorGUI.DisabledGroupScope(action == null))
                {
                    GUIContent icon = EditorGUIUtility.IconContent(iconName);
                    icon.tooltip = tooltip;
                    if (GUILayout.Button(icon, EditorStyles.miniButton, GUILayout.Width(32f)))
                    {
                        editorWindow.Close();
                        action?.Invoke();
                    }
                }
            }

            private void DrawMoveEntryToolButton()
            {
                GUIContent icon = EditorGUIUtility.IconContent("d_MoveTool");
                icon.tooltip = "Show or hide move entry controls";
                if (GUILayout.Button(icon, EditorStyles.miniButton, GUILayout.Width(32f)))
                {
                    _isMoveEntryExpanded = !_isMoveEntryExpanded;
                    editorWindow.Repaint();
                }
            }

            private static void DrawSeparator()
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
                EditorGUILayout.Space(4f);
            }
        }

        private sealed class PuzzleLevelDataEditorPopup : PopupWindowContent
        {
            #region Private Fields

            private readonly PuzzleLevelValidationRequest _request;
            private readonly PuzzleLevelValidationResult _validation;
            private PuzzleLevelDeserializationResult _result;
            private string _error;
            private string _data = string.Empty;
            private string _lineNumbers = "1";
            private int _lineCount = 1;
            private Vector2 _scrollPosition;
            private bool _wordWrap = true;
            private GUIStyle _dataStyle;
            private GUIStyle _lineNumberStyle;

            #endregion

            #region Public Methods

            public PuzzleLevelDataEditorPopup(
                PuzzleLevelValidationRequest request,
                PuzzleLevelValidationResult validation)
            {
                _request = request;
                _validation = validation;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(780f, 640f);
            }

            public override void OnOpen()
            {
                InitializeStyles();
                if (!PuzzleLevelValidationService.TryDeserialize(
                        _request,
                        out _result,
                        out _error
                    ))
                {
                    return;
                }

                _data = TruncateData(_result.Text) ?? string.Empty;
                _lineNumbers = CreateLineNumbers(_data, out _lineCount);
            }

            public override void OnGUI(Rect rect)
            {
                DrawHeader();
                DrawMetadata();
                EditorGUILayout.Space(6f);
                DrawDataEditor();
            }

            #endregion

            #region Private Methods

            private void InitializeStyles()
            {
                _dataStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = _wordWrap
                };
                _lineNumberStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperRight,
                    padding = new RectOffset(0, 6, 3, 0)
                };
            }

            private void DrawHeader()
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Level Data Editor",
                        EditorStyles.boldLabel
                    );
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Close", GUILayout.Width(56f)))
                    {
                        editorWindow.Close();
                    }
                }
            }

            private void DrawMetadata()
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        _result?.DisplayName ?? "Deserialization Preview",
                        EditorStyles.miniBoldLabel
                    );
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        EditorGUILayout.TextField("Level Id", _request.LevelId);
                        EditorGUILayout.ObjectField(
                            "Source Asset",
                            _request.Asset,
                            typeof(TextAsset),
                            false
                        );
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Declared Type",
                            GUILayout.Width(94f)
                        );
                        EditorGUILayout.LabelField(_request.DeclaredDataType.ToString());
                        EditorGUILayout.LabelField(
                            "Effective Type",
                            GUILayout.Width(94f)
                        );
                        EditorGUILayout.LabelField(_request.EffectiveDataType.ToString());
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Validator", GUILayout.Width(94f));
                        EditorGUILayout.LabelField(
                            string.IsNullOrEmpty(_validation?.ValidatorName)
                                ? "Unavailable"
                                : _validation.ValidatorName
                        );
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Content Hash", GUILayout.Width(94f));
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(_request.ContentHash)
                                ? "Unavailable"
                                : _request.ContentHash,
                            EditorStyles.miniLabel,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)
                        );
                    }

                    EditorGUILayout.LabelField(
                        $"Source: {_request.Content.Length:N0} bytes | "
                        + $"Deserialized: {_data.Length:N0} characters | "
                        + $"Lines: {_lineCount:N0}",
                        EditorStyles.miniLabel
                    );
                }
            }

            private void DrawDataEditor()
            {
                EditorGUILayout.LabelField(
                    "Deserialized Data",
                    EditorStyles.boldLabel
                );
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    EditorGUILayout.LabelField(
                        "Read-only representation",
                        EditorStyles.centeredGreyMiniLabel
                    );
                    _wordWrap = GUILayout.Toggle(
                        _wordWrap,
                        "Wrap",
                        EditorStyles.toolbarButton,
                        GUILayout.Width(42f)
                    );
                    if (GUILayout.Button(
                            "Copy",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(40f)
                        ))
                    {
                        EditorGUIUtility.systemCopyBuffer = _data;
                    }
                }

                if (!string.IsNullOrEmpty(_error))
                {
                    EditorGUILayout.HelpBox(_error, MessageType.Warning);
                    return;
                }

                if (_dataStyle == null || _lineNumberStyle == null)
                {
                    InitializeStyles();
                }

                _dataStyle.wordWrap = _wordWrap;
                float dataHeight = GetDataHeight();
                _scrollPosition = EditorGUILayout.BeginScrollView(
                    _scrollPosition,
                    GUILayout.Height(400f)
                );
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!_wordWrap)
                    {
                        EditorGUILayout.SelectableLabel(
                            _lineNumbers,
                            _lineNumberStyle,
                            GUILayout.Width(42f),
                            GUILayout.MinHeight(dataHeight)
                        );
                    }

                    EditorGUILayout.SelectableLabel(
                        _data,
                        _dataStyle,
                        GUILayout.MinHeight(dataHeight)
                    );
                }

                EditorGUILayout.EndScrollView();
            }

            private float GetDataHeight()
            {
                if (!_wordWrap)
                {
                    return Mathf.Max(
                        400f,
                        _lineCount * (EditorGUIUtility.singleLineHeight + 2f)
                    );
                }

                float contentWidth = Mathf.Max(
                    1f,
                    EditorGUIUtility.currentViewWidth - 38f
                );
                return Mathf.Max(
                    400f,
                    _dataStyle.CalcHeight(new GUIContent(_data), contentWidth)
                );
            }

            private static string TruncateData(string value)
            {
                if (string.IsNullOrEmpty(value)
                    || value.Length <= MaximumLevelDataLength)
                {
                    return value;
                }

                return value.Substring(0, MaximumLevelDataLength)
                    + "\n\n[Preview truncated]";
            }

            private static string CreateLineNumbers(string value, out int lineCount)
            {
                lineCount = 1;
                for (int index = 0; index < value.Length; index++)
                {
                    if (value[index] == '\n')
                    {
                        lineCount++;
                    }
                }

                StringBuilder lineNumbers = new StringBuilder(lineCount * 4);
                for (int index = 1; index <= lineCount; index++)
                {
                    lineNumbers.Append(index);
                    if (index < lineCount)
                    {
                        lineNumbers.AppendLine();
                    }
                }

                return lineNumbers.ToString();
            }

            #endregion
        }

        private sealed class PuzzleLevelValidationPopup : PopupWindowContent
        {
            #region Private Fields

            private readonly PuzzleLevelValidationResult _validation;
            private readonly IReadOnlyList<PuzzleLevelValidationDiagnostic>
                _diagnostics;
            private Vector2 _scrollPosition;
            private readonly HashSet<int> _expandedSuccessGroupStarts
                = new HashSet<int>();

            #endregion

            #region Public Methods

            public PuzzleLevelValidationPopup(
                PuzzleLevelValidationResult validation,
                IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics
            )
            {
                _validation = validation;
                _diagnostics = diagnostics
                    ?? Array.Empty<PuzzleLevelValidationDiagnostic>();
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(640f, 520f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField(
                    "Status",
                    EditorStyles.boldLabel
                );
                DrawStatusSummary();
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    "Validation Steps",
                    EditorStyles.boldLabel
                );
                _scrollPosition = EditorGUILayout.BeginScrollView(
                    _scrollPosition
                );
                DrawValidationSteps();
                EditorGUILayout.EndScrollView();
            }

            #endregion

            #region Private Methods

            private void DrawStatusSummary()
            {
                bool hasError = HasSeverity(
                    _diagnostics,
                    PuzzleLevelValidationSeverity.Error
                );
                bool hasWarning = HasSeverity(
                    _diagnostics,
                    PuzzleLevelValidationSeverity.Warning
                );
                EditorGUILayout.LabelField(
                    "Result",
                    GetValidationLabel(hasError, hasWarning, _diagnostics)
                );
                EditorGUILayout.LabelField(
                    "Validator",
                    string.IsNullOrEmpty(_validation?.ValidatorName)
                        ? "None"
                        : _validation.ValidatorName,
                    EditorStyles.wordWrappedLabel
                );
                EditorGUILayout.LabelField(
                    "Hash",
                    string.IsNullOrEmpty(_validation?.ContentHash)
                        ? "Unavailable"
                        : _validation.ContentHash,
                    EditorStyles.wordWrappedLabel
                );
            }

            private void DrawValidationSteps()
            {
                if (_validation == null || _validation.Steps.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No validation steps were reported.",
                        MessageType.Info
                    );
                    return;
                }

                for (int index = 0; index < _validation.Steps.Count;)
                {
                    if (_validation.Steps[index].Status
                        == PuzzleLevelValidationStepStatus.Success)
                    {
                        int groupEndIndex = GetSuccessGroupEndIndex(index);
                        DrawSuccessGroup(index, groupEndIndex);
                        index = groupEndIndex;
                        continue;
                    }

                    DrawStepCard(_validation.Steps[index], index + 1);
                    index++;
                }
            }

            private int GetSuccessGroupEndIndex(int startIndex)
            {
                int index = startIndex;
                while (index < _validation.Steps.Count
                       && _validation.Steps[index].Status
                       == PuzzleLevelValidationStepStatus.Success)
                {
                    index++;
                }

                return index;
            }

            private void DrawSuccessGroup(int startIndex, int endIndex)
            {
                if (endIndex - startIndex == 1)
                {
                    DrawSuccessfulStep(_validation.Steps[startIndex], startIndex + 1);
                    return;
                }

                bool isExpanded = _expandedSuccessGroupStarts.Contains(startIndex);
                Color previousColor = GUI.contentColor;
                GUI.contentColor = GetStepColor(
                    PuzzleLevelValidationStepStatus.Success
                );
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        $"Steps {startIndex + 1}-{endIndex} SUCCESS",
                        EditorStyles.miniBoldLabel
                    );
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            isExpanded ? "Collapse" : "Expand...",
                            GUILayout.Width(76f)
                        ))
                    {
                        ToggleSuccessGroup(startIndex, isExpanded);
                    }
                }

                GUI.contentColor = previousColor;
                if (!isExpanded)
                {
                    EditorGUILayout.Space(4f);
                    return;
                }

                for (int index = startIndex; index < endIndex; index++)
                {
                    DrawSuccessfulStep(_validation.Steps[index], index + 1);
                }
            }

            private void ToggleSuccessGroup(int startIndex, bool isExpanded)
            {
                if (isExpanded)
                {
                    _expandedSuccessGroupStarts.Remove(startIndex);
                    return;
                }

                _expandedSuccessGroupStarts.Add(startIndex);
            }

            private static void DrawStepCard(
                PuzzleLevelValidationStep step,
                int index
            )
            {
                if (step.Status == PuzzleLevelValidationStepStatus.Success)
                {
                    DrawSuccessfulStep(step, index);
                    return;
                }

                Color previousColor = GUI.contentColor;
                GUI.contentColor = GetStepColor(step.Status);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        $"Step {index}: {GetStepStatusLabel(step.Status)}",
                        EditorStyles.boldLabel
                    );
                    GUI.contentColor = previousColor;
                    EditorGUILayout.LabelField(
                        step.Name,
                        EditorStyles.wordWrappedLabel
                    );
                    EditorGUILayout.LabelField(
                        "More info",
                        EditorStyles.miniBoldLabel
                    );
                    if (step.Messages.Count == 0)
                    {
                        EditorGUILayout.LabelField(
                            "No additional information.",
                            EditorStyles.wordWrappedMiniLabel
                        );
                    }
                    else
                    {
                        for (int messageIndex = 0;
                             messageIndex < step.Messages.Count;
                             messageIndex++)
                        {
                            EditorGUILayout.LabelField(
                                step.Messages[messageIndex],
                                EditorStyles.wordWrappedMiniLabel
                            );
                        }
                    }
                }

                GUI.contentColor = previousColor;
                EditorGUILayout.Space(4f);
            }

            private static void DrawSuccessfulStep(
                PuzzleLevelValidationStep step,
                int index
            )
            {
                Color previousColor = GUI.contentColor;
                GUI.contentColor = GetStepColor(step.Status);
                string details = step.Messages.Count > 0
                    ? $" - {step.Messages[0]}"
                    : string.Empty;
                EditorGUILayout.LabelField(
                    $"Step {index}: SUCCESS - {step.Name}{details}",
                    EditorStyles.miniLabel
                );
                GUI.contentColor = previousColor;
            }

            private static Color GetStepColor(
                PuzzleLevelValidationStepStatus status
            )
            {
                switch (status)
                {
                    case PuzzleLevelValidationStepStatus.Failed:
                        return new Color(0.88f, 0.3f, 0.3f);
                    case PuzzleLevelValidationStepStatus.Warning:
                        return new Color(0.92f, 0.7f, 0.2f);
                    default:
                        return new Color(0.35f, 0.8f, 0.4f);
                }
            }

            private static string GetStepStatusLabel(
                PuzzleLevelValidationStepStatus status
            )
            {
                switch (status)
                {
                    case PuzzleLevelValidationStepStatus.Failed:
                        return "FAILED";
                    case PuzzleLevelValidationStepStatus.Warning:
                        return "WARNING";
                    default:
                        return "SUCCESS";
                }
            }

            #endregion
        }

        #endregion
    }
}
