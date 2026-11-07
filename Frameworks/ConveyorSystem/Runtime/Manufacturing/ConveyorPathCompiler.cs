using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class RoundedPolylinePathProvider : IConveyorPathProvider
    {
        private readonly float _radius;
        private readonly int _cornerSteps;

        public RoundedPolylinePathProvider(float radius = 0.5f, int cornerSteps = 8)
        {
            if (!ConveyorValidation.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (cornerSteps < 1 || cornerSteps > 64) throw new ArgumentOutOfRangeException(nameof(cornerSteps));
            _radius = radius;
            _cornerSteps = cornerSteps;
        }

        public ConveyorPath Bake(ConveyorDefinitionSnapshot definition, Matrix4x4 localToWorld)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            List<Vector3> local = definition.Mode == ConveyorDefinitionMode.PrefabSegments
                ? ConveyorPathCompiler.CompileSegments(definition) : CompilePoints(definition.Points);
            var world = new Vector3[local.Count];
            for (int index = 0; index < world.Length; index++) world[index] = localToWorld.MultiplyPoint3x4(local[index]);
            return new ConveyorPath(world, localToWorld.MultiplyVector(Vector3.up));
        }

        private List<Vector3> CompilePoints(IReadOnlyList<Vector3> points)
        {
            ConveyorPathCompiler.ValidateControlPoints(points);
            var result = new List<Vector3>();
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 point = points[index];
                Vector3 before = points[(index + points.Count - 1) % points.Count] - point;
                Vector3 after = points[(index + 1) % points.Count] - point;
                float radius = Mathf.Min(_radius, Mathf.Min(before.magnitude, after.magnitude) * 0.43f);
                if (radius < 0.0001f) { result.Add(point); continue; }
                Vector3 start = point + before.normalized * radius;
                Vector3 end = point + after.normalized * radius;
                for (int step = 0; step <= _cornerSteps; step++)
                {
                    float t = (float)step / _cornerSteps;
                    float u = 1 - t;
                    result.Add(u * u * start + 2 * u * t * point + t * t * end);
                }
            }
            return result;
        }
    }

    public static class ConveyorPathCompiler
    {
        public static void ValidateControlPoints(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 3 || points.Count > 256) throw new ArgumentException("Author 3–256 control points.");
            float area = 0;
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 a = points[index];
                Vector3 b = points[(index + 1) % points.Count];
                if (!ConveyorValidation.IsFinite(a) || Math.Abs(a.y - points[0].y) > 0.001f)
                    throw new ArgumentException("Default control-point authoring requires a finite local XZ-plane loop.");
                if ((a - b).sqrMagnitude < 0.0001f) throw new ArgumentException("Adjacent path points are too close.");
                area += a.x * b.z - b.x * a.z;
                ValidateIntersections(points, index);
            }
            if (Math.Abs(area) < 0.001f) throw new ArgumentException("Path must enclose a nonzero area.");
        }

        public static List<Vector3> CompileSegments(ConveyorDefinitionSnapshot definition)
        {
            if (definition.Segments.Count < 1 || definition.Segments.Count > 1024)
                throw new ArgumentException("Author 1–1024 placed prefab segments.");
            var result = new List<Vector3>();
            for (int index = 0; index < definition.Segments.Count; index++)
            {
                ConveyorSegmentPlacement placement = definition.Segments[index];
                if (placement.segment == null) throw new ArgumentException("A segment asset is missing.");
                placement.segment.Validate();
                ConveyorSegmentPlacement next = definition.Segments[(index + 1) % definition.Segments.Count];
                if (next.segment == null) throw new ArgumentException("A segment asset is missing.");
                ValidateJoin(placement, next);
                for (int point = 0; point < placement.segment.PointCount; point++)
                {
                    Vector3 position = placement.Matrix.MultiplyPoint3x4(placement.segment.GetPoint(point));
                    if (result.Count == 0 || (result[result.Count - 1] - position).sqrMagnitude > 0.000001f) result.Add(position);
                }
            }
            if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude < 0.000001f) result.RemoveAt(result.Count - 1);
            return result;
        }

        public static ConveyorSegmentPlacement SnapAfter(ConveyorSegmentPlacement previous, ConveyorSegmentAsset next)
        {
            if (previous.segment == null || next == null) throw new ArgumentException("Both segment assets are required.");
            previous.segment.Validate(); next.Validate();
            Pose exit = previous.segment.Exit;
            Pose entrance = next.Entrance;
            Quaternion rotation = Quaternion.Euler(previous.eulerAngles) * exit.rotation * Quaternion.Inverse(entrance.rotation);
            Vector3 position = previous.Matrix.MultiplyPoint3x4(exit.position) - rotation * entrance.position;
            return new ConveyorSegmentPlacement { segment = next, position = position, eulerAngles = rotation.eulerAngles };
        }

        private static void ValidateJoin(ConveyorSegmentPlacement left, ConveyorSegmentPlacement right)
        {
            Pose exit = left.segment.Exit;
            Pose entrance = right.segment.Entrance;
            Vector3 end = left.Matrix.MultiplyPoint3x4(exit.position);
            Vector3 start = right.Matrix.MultiplyPoint3x4(entrance.position);
            Quaternion endRotation = Quaternion.Euler(left.eulerAngles) * exit.rotation;
            Quaternion startRotation = Quaternion.Euler(right.eulerAngles) * entrance.rotation;
            if ((end - start).sqrMagnitude > 0.000025f || Quaternion.Angle(endRotation, startRotation) > 2)
                throw new ArgumentException("Prefab segment connectors do not close continuously (5 mm / 2 degree tolerance).");
        }

        private static void ValidateIntersections(IReadOnlyList<Vector3> points, int index)
        {
            Vector3 a = points[index];
            Vector3 b = points[(index + 1) % points.Count];
            for (int other = index + 1; other < points.Count; other++)
            {
                if (other == index + 1 || (index == 0 && other == points.Count - 1)) continue;
                Vector3 c = points[other];
                Vector3 d = points[(other + 1) % points.Count];
                if (SegmentsIntersect(a, b, c, d)) throw new ArgumentException("The control-point loop intersects itself.");
            }
        }

        private static bool SegmentsIntersect(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float abC = Turn(a, b, c), abD = Turn(a, b, d), cdA = Turn(c, d, a), cdB = Turn(c, d, b);
            return (abC * abD < 0 && cdA * cdB < 0) || OnSegment(a, b, c, abC)
                || OnSegment(a, b, d, abD) || OnSegment(c, d, a, cdA) || OnSegment(c, d, b, cdB);
        }

        private static float Turn(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        private static bool OnSegment(Vector3 a, Vector3 b, Vector3 p, float turn) => Math.Abs(turn) < 0.000001f
            && p.x >= Mathf.Min(a.x, b.x) - 0.000001f && p.x <= Mathf.Max(a.x, b.x) + 0.000001f
            && p.z >= Mathf.Min(a.z, b.z) - 0.000001f && p.z <= Mathf.Max(a.z, b.z) + 0.000001f;
    }
}
