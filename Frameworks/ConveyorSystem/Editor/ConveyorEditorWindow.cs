using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor
{
    /// <summary>Dockable host for the same authoring inspector, plus asset and live-world tools.</summary>
    public sealed class ConveyorEditorWindow : EditorWindow
    {
        #region Properties
        public Conveyor Target => conveyor;
        #endregion

        #region Serialized Fields
        [SerializeField] private Conveyor conveyor;
        [SerializeField] private ConveyorManager manager;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private int tab;
        #endregion

        #region Private Fields
        private readonly Dictionary<UnityEngine.Object, UnityEditor.Editor> _assetEditors = new();
        private UnityEditor.Editor _ownerEditor;
        private Vector2 _scroll;
        private string _message;
        private MessageType _messageType;
        private int _channel;
        private int _profile;
        private float _position;
        private long _nextKey = long.MinValue;
        #endregion

        #region Unity Callbacks
        private void OnEnable()
        {
            titleContent = new GUIContent("Conveyors");
            minSize = new Vector2(390, 420);
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            if (followSelection) FollowSelection();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            ReleaseEditors();
        }

        private void OnSelectionChange()
        {
            if (followSelection) FollowSelection();
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            var next = (Conveyor)EditorGUILayout.ObjectField("Scene conveyor", conveyor, typeof(Conveyor), true);
            if (next != conveyor) Bind(next);
            if (conveyor == null)
            {
                EditorGUILayout.HelpBox("Select a scene Conveyor, or drag one here. Create starter assets from Tools / Scheherazade / Conveyor System, then assign them to a Conveyor component.", MessageType.Info);
                return;
            }

            tab = GUILayout.Toolbar(tab, new[] { "Authoring", "Assets", "Live test" });
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
            if (tab == 0) DrawAuthoring();
            else if (tab == 1) DrawAssets();
            else DrawLiveTools();
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Public Methods
        [MenuItem("Tools/Scheherazade/Conveyor System/Conveyor Editor")]
        public static void Open() => GetWindow<ConveyorEditorWindow>("Conveyors");

        public static void ShowFor(Conveyor owner)
        {
            var window = GetWindow<ConveyorEditorWindow>("Conveyors");
            window.Bind(owner);
            window.Show();
        }

        public void Bind(Conveyor owner)
        {
            if (owner != null && EditorUtility.IsPersistent(owner))
                throw new ArgumentException("Choose a scene conveyor, not a prefab asset.", nameof(owner));
            if (conveyor == owner) return;
            ReleaseEditors();
            conveyor = owner;
            _message = null;
            _scroll = Vector2.zero;
            Repaint();
        }
        #endregion

        #region Private Methods
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                followSelection = GUILayout.Toggle(followSelection, "Follow selection", EditorStyles.toolbarButton);
                using (new EditorGUI.DisabledScope(conveyor == null))
                {
                    if (GUILayout.Button("Select / frame", EditorStyles.toolbarButton))
                    {
                        Selection.activeGameObject = conveyor.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }
            }
        }

        private void DrawAuthoring()
        {
            EditorGUILayout.HelpBox("The existing Conveyor inspector is shared here: point/segment tools, validation, temporary preview and persistent baking. Select / frame to edit numbered handles in Scene View. Asset edits are shared by every consumer.", MessageType.Info);
            UnityEditor.Editor.CreateCachedEditor(conveyor, typeof(ConveyorEditor), ref _ownerEditor);
            _ownerEditor.OnInspectorGUI();
        }

        private void DrawAssets()
        {
            EditorGUILayout.HelpBox("These are the actual referenced assets, not copies. Changes support Undo. Rebuild the geometry preview after editing; occupied runtimes cannot be rebuilt.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawAsset("Route definition", conveyor.Definition);
                DrawAsset("Configuration", conveyor.Configuration);
                if (conveyor.Configuration == null) return;
                var settings = new SerializedObject(conveyor.Configuration);
                foreach (string field in new[] { "pathProvider", "transportProvider", "geometryProvider", "payloadViewProvider" })
                {
                    SerializedProperty property = settings.FindProperty(field);
                    DrawAsset(property.displayName, property.objectReferenceValue);
                }
                SerializedProperty modules = settings.FindProperty("modules");
                for (int index = 0; index < modules.arraySize; index++)
                {
                    UnityEngine.Object module = modules.GetArrayElementAtIndex(index).objectReferenceValue;
                    DrawAsset("Module " + (index + 1), module);
                    if (module == null) continue;
                    SerializedProperty policy = new SerializedObject(module).FindProperty("overflowPolicy");
                    if (policy != null) DrawAsset("Overflow policy", policy.objectReferenceValue);
                }
            }
            if (GUILayout.Button("Save edited assets")) AssetDatabase.SaveAssets();
        }

        private void DrawAsset(string label, UnityEngine.Object asset)
        {
            if (asset == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(label + " · " + asset.name, EditorStyles.boldLabel);
                if (!_assetEditors.TryGetValue(asset, out UnityEditor.Editor editor) || editor == null)
                {
                    editor = UnityEditor.Editor.CreateEditor(asset);
                    _assetEditors[asset] = editor;
                }
                editor.OnInspectorGUI();
            }
        }

        private void DrawLiveTools()
        {
            manager = (ConveyorManager)EditorGUILayout.ObjectField("Clock manager", manager, typeof(ConveyorManager), true);
            if (!Application.isPlaying || !conveyor.IsInitialized)
            {
                EditorGUILayout.HelpBox("Enter Play Mode in the test scene. Assign its Conveyor Manager to pause/step. Runtime insertions are temporary and do not edit the route asset.", MessageType.Info);
                return;
            }
            ConveyorRuntime runtime = conveyor.Runtime;
            ConveyorWorld world = runtime.World;
            EditorGUILayout.LabelField("Payloads on this conveyor", runtime.Count.ToString());
            EditorGUILayout.LabelField("World tick / cargo", world.TickIndex + " / " + world.Count);
            EditorGUILayout.LabelField("Transfers / vaporized", world.TransferCount + " / " + world.VaporizedCount);
            for (int channel = 0; channel < runtime.Channels; channel++)
                EditorGUILayout.LabelField("Lane " + channel, runtime.GetBlockReason(channel) + " · " + runtime.GetBlockedTicks(channel) + " blocked ticks");
            bool matchingManager = manager != null && manager.World == world;
            using (new EditorGUI.DisabledScope(!matchingManager || world.IsBusy || world.IsFaulted))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(matchingManager && manager.Running ? "Pause" : "Run")) manager.Running = !manager.Running;
                    using (new EditorGUI.DisabledScope(matchingManager && manager.Running))
                    {
                        if (GUILayout.Button("Single step")) Run(manager.StepOnce);
                    }
                }
            }
            if (!matchingManager) EditorGUILayout.HelpBox("Assign the manager that owns this world to control its clock.", MessageType.Info);
            _channel = Mathf.Clamp(EditorGUILayout.IntField("Lane", _channel), 0, runtime.Channels - 1);
            _profile = EditorGUILayout.IntField("Payload profile ID", _profile);
            _position = EditorGUILayout.Slider("Normalized entry", _position, 0, 0.9999f);
            using (new EditorGUI.DisabledScope(world.IsBusy || world.IsFaulted || world.IsDisposed))
            {
                if (GUILayout.Button("Insert one test payload")) Run(() =>
                {
                    ConveyorAdmissionStatus status = world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(_nextKey++, _profile),
                        _channel, _position * runtime.Path.Length, out _);
                    world.Publish();
                    _message = "Admission: " + status;
                    _messageType = status == ConveyorAdmissionStatus.Accepted ? MessageType.Info : MessageType.Warning;
                });
            }
            if (world.IsFaulted) EditorGUILayout.HelpBox("The world is faulted. Inspect Console and restart Play Mode after fixing the cause.", MessageType.Error);
        }

        private void Run(Action action)
        {
            try { _message = null; action(); }
            catch (Exception error) { _message = error.Message; _messageType = MessageType.Error; }
        }

        private void FollowSelection()
        {
            if (Selection.activeGameObject == null) return;
            Conveyor selected = Selection.activeGameObject.GetComponentInParent<Conveyor>();
            if (selected != null && !EditorUtility.IsPersistent(selected)) Bind(selected);
        }

        private void HandleUndoRedo() { ReleaseEditors(); Repaint(); SceneView.RepaintAll(); }
        private void HandlePlayModeChanged(PlayModeStateChange state) { ReleaseEditors(); Repaint(); }

        private void ReleaseEditors()
        {
            if (_ownerEditor != null) DestroyImmediate(_ownerEditor);
            _ownerEditor = null;
            foreach (UnityEditor.Editor editor in _assetEditors.Values)
            {
                if (editor != null) DestroyImmediate(editor);
            }
            _assetEditors.Clear();
        }
        #endregion
    }
}
