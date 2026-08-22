using System;
using System.Collections.Generic;
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
        private const float ContextColumnWidth = 28f;
        private const float IdColumnWidth = 150f;
        private const float TypeColumnWidth = 76f;
        private const float ValidColumnWidth = 84f;

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
            bool refreshIdsRequested = DrawAutoIdControls();
            DrawEntriesToolbar();

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

        private void DrawEntriesToolbar()
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
            DrawPagination(totalPages);
            DrawColumnHeaders();

            int startIndex = _pageIndex * _pageSize;
            int endIndex = Mathf.Min(startIndex + _pageSize, filteredIndices.Count);
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.MaxHeight(420f));
            for (int i = startIndex; i < endIndex; i++)
            {
                DrawEntryRow(filteredIndices[i], idIndices);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPagination(int totalPages)
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
                    $"Page {_pageIndex + 1} / {totalPages}",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(92f));
                EditorGUILayout.LabelField(
                    "Page size",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(58f));
                _pageSize = Mathf.Clamp(
                    EditorGUILayout.IntField(_pageSize, GUILayout.Width(42f)),
                    MinimumPageSize,
                    MaximumPageSize);
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawColumnHeaders()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(
                    "C",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(ContextColumnWidth));
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

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("C", EditorStyles.miniButton,
                        GUILayout.Width(ContextColumnWidth)))
                {
                    Rect buttonRect = GUILayoutUtility.GetLastRect();
                    OpenEntryContextMenu(buttonRect, index, levelId, asset);
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
            EditorGUILayout.LabelField(
                new GUIContent($"● {label}", CreateValidationTooltip(
                    validation,
                    diagnostics)),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(ValidColumnWidth));
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

        private void OpenEntryContextMenu(
            Rect buttonRect,
            int index,
            string levelId,
            TextAsset asset)
        {
            CustomPopupDropdown.Show(
                buttonRect,
                new PuzzleLevelReferenceTableEntryPopup(
                    levelId,
                    asset,
                    index + 1,
                    _entriesProperty.arraySize,
                    targetIndex => MoveEntry(index, targetIndex),
                    () => DeleteEntry(index, levelId, asset),
                    () => ViewEntryData(buttonRect, index)));
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
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void AddEntry()
        {
            serializedObject.Update();
            Undo.RecordObject(target, "Add Puzzle Level Entry");
            _entriesProperty.arraySize++;
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(
                _entriesProperty.arraySize - 1);
            entry.FindPropertyRelative("Id").stringValue = string.Empty;
            entry.FindPropertyRelative("Asset").objectReferenceValue = null;
            entry.FindPropertyRelative("DataType").intValue = (int)DataType.Unknown;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            _pageIndex = Mathf.Max(
                0,
                Mathf.CeilToInt((float)_entriesProperty.arraySize / _pageSize) - 1);
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
            _pageIndex = 0;
            PuzzleLevelValidationService.InvalidateAll();
            Repaint();
        }

        private void ViewEntryData(Rect buttonRect, int index)
        {
            serializedObject.Update();
            if (index < 0 || index >= _entriesProperty.arraySize)
            {
                return;
            }

            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(index);
            string levelId = entry.FindPropertyRelative("Id").stringValue;
            TextAsset asset = entry.FindPropertyRelative("Asset")
                .objectReferenceValue as TextAsset;
            DataType dataType = (DataType)entry.FindPropertyRelative("DataType")
                .intValue;
            PuzzleLevelValidationRequest request
                = PuzzleLevelValidationService.CreateRequest(levelId, asset, dataType);
            CustomPopupDropdown.Show(
                buttonRect,
                new PuzzleLevelDeserializedDataPopup(request));
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
            private readonly Action<int> _moveEntry;
            private readonly Action _deleteEntry;
            private readonly Action _viewDeserializedData;
            private int _targetIndex;
            private readonly int _entryCount;

            public PuzzleLevelReferenceTableEntryPopup(
                string levelId,
                TextAsset asset,
                int currentIndex,
                int entryCount,
                Action<int> moveEntry,
                Action deleteEntry,
                Action viewDeserializedData)
            {
                _levelId = levelId;
                _asset = asset;
                _targetIndex = currentIndex;
                _entryCount = entryCount;
                _moveEntry = moveEntry;
                _deleteEntry = deleteEntry;
                _viewDeserializedData = viewDeserializedData;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360f, 174f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Level Entry", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.TextField("Id", _levelId);
                    EditorGUILayout.ObjectField("Asset", _asset, typeof(TextAsset), false);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _targetIndex = EditorGUILayout.IntField(
                        "Move to", Mathf.Clamp(_targetIndex, 1, _entryCount));
                    if (GUILayout.Button("Move", GUILayout.Width(58f)))
                    {
                        editorWindow.Close();
                        _moveEntry?.Invoke(_targetIndex);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("View Deserialized Data"))
                    {
                        editorWindow.Close();
                        _viewDeserializedData?.Invoke();
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(64f)))
                    {
                        editorWindow.Close();
                        _deleteEntry?.Invoke();
                    }
                }
            }
        }

        private sealed class PuzzleLevelDeserializedDataPopup : PopupWindowContent
        {
            private const int MaximumPreviewLength = 262144;

            private readonly PuzzleLevelValidationRequest _request;
            private readonly string _displayName;
            private readonly string _text;
            private readonly string _error;
            private Vector2 _scrollPosition;

            public PuzzleLevelDeserializedDataPopup(
                PuzzleLevelValidationRequest request)
            {
                _request = request;
                if (PuzzleLevelValidationService.TryDeserialize(
                        request,
                        out PuzzleLevelDeserializationResult result,
                        out string error))
                {
                    _displayName = result.DisplayName;
                    _text = TruncatePreview(result.Text);
                }
                else
                {
                    _error = error;
                }
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(560f, 420f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(_displayName)
                        ? "Deserialized Data"
                        : _displayName,
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    _request.SourceName,
                    EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(_error))
                {
                    EditorGUILayout.HelpBox(_error, MessageType.Warning);
                    return;
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                EditorGUILayout.SelectableLabel(
                    _text ?? string.Empty,
                    EditorStyles.textArea,
                    GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            private static string TruncatePreview(string value)
            {
                if (string.IsNullOrEmpty(value) || value.Length <= MaximumPreviewLength)
                {
                    return value;
                }

                return value.Substring(0, MaximumPreviewLength)
                    + "\n\n[Preview truncated]";
            }
        }

        #endregion
    }
}
