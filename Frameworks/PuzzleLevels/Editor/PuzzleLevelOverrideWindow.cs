using System.IO;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor
{
    public class PuzzleLevelOverrideWindow : EditorWindow
    {
        private const float DropZoneHeight = 80f;

        private string _levelId = "";
        private string _overrideFilePath = "";
        private bool _isDraggingOver;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Puzzle Levels/Override Injector")]
        public static void Open()
        {
            PuzzleLevelOverrideWindow window = GetWindow<PuzzleLevelOverrideWindow>(
                false, "Level Override Injector", true);
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        private void OnGUI()
        {
            PuzzleLevelManager manager = PuzzleLevelManager.Instance;

            DrawHeader();
            DrawInjectSection(manager);
            GUILayout.Space(8);
            DrawActiveOverrides(manager);
        }

        private void DrawHeader()
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Puzzle Level Override Injector", EditorStyles.boldLabel);
            GUILayout.Space(2);

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);
        }

        private void DrawInjectSection(PuzzleLevelManager manager)
        {
            EditorGUILayout.LabelField("Inject Override", EditorStyles.miniBoldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode before injecting runtime overrides.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Level ID", GUILayout.Width(60));
                _levelId = EditorGUILayout.TextField(_levelId);
            }

            GUILayout.Space(4);

            DrawFileDropZone();

            GUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(
                !Application.isPlaying
                || manager == null
                || string.IsNullOrWhiteSpace(_levelId)
                || string.IsNullOrWhiteSpace(_overrideFilePath));

            if (GUILayout.Button("Inject Override", GUILayout.Height(28)))
            {
                InjectOverride();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawFileDropZone()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, DropZoneHeight,
                GUILayout.ExpandWidth(true));

            Event evt = Event.current;

            if (evt.type == EventType.DragUpdated
                || evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    string draggedPath = DragAndDrop.paths.Length > 0
                        ? DragAndDrop.paths[0]
                        : null;
                    bool isSupported = IsSupportedOverrideFile(draggedPath);
                    DragAndDrop.visualMode = isSupported
                        ? DragAndDropVisualMode.Copy
                        : DragAndDropVisualMode.Rejected;
                    _isDraggingOver = isSupported;

                    if (evt.type == EventType.DragPerform
                        && isSupported)
                    {
                        DragAndDrop.AcceptDrag();
                        _overrideFilePath = draggedPath;
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

            EditorGUI.DrawRect(dropArea, bgColor);

            string displayText;
            if (!string.IsNullOrEmpty(_overrideFilePath))
            {
                displayText = Path.GetFileName(_overrideFilePath);
            }
            else
            {
                displayText = _isDraggingOver
                    ? "Release to select file"
                    : "Drop override file here\n(.json, .bytes, .bin)";
            }

            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 12,
                normal =
                {
                    textColor = string.IsNullOrEmpty(_overrideFilePath)
                        ? Color.gray
                        : Color.white
                }
            };

            GUI.Label(dropArea, displayText, labelStyle);
        }

        private void DrawActiveOverrides(PuzzleLevelManager manager)
        {
            if (!Application.isPlaying || manager == null)
            {
                return;
            }

            PuzzleLevelOverrideRegistry registry = manager.GetOverrideRegistry();
            if (registry == null || registry.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No active overrides.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                $"Active Overrides ({registry.Count})",
                EditorStyles.miniBoldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition, GUILayout.Height(120));

            var overriddenIds = registry.GetOverriddenIds();
            foreach (string key in overriddenIds)
            {
                DrawOverrideEntry(key);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(4);

            if (GUILayout.Button("Clear All Overrides", GUILayout.Height(26)))
            {
                registry.Clear();
            }
        }

        private void DrawOverrideEntry(string levelId)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(levelId, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Remove", EditorStyles.miniButton,
                        GUILayout.Width(60)))
                {
                    PuzzleLevelManager manager = PuzzleLevelManager.Instance;
                    if (manager != null)
                    {
                        manager.GetOverrideRegistry()?.RemoveOverride(levelId);
                    }

                    Repaint();
                }
            }
        }

        private void InjectOverride()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Play Mode Required",
                    "Enter Play Mode before injecting runtime overrides.",
                    "OK");
                return;
            }

            if (!File.Exists(_overrideFilePath))
            {
                EditorUtility.DisplayDialog(
                    "File Not Found",
                    $"Override file not found: {_overrideFilePath}",
                    "OK");
                return;
            }

            PuzzleLevelManager manager = PuzzleLevelManager.Instance;
            if (manager == null)
            {
                EditorUtility.DisplayDialog(
                    "Manager Not Found",
                    "PuzzleLevelManager instance not found. Ensure the manager "
                    + "asset exists in Resources.",
                    "OK");
                return;
            }

            string ext = Path.GetExtension(_overrideFilePath).ToLowerInvariant();
            DataType dataType = ext == ".json"
                ? DataType.Text
                : DataType.Binary;

            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(_overrideFilePath);
            }
            catch (System.Exception exception)
            {
                QuickLog.Error<PuzzleLevelOverrideWindow>(
                    "Failed to read override file '{0}': {1}",
                    _overrideFilePath,
                    exception.Message);
                EditorUtility.DisplayDialog(
                    "Override Read Failed",
                    exception.Message,
                    "OK");
                return;
            }

            if (!IsSupportedOverrideFile(_overrideFilePath))
            {
                EditorUtility.DisplayDialog(
                    "Unsupported Override File",
                    "Supported extensions are .json, .bytes, and .bin.",
                    "OK");
                return;
            }

            string levelId = _levelId.Trim();
            PuzzleLevelData levelData = new PuzzleLevelData(
                levelId,
                fileBytes,
                dataType);

            PuzzleLevelOverrideRegistry registry = manager.GetOverrideRegistry();
            if (registry == null || !registry.SetOverride(levelId, levelData))
            {
                EditorUtility.DisplayDialog(
                    "Override Failed",
                    "The runtime override registry rejected the override.",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Override Injected",
                $"Level '{levelId}' overridden with "
                + $"'{Path.GetFileName(_overrideFilePath)}'.",
                "OK");

            _levelId = "";
            _overrideFilePath = "";

            Repaint();
        }

        private static bool IsSupportedOverrideFile(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty)
                .ToLowerInvariant();
            return extension == ".json"
                || extension == ".bytes"
                || extension == ".bin";
        }
    }
}
