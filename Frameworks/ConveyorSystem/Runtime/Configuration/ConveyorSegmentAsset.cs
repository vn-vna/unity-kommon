using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [CreateAssetMenu(menuName = "Scheherazade/Conveyor System/Prefab Segment")]
    public sealed class ConveyorSegmentAsset : ScriptableObject
    {
        #region Properties
        public GameObject Prefab => prefab;
        public float Width => width;
        public int PointCount => centerline == null ? 0 : centerline.Length;
        public Vector3 GetPoint(int index) => centerline[index];
        public Pose Entrance => Connector(false);
        public Pose Exit => Connector(true);
        #endregion

        #region Serialized Fields
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0.01f)] private float width = 1;
        [SerializeField] private Vector3[] centerline = { Vector3.zero, Vector3.forward * 2 };
        #endregion

        #region Public Methods
        public void Configure(GameObject visualPrefab, float beltWidth, IReadOnlyList<Vector3> path)
        {
            ConveyorValidation.Positive(beltWidth, nameof(beltWidth));
            if (path == null || path.Count < 2) throw new ArgumentException("A segment needs at least two path points.");
            var copy = new Vector3[path.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = path[index];
            prefab = visualPrefab;
            width = beltWidth;
            centerline = copy;
        }

        public void Validate()
        {
            ConveyorValidation.Positive(width, nameof(width));
            if (centerline == null || centerline.Length < 2) throw new ArgumentException("Segment path is missing.");
            for (int index = 0; index < centerline.Length; index++)
            {
                if (!ConveyorValidation.IsFinite(centerline[index])) throw new ArgumentException("Segment points must be finite.");
                if (index > 0 && (centerline[index] - centerline[index - 1]).sqrMagnitude < 0.000001f)
                    throw new ArgumentException("Segment edges cannot collapse.");
            }
            Connector(false);
            Connector(true);
        }
        #endregion

        #region Private Methods
        private Pose Connector(bool exit)
        {
            if (centerline == null || centerline.Length < 2) throw new ArgumentException("Segment path is missing.");
            int last = centerline.Length - 1;
            Vector3 direction = exit ? centerline[last] - centerline[last - 1] : centerline[1] - centerline[0];
            if (direction.sqrMagnitude < 0.000001f || Vector3.Cross(direction.normalized, Vector3.up).sqrMagnitude < 0.0001f)
                throw new ArgumentException("Segment connector direction must be nonzero and not vertical.");
            return new Pose(exit ? centerline[last] : centerline[0], Quaternion.LookRotation(direction, Vector3.up));
        }
        #endregion
    }
}
