using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem.Editor
{
    [CustomEditor(typeof(GridBoard))]
    internal sealed class GridBoardEditor : UnityEditor.Editor
    {
        #region Constants

        private const string PreferencesPrefix =
            "Scheherazade.GridSystem.GridBoardEditor.";
        private const string ShowGridKey = PreferencesPrefix + "ShowGrid";
        private const string ShowLabelsKey = PreferencesPrefix + "ShowLabels";
        private const string ShowFillKey = PreferencesPrefix + "ShowFill";
        private const string ShowInactiveKey = PreferencesPrefix + "ShowInactive";
        private const string ShowPermanentKey = PreferencesPrefix + "ShowPermanent";
        private const string LabelSizeKey = PreferencesPrefix + "LabelSize";
        private const string AdaptiveLabelsKey = PreferencesPrefix + "AdaptiveLabels";
        private const string MinimumLabelSpacingKey =
            PreferencesPrefix + "MinimumLabelSpacing";

        private const float DefaultLabelSize = 11f;
        private const float DefaultMinimumLabelSpacing = 42f;
        private const float MinimumLabelSpacing = 24f;
        private const float MaximumLabelSpacing = 96f;
        private const int MaximumLabelStep = 128;
        private const float LabelHeightOffset = 0.02f;
        private const float MinimumCellSize = 0.0001f;

        private static readonly Color FreeCellColor =
            new Color(0.15f, 0.75f, 1f, 0.9f);
        private static readonly Color OccupiedCellColor =
            new Color(1f, 0.25f, 0.2f, 0.95f);
        private static readonly Color BorderCellColor =
            new Color(1f, 0.7f, 0.15f, 0.95f);
        private static readonly Color InactiveCellColor =
            new Color(0.5f, 0.55f, 0.65f, 0.7f);
        private static readonly Color SelectedCellColor =
            new Color(1f, 1f, 1f, 1f);
        private static readonly Color UnknownCellColor =
            new Color(0.75f, 0.75f, 0.8f, 0.75f);

        #endregion

        #region Interfaces & Properties

        private GridBoard Board => target as GridBoard;

        internal static bool PermanentShowEnabled =>
            EditorPrefs.GetBool(ShowPermanentKey, false);

        internal static void DrawPermanentBoard(GridBoard board, SceneView sceneView)
        {
            EnsurePreferencesLoaded();
            if (!_showGrid)
            {
                return;
            }

            DrawBoard(board, sceneView);
        }

        #endregion

        #region Private Fields

        private static bool _showGrid;
        private static bool _showLabels;
        private static bool _showFill;
        private static bool _showInactive;
        private static bool _showPermanent;
        private static bool _adaptiveLabels;
        private static float _labelSize;
        private static float _minimumLabelSpacing;
        private static GUIStyle _labelStyle;
        private static GUIStyle _missingProviderStyle;
        private static bool _preferencesLoaded;

        private static readonly Vector3[] CellCorners = new Vector3[4];

        #endregion

        #region Unity Editor Callbacks

        private void OnEnable()
        {
            LoadPreferences();
        }

        private void OnSceneGUI()
        {
            if (!_showGrid || _showPermanent || Board == null)
            {
                return;
            }

            DrawBoard(
                Board,
                SceneView.currentDrawingSceneView ?? SceneView.lastActiveSceneView
            );
        }

        #endregion

        #region Inspector GUI

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawDebuggerPanel();
        }

        private void DrawDebuggerPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Grid Scene View Debugger",
                    EditorStyles.boldLabel
                );
                EditorGUILayout.LabelField(
                    "Draw cell geometry, coordinates, and occupancy in the Scene View.",
                    EditorStyles.wordWrappedMiniLabel
                );

                EditorGUI.BeginChangeCheck();
                _showGrid = EditorGUILayout.ToggleLeft(
                    "Draw cell gizmos",
                    _showGrid
                );
                _showLabels = EditorGUILayout.ToggleLeft(
                    "Draw coordinate and occupation labels",
                    _showLabels
                );
                _showFill = EditorGUILayout.ToggleLeft(
                    "Draw cell status fill",
                    _showFill
                );
                _showInactive = EditorGUILayout.ToggleLeft(
                    "Draw inactive pooled cells",
                    _showInactive
                );
                _showPermanent = EditorGUILayout.ToggleLeft(
                    "Always draw gizmos for every GridBoard",
                    _showPermanent
                );
                _labelSize = EditorGUILayout.Slider(
                    "Label size",
                    _labelSize,
                    8f,
                    24f
                );
                _adaptiveLabels = EditorGUILayout.ToggleLeft(
                    "Adapt coordinate label step to Scene View distance",
                    _adaptiveLabels
                );
                if (_adaptiveLabels)
                {
                    _minimumLabelSpacing = EditorGUILayout.Slider(
                        "Minimum label spacing",
                        _minimumLabelSpacing,
                        MinimumLabelSpacing,
                        MaximumLabelSpacing
                    );
                }

                if (EditorGUI.EndChangeCheck())
                {
                    SavePreferences();
                    SceneView.RepaintAll();
                }

                DrawBoardStatus();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Repaint Scene Views"))
                    {
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("Reset Debugger"))
                    {
                        ResetPreferences();
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private void DrawBoardStatus()
        {
            GridConfiguration configuration = Board != null
                ? Board.Configuration
                : null;

            if (configuration == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a GridConfiguration to preview this board.",
                    MessageType.Warning
                );
                return;
            }

            string state = Board.Initialized
                ? "Live map"
                : "Configuration preview";
            string provider = ResolveCoordinates(Board) != null
                ? "Coordinate provider: ready"
                : "Coordinate provider: missing";

            EditorGUILayout.LabelField(
                "State",
                state + "  |  " + provider,
                EditorStyles.miniLabel
            );
            EditorGUILayout.LabelField(
                "Grid",
                configuration.GridSize.x + " x " + configuration.GridSize.y
                    + "  |  Pool "
                    + configuration.PoolSize.x + " x " + configuration.PoolSize.y
                    + "  |  Border "
                    + configuration.BorderSize.x + " x "
                    + configuration.BorderSize.y,
                EditorStyles.miniLabel
            );

            if (!Board.Initialized)
            {
                EditorGUILayout.HelpBox(
                    "Occupancy is shown as UNKNOWN until the runtime map is initialized.",
                    MessageType.Info
                );
            }
        }

        #endregion

        #region Scene GUI

        private static void DrawBoard(GridBoard board, SceneView sceneView)
        {
            GridConfiguration configuration = board.Configuration;
            if (configuration == null)
            {
                return;
            }

            IGridCoordinateProvider coordinates = ResolveCoordinates(board);
            if (coordinates == null)
            {
                DrawMissingProviderMessage(board);
                return;
            }

            GridCoord gridSize = board.Initialized
                ? board.GridSize
                : configuration.GridSize;
            GridCoord poolSize = configuration.PoolSize;
            GridCoord borderSize = configuration.BorderSize;
            CalculateCellBasis(
                coordinates,
                board.transform,
                out Vector3 origin,
                out Vector3 stepX,
                out Vector3 stepY
            );
            int labelStep = CalculateLabelStep(
                sceneView,
                origin,
                stepX,
                stepY
            );

            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            try
            {
                if (board.Initialized
                    && board.Map != null
                    && board.Map.PooledCells != null)
                {
                    DrawRuntimeCells(
                        board.Map.PooledCells,
                        gridSize,
                        origin,
                        stepX,
                        stepY,
                        labelStep
                    );
                }
                else
                {
                    DrawPreviewCells(
                        gridSize,
                        poolSize,
                        borderSize,
                        origin,
                        stepX,
                        stepY,
                        labelStep
                    );
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private static void DrawRuntimeCells(
            GridCell[,] pooledCells,
            GridCoord gridSize,
            Vector3 origin,
            Vector3 stepX,
            Vector3 stepY,
            int labelStep
        )
        {
            foreach (GridCell cell in pooledCells)
            {
                if (cell == null)
                {
                    continue;
                }

                GridCoord position = cell.GridPosition;
                bool active = IsInsideGrid(position, gridSize);
                bool border = cell.IsBorder;
                if (!ShouldDrawCell(active, border))
                {
                    continue;
                }

                DrawCell(
                    cell,
                    position,
                    active,
                    border,
                    true,
                    origin,
                    stepX,
                    stepY,
                    labelStep
                );
            }
        }

        private static void DrawPreviewCells(
            GridCoord gridSize,
            GridCoord poolSize,
            GridCoord borderSize,
            Vector3 origin,
            Vector3 stepX,
            Vector3 stepY,
            int labelStep
        )
        {
            int maximumX = poolSize.x + borderSize.x;
            int maximumY = poolSize.y + borderSize.y;

            for (int x = -borderSize.x; x < maximumX; x++)
            {
                for (int y = -borderSize.y; y < maximumY; y++)
                {
                    GridCoord position = new GridCoord(x, y);
                    bool active = IsInsideGrid(position, gridSize);
                    bool border = IsInsideBorderRing(
                        position,
                        gridSize,
                        borderSize
                    );
                    if (!ShouldDrawCell(active, border))
                    {
                        continue;
                    }

                    DrawCell(
                        null,
                        position,
                        active,
                        border,
                        false,
                        origin,
                        stepX,
                        stepY,
                        labelStep
                    );
                }
            }
        }

        private static void DrawCell(
            GridCell cell,
            GridCoord position,
            bool active,
            bool border,
            bool initialized,
            Vector3 origin,
            Vector3 stepX,
            Vector3 stepY,
            int labelStep
        )
        {
            Vector3 center = origin + stepX * position.x + stepY * position.y;
            SetCellCorners(center, stepX, stepY);

            Color outlineColor = ResolveOutlineColor(
                cell,
                active,
                border,
                initialized
            );
            Color fillColor = _showFill
                ? ResolveFillColor(cell, active, border, initialized)
                : Color.clear;

            Handles.DrawSolidRectangleWithOutline(
                CellCorners,
                fillColor,
                outlineColor
            );

            if (_showLabels && ShouldDrawLabel(cell, position, labelStep))
            {
                DrawCellLabel(
                    cell,
                    position,
                    center,
                    stepX,
                    stepY,
                    border,
                    initialized
                );
            }
        }

        private static void DrawCellLabel(
            GridCell cell,
            GridCoord position,
            Vector3 center,
            Vector3 stepX,
            Vector3 stepY,
            bool border,
            bool initialized
        )
        {
            Vector3 normal = Vector3.Cross(stepX, stepY).normalized;
            if (normal.sqrMagnitude < MinimumCellSize)
            {
                normal = Vector3.up;
            }

            string label = "(" + position.x + ", " + position.y + ")\n"
                + ResolveStatusLabel(cell, border, initialized);
            Color labelColor = ResolveLabelColor(cell, border, initialized);
            _labelStyle = GetLabelStyle(labelColor);

            Handles.Label(
                center + normal * LabelHeightOffset,
                label,
                _labelStyle
            );
        }

        #endregion

        #region Private Methods

        private static IGridCoordinateProvider ResolveCoordinates(GridBoard board)
        {
            if (board == null)
            {
                return null;
            }

            if (board.Initialized && board.Coordinates != null)
            {
                return board.Coordinates;
            }

            return board.Configuration != null
                ? board.Configuration.CoordinateProvider
                : null;
        }

        private static void CalculateCellBasis(
            IGridCoordinateProvider coordinates,
            Transform boardTransform,
            out Vector3 origin,
            out Vector3 stepX,
            out Vector3 stepY
        )
        {
            origin = coordinates.CellToWorld(new GridCoord(0, 0));
            stepX = coordinates.CellToWorld(new GridCoord(1, 0)) - origin;
            stepY = coordinates.CellToWorld(new GridCoord(0, 1)) - origin;

            if (stepX.sqrMagnitude < MinimumCellSize)
            {
                stepX = boardTransform != null
                    ? boardTransform.right
                    : Vector3.right;
            }

            if (stepY.sqrMagnitude < MinimumCellSize)
            {
                stepY = boardTransform != null
                    ? boardTransform.forward
                    : Vector3.forward;
            }
        }

        private static int CalculateLabelStep(
            SceneView sceneView,
            Vector3 gridOrigin,
            Vector3 stepX,
            Vector3 stepY
        )
        {
            if (!_adaptiveLabels || sceneView == null || sceneView.camera == null)
            {
                return 1;
            }

            Camera camera = sceneView.camera;
            float cellWorldSize = Mathf.Max(stepX.magnitude, stepY.magnitude);
            if (cellWorldSize < MinimumCellSize)
            {
                return 1;
            }

            float pixelsPerWorldUnit = CalculatePixelsPerWorldUnit(
                camera,
                gridOrigin
            );
            float pixelsPerCell = cellWorldSize * pixelsPerWorldUnit;
            if (pixelsPerCell < MinimumCellSize)
            {
                return MaximumLabelStep;
            }

            int step = Mathf.CeilToInt(_minimumLabelSpacing / pixelsPerCell);
            return Mathf.Clamp(step, 1, MaximumLabelStep);
        }

        private static float CalculatePixelsPerWorldUnit(
            Camera camera,
            Vector3 worldPosition
        )
        {
            float pixelHeight = Mathf.Max(1f, camera.pixelHeight);
            if (camera.orthographic)
            {
                float orthographicWorldHeight = Mathf.Max(
                    MinimumCellSize,
                    camera.orthographicSize * 2f
                );
                return pixelHeight / orthographicWorldHeight;
            }

            float distance = Vector3.Dot(
                worldPosition - camera.transform.position,
                camera.transform.forward
            );
            if (distance < MinimumCellSize)
            {
                distance = Vector3.Distance(
                    worldPosition,
                    camera.transform.position
                );
            }

            float verticalFieldOfView = camera.fieldOfView * Mathf.Deg2Rad;
            float perspectiveWorldHeight = Mathf.Max(
                MinimumCellSize,
                2f * distance * Mathf.Tan(verticalFieldOfView * 0.5f)
            );
            return pixelHeight / perspectiveWorldHeight;
        }

        private static bool ShouldDrawLabel(
            GridCell cell,
            GridCoord position,
            int labelStep
        )
        {
            if (labelStep <= 1)
            {
                return true;
            }

            if (cell != null
                && (cell.Occupant != null
                    || cell.Selected
                    || cell.Tracked
                    || cell.Debugging))
            {
                return true;
            }

            return position.x % labelStep == 0
                && position.y % labelStep == 0;
        }

        private static bool ShouldDrawCell(bool active, bool border)
        {
            return _showInactive || active || border;
        }

        private static bool IsInsideGrid(GridCoord position, GridCoord gridSize)
        {
            return position.x >= 0
                && position.y >= 0
                && position.x < gridSize.x
                && position.y < gridSize.y;
        }

        private static bool IsInsideBorderRing(
            GridCoord position,
            GridCoord gridSize,
            GridCoord borderSize
        )
        {
            return position.x >= -borderSize.x
                && position.y >= -borderSize.y
                && position.x < gridSize.x + borderSize.x
                && position.y < gridSize.y + borderSize.y
                && !IsInsideGrid(position, gridSize);
        }

        private static void SetCellCorners(
            Vector3 center,
            Vector3 stepX,
            Vector3 stepY
        )
        {
            Vector3 halfX = stepX * 0.5f;
            Vector3 halfY = stepY * 0.5f;
            CellCorners[0] = center - halfX - halfY;
            CellCorners[1] = center + halfX - halfY;
            CellCorners[2] = center + halfX + halfY;
            CellCorners[3] = center - halfX + halfY;
        }

        private static Color ResolveOutlineColor(
            GridCell cell,
            bool active,
            bool border,
            bool initialized
        )
        {
            if (cell != null && cell.Selected)
            {
                return SelectedCellColor;
            }

            if (!initialized)
            {
                return active ? FreeCellColor : BorderCellColor;
            }

            if (!active)
            {
                return InactiveCellColor;
            }

            if (cell != null && cell.Occupant != null)
            {
                return OccupiedCellColor;
            }

            return border ? BorderCellColor : FreeCellColor;
        }

        private static Color ResolveFillColor(
            GridCell cell,
            bool active,
            bool border,
            bool initialized
        )
        {
            Color outline = ResolveOutlineColor(cell, active, border, initialized);
            outline.a = initialized && cell != null && cell.Occupant != null
                ? 0.2f
                : 0.08f;
            return outline;
        }

        private static Color ResolveLabelColor(
            GridCell cell,
            bool border,
            bool initialized
        )
        {
            if (!initialized)
            {
                return UnknownCellColor;
            }

            if (cell != null && cell.Occupant != null)
            {
                return OccupiedCellColor;
            }

            return border ? BorderCellColor : Color.white;
        }

        private static string ResolveStatusLabel(
            GridCell cell,
            bool border,
            bool initialized
        )
        {
            if (!initialized || cell == null)
            {
                return border ? "BORDER\nUNKNOWN" : "UNKNOWN";
            }

            if (cell.Occupant == null)
            {
                return border ? "BORDER\nFREE" : "FREE";
            }

            return border
                ? "BORDER\nOCCUPIED\n" + GetOccupantLabel(cell.Occupant)
                : "OCCUPIED\n" + GetOccupantLabel(cell.Occupant);
        }

        private static string GetOccupantLabel(IGridOccupant occupant)
        {
            if (occupant == null)
            {
                return string.Empty;
            }

            UnityEngine.Object unityObject = occupant as UnityEngine.Object;
            if (unityObject != null)
            {
                return unityObject.name;
            }

            IPlaceableObject placeableObject = occupant.PlaceableObject;
            if (placeableObject != null && !string.IsNullOrEmpty(placeableObject.Id))
            {
                return placeableObject.Id;
            }

            return occupant.GetType().Name;
        }

        private static GUIStyle GetLabelStyle(Color color)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    richText = false
                };
            }

            _labelStyle.fontSize = Mathf.RoundToInt(_labelSize);
            _labelStyle.normal.textColor = color;
            return _labelStyle;
        }

        private static void DrawMissingProviderMessage(GridBoard board)
        {
            if (_missingProviderStyle == null)
            {
                _missingProviderStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { textColor = Color.yellow }
                };
            }

            Handles.Label(
                board.transform.position,
                "Grid Debugger\nMissing coordinate provider",
                _missingProviderStyle
            );
        }

        private static void EnsurePreferencesLoaded()
        {
            if (_preferencesLoaded)
            {
                return;
            }

            LoadPreferences();
        }

        private static void LoadPreferences()
        {
            _showGrid = EditorPrefs.GetBool(ShowGridKey, true);
            _showLabels = EditorPrefs.GetBool(ShowLabelsKey, true);
            _showFill = EditorPrefs.GetBool(ShowFillKey, true);
            _showInactive = EditorPrefs.GetBool(ShowInactiveKey, false);
            _showPermanent = EditorPrefs.GetBool(ShowPermanentKey, false);
            _labelSize = EditorPrefs.GetFloat(LabelSizeKey, DefaultLabelSize);
            _adaptiveLabels = EditorPrefs.GetBool(AdaptiveLabelsKey, true);
            _minimumLabelSpacing = EditorPrefs.GetFloat(
                MinimumLabelSpacingKey,
                DefaultMinimumLabelSpacing
            );
            _preferencesLoaded = true;
        }

        private static void SavePreferences()
        {
            EditorPrefs.SetBool(ShowGridKey, _showGrid);
            EditorPrefs.SetBool(ShowLabelsKey, _showLabels);
            EditorPrefs.SetBool(ShowFillKey, _showFill);
            EditorPrefs.SetBool(ShowInactiveKey, _showInactive);
            EditorPrefs.SetBool(ShowPermanentKey, _showPermanent);
            EditorPrefs.SetFloat(LabelSizeKey, _labelSize);
            EditorPrefs.SetBool(AdaptiveLabelsKey, _adaptiveLabels);
            EditorPrefs.SetFloat(MinimumLabelSpacingKey, _minimumLabelSpacing);
        }

        private static void ResetPreferences()
        {
            EditorPrefs.DeleteKey(ShowGridKey);
            EditorPrefs.DeleteKey(ShowLabelsKey);
            EditorPrefs.DeleteKey(ShowFillKey);
            EditorPrefs.DeleteKey(ShowInactiveKey);
            EditorPrefs.DeleteKey(ShowPermanentKey);
            EditorPrefs.DeleteKey(LabelSizeKey);
            EditorPrefs.DeleteKey(AdaptiveLabelsKey);
            EditorPrefs.DeleteKey(MinimumLabelSpacingKey);
            LoadPreferences();
        }

        #endregion
    }

    [InitializeOnLoad]
    internal static class GridBoardSceneDebugger
    {
        #region Private Fields

        private static readonly List<GridBoard> Boards = new List<GridBoard>();
        private static bool _boardsDirty = true;

        #endregion

        #region Unity Editor Callbacks

        static GridBoardSceneDebugger()
        {
            SceneView.duringSceneGui += HandleSceneGUI;
            EditorApplication.hierarchyChanged += HandleHierarchyChanged;
            RefreshBoards();
        }

        #endregion

        #region Private Methods

        private static void HandleSceneGUI(SceneView sceneView)
        {
            if (!GridBoardEditor.PermanentShowEnabled)
            {
                return;
            }

            if (_boardsDirty)
            {
                RefreshBoards();
            }

            for (int i = 0; i < Boards.Count; i++)
            {
                GridBoard board = Boards[i];
                if (board == null || !board.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GridBoardEditor.DrawPermanentBoard(board, sceneView);
            }
        }

        private static void HandleHierarchyChanged()
        {
            _boardsDirty = true;
        }

        private static void RefreshBoards()
        {
            Boards.Clear();

            GridBoard[] sceneBoards =
                Resources.FindObjectsOfTypeAll<GridBoard>();
            for (int i = 0; i < sceneBoards.Length; i++)
            {
                GridBoard board = sceneBoards[i];
                if (board == null
                    || EditorUtility.IsPersistent(board)
                    || !board.gameObject.scene.IsValid())
                {
                    continue;
                }

                Boards.Add(board);
            }

            _boardsDirty = false;
        }

        #endregion
    }
}
