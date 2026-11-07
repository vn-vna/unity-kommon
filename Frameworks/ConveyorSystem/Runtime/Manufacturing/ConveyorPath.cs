using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    /// <summary>Immutable world-space closed polyline with arc-length lookup. No slot/capacity policy.</summary>
    public sealed class ConveyorPath
    {
        #region Properties
        public int PointCount => _points.Length;
        public double Length => _distances[_points.Length];
        public Vector3 Up { get; }
        public Vector3 GetPoint(int index) => _points[index];
        #endregion

        #region Private Fields
        private readonly Vector3[] _points;
        private readonly double[] _distances;
        #endregion

        #region Construction
        public ConveyorPath(IReadOnlyList<Vector3> points, Vector3 up)
        {
            if (points == null || points.Count < 3 || points.Count > 32768)
                throw new ArgumentException("A closed path requires 3–32768 points.", nameof(points));
            if (!ConveyorValidation.IsFinite(up) || up.sqrMagnitude < 0.0001f)
                throw new ArgumentException("A finite nonzero up direction is required.", nameof(up));
            Up = up.normalized;
            _points = new Vector3[points.Count];
            _distances = new double[points.Count + 1];
            for (int index = 0; index < points.Count; index++)
            {
                if (!ConveyorValidation.IsFinite(points[index])) throw new ArgumentException("Path points must be finite.");
                _points[index] = points[index];
            }
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 delta = _points[(index + 1) % points.Count] - _points[index];
                if (delta.sqrMagnitude < 0.000001f || Vector3.Cross(delta.normalized, Up).sqrMagnitude < 0.0001f)
                    throw new ArgumentException("Path edges must be nonzero and not parallel to the up direction.");
                _distances[index + 1] = _distances[index] + delta.magnitude;
            }
        }
        #endregion

        #region Public Methods
        public Pose Evaluate(double distance)
        {
            if (!ConveyorValidation.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
            distance = ConveyorValidation.Wrap(distance, Length);
            int low = 0;
            int high = _points.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_distances[middle] <= distance) low = middle;
                else high = middle - 1;
            }
            Vector3 delta = _points[(low + 1) % _points.Length] - _points[low];
            float fraction = (float)((distance - _distances[low]) / (_distances[low + 1] - _distances[low]));
            return new Pose(_points[low] + delta * fraction, Quaternion.LookRotation(delta, Up));
        }

        public Pose EvaluateLane(double distance, int channel, int channels, float width)
        {
            if (channels < 1 || (uint)channel >= (uint)channels) throw new ArgumentOutOfRangeException(nameof(channel));
            ConveyorValidation.Positive(width, nameof(width));
            Pose pose = Evaluate(distance);
            pose.position += pose.rotation * Vector3.right * (((channel + 0.5f) / channels - 0.5f) * width);
            return pose;
        }

        public double FindNearestDistance(Vector3 position)
        {
            if (!ConveyorValidation.IsFinite(position)) throw new ArgumentException("Position must be finite.");
            double nearest = 0;
            float best = float.PositiveInfinity;
            for (int index = 0; index < _points.Length; index++)
            {
                Vector3 start = _points[index];
                Vector3 delta = _points[(index + 1) % _points.Length] - start;
                float fraction = Mathf.Clamp01(Vector3.Dot(position - start, delta) / delta.sqrMagnitude);
                float squared = (position - start - delta * fraction).sqrMagnitude;
                if (squared >= best) continue;
                best = squared;
                nearest = _distances[index] + fraction * (_distances[index + 1] - _distances[index]);
            }
            return ConveyorValidation.Wrap(nearest, Length);
        }
        #endregion
    }
}
