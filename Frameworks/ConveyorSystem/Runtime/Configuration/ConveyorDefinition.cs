using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public enum ConveyorDefinitionMode { ControlPoints, PrefabSegments }

    [Serializable]
    public struct ConveyorSegmentPlacement
    {
        public ConveyorSegmentAsset segment;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Matrix4x4 Matrix => Matrix4x4.TRS(position, Quaternion.Euler(eulerAngles), Vector3.one);
    }

    public sealed class ConveyorDefinitionSnapshot
    {
        public ConveyorDefinitionMode Mode { get; }
        public IReadOnlyList<Vector3> Points { get; }
        public IReadOnlyList<ConveyorSegmentPlacement> Segments { get; }

        public ConveyorDefinitionSnapshot(IReadOnlyList<Vector3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            Mode = ConveyorDefinitionMode.ControlPoints;
            var copy = new Vector3[points.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = points[index];
            Points = Array.AsReadOnly(copy);
            Segments = Array.AsReadOnly(Array.Empty<ConveyorSegmentPlacement>());
        }

        public ConveyorDefinitionSnapshot(IReadOnlyList<ConveyorSegmentPlacement> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            Mode = ConveyorDefinitionMode.PrefabSegments;
            var copy = new ConveyorSegmentPlacement[segments.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = segments[index];
            Segments = Array.AsReadOnly(copy);
            Points = Array.AsReadOnly(Array.Empty<Vector3>());
        }
    }

    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Definition")]
    public sealed class ConveyorDefinition : ScriptableObject
    {
        #region Properties
        public ConveyorDefinitionMode Mode => mode;
        #endregion

        #region Serialized Fields
        [SerializeField] private ConveyorDefinitionMode mode;
        [SerializeField] private Vector3[] points =
        {
            new Vector3(-3, 0, -2), new Vector3(3, 0, -2),
            new Vector3(3, 0, 2), new Vector3(-3, 0, 2)
        };
        [SerializeField] private ConveyorSegmentPlacement[] segments = Array.Empty<ConveyorSegmentPlacement>();
        #endregion

        #region Public Methods
        public ConveyorDefinitionSnapshot Snapshot() => mode == ConveyorDefinitionMode.ControlPoints
            ? new ConveyorDefinitionSnapshot(points) : new ConveyorDefinitionSnapshot(segments);

        public void SetPoints(IReadOnlyList<Vector3> values)
        {
            var snapshot = new ConveyorDefinitionSnapshot(values);
            var copy = new Vector3[snapshot.Points.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = snapshot.Points[index];
            points = copy;
            mode = ConveyorDefinitionMode.ControlPoints;
        }

        public void SetSegments(IReadOnlyList<ConveyorSegmentPlacement> values)
        {
            var snapshot = new ConveyorDefinitionSnapshot(values);
            var copy = new ConveyorSegmentPlacement[snapshot.Segments.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = snapshot.Segments[index];
            segments = copy;
            mode = ConveyorDefinitionMode.PrefabSegments;
        }
        #endregion
    }
}
