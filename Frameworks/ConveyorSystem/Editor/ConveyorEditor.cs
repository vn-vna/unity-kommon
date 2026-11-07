using System;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor
{
    [CustomEditor(typeof(Conveyor))]
    public sealed class ConveyorEditor : UnityEditor.Editor
    {
        private ConveyorBuildResult _preview;
        private string _error;
        private int _selectedPoint;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var owner = (Conveyor)target;
            if (GUILayout.Button("Open Conveyor Editor window")) ConveyorEditorWindow.ShowFor(owner);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Shared definition edits affect every consumer of that asset. Preview owns only temporary geometry; it never creates a simulation manager.", MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Validate definition and providers")) Run(() => Validate(owner));
                if (GUILayout.Button("Rebuild geometry preview")) Run(() => RebuildPreview(owner));
                if (GUILayout.Button("Clear preview")) ClearPreview();
                if (GUILayout.Button("Bake geometry prefab…")) Run(() => Bake(owner));
                DrawRouteTools(owner);
            }
            if (owner.IsInitialized)
            {
                EditorGUILayout.LabelField("Live payloads", owner.Runtime.Count.ToString());
                EditorGUILayout.LabelField("World ticks", owner.Runtime.World.TickIndex.ToString());
            }
        }

        private void OnSceneGUI()
        {
            var owner = (Conveyor)target;
            if (owner.Definition == null || owner.Configuration == null) return;
            ConveyorDefinitionSnapshot snapshot = owner.Definition.Snapshot();
            if (snapshot.Mode != ConveyorDefinitionMode.ControlPoints) return;
            var points = new Vector3[snapshot.Points.Count];
            for (int index = 0; index < points.Length; index++) points[index] = snapshot.Points[index];
            Handles.color = new Color(0.4f, 0.9f, 0.75f);
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 world = owner.transform.TransformPoint(points[index]);
                Handles.DrawDottedLine(world, owner.transform.TransformPoint(points[(index + 1) % points.Length]), 4);
                Handles.Label(world, " " + (index + 1));
                if (Application.isPlaying) continue;
                EditorGUI.BeginChangeCheck();
                Vector3 changed = Handles.PositionHandle(world, owner.transform.rotation);
                if (!EditorGUI.EndChangeCheck()) continue;
                _selectedPoint = index;
                Vector3 local = owner.transform.InverseTransformPoint(changed);
                local.y = points[0].y;
                points[index] = local;
                Run(() => ApplyPoints(owner, points, "Move conveyor control point"));
                break;
            }
        }

        private void OnDisable() { ClearPreview(); }

        private void DrawRouteTools(Conveyor owner)
        {
            if (owner.Definition == null) return;
            ConveyorDefinitionSnapshot snapshot = owner.Definition.Snapshot();
            if (snapshot.Mode == ConveyorDefinitionMode.PrefabSegments)
            {
                if (GUILayout.Button("Snap segment chain and validate closure")) Run(() => SnapSegments(owner, snapshot));
                return;
            }
            if (snapshot.Points.Count == 0) return;
            _selectedPoint = Mathf.Clamp(EditorGUILayout.IntField("Selected point (1-based)", _selectedPoint + 1) - 1, 0, snapshot.Points.Count - 1);
            if (GUILayout.Button("Insert point after selection")) Run(() =>
            {
                var points = new Vector3[snapshot.Points.Count + 1];
                for (int index = 0; index < snapshot.Points.Count; index++) points[index <= _selectedPoint ? index : index + 1] = snapshot.Points[index];
                points[_selectedPoint + 1] = (snapshot.Points[_selectedPoint] + snapshot.Points[(_selectedPoint + 1) % snapshot.Points.Count]) * 0.5f;
                ApplyPoints(owner, points, "Insert conveyor control point");
            });
            using (new EditorGUI.DisabledScope(snapshot.Points.Count <= 3))
            {
                if (GUILayout.Button("Remove selected point")) Run(() =>
                {
                    var points = new Vector3[snapshot.Points.Count - 1];
                    for (int index = 0, targetIndex = 0; index < snapshot.Points.Count; index++)
                    {
                        if (index != _selectedPoint) points[targetIndex++] = snapshot.Points[index];
                    }
                    ApplyPoints(owner, points, "Remove conveyor control point");
                    _selectedPoint = Mathf.Clamp(_selectedPoint, 0, points.Length - 1);
                });
            }
        }

        private void ApplyPoints(Conveyor owner, Vector3[] points, string description)
        {
            owner.Configuration.PathProvider.Bake(new ConveyorDefinitionSnapshot(points), owner.transform.localToWorldMatrix);
            Undo.RecordObject(owner.Definition, description);
            owner.Definition.SetPoints(points);
            EditorUtility.SetDirty(owner.Definition);
            if (_preview != null) RebuildPreview(owner);
        }

        private void SnapSegments(Conveyor owner, ConveyorDefinitionSnapshot snapshot)
        {
            var segments = new ConveyorSegmentPlacement[snapshot.Segments.Count];
            if (segments.Length == 0) throw new ArgumentException("Add segment assets before snapping.");
            segments[0] = snapshot.Segments[0];
            for (int index = 1; index < segments.Length; index++)
                segments[index] = ConveyorPathCompiler.SnapAfter(segments[index - 1], snapshot.Segments[index].segment);
            owner.Configuration.PathProvider.Bake(new ConveyorDefinitionSnapshot(segments), owner.transform.localToWorldMatrix);
            Undo.RecordObject(owner.Definition, "Snap conveyor segments");
            owner.Definition.SetSegments(segments);
            EditorUtility.SetDirty(owner.Definition);
            if (_preview != null) RebuildPreview(owner);
        }

        private static void Validate(Conveyor owner)
        {
            if (owner.Definition == null || owner.Configuration == null) throw new ArgumentException("Assign a definition and configuration.");
            owner.Configuration.Validate();
            owner.Configuration.PathProvider.Bake(owner.Definition.Snapshot(), owner.transform.localToWorldMatrix);
        }

        private void RebuildPreview(Conveyor owner)
        {
            Validate(owner);
            ConveyorBuildResult candidate = ConveyorManufacturer.Build(owner.Definition.Snapshot(), owner.Configuration.PathProvider,
                owner.Configuration.GeometryProvider, owner.transform, owner.Configuration.Width, owner.Configuration.Channels);
            if (candidate.Geometry?.Root != null) candidate.Geometry.Root.hideFlags = HideFlags.HideAndDontSave;
            ClearPreview();
            _preview = candidate;
            candidate.Activate();
            SceneView.RepaintAll();
        }

        private static void Bake(Conveyor owner)
        {
            string path = EditorUtility.SaveFilePanelInProject("Bake conveyor geometry", owner.name + "-Conveyor", "prefab", "Choose a new prefab path.");
            if (!string.IsNullOrEmpty(path)) ConveyorEditorBaker.Bake(owner, path);
        }

        private void ClearPreview() { _preview?.Dispose(); _preview = null; }
        private void Run(Action action)
        {
            try { action(); _error = null; }
            catch (Exception error) { _error = error.Message; }
            Repaint();
        }
    }
}
