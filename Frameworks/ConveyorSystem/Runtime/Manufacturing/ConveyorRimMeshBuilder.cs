using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public static class ConveyorRimMeshBuilder
    {
        public static Mesh Build(ConveyorPath path, float lateralOffset, float height, float radius, Matrix4x4 worldToLocal, int sides = 10)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            ConveyorValidation.Positive(radius, nameof(radius));
            if (!ConveyorValidation.IsFinite(lateralOffset) || !ConveyorValidation.IsFinite(height) || sides < 3 || sides > 32)
                throw new ArgumentException("Rim settings must be finite with 3–32 radial sides.");
            var vertices = new Vector3[path.PointCount * sides];
            var indices = new int[path.PointCount * sides * 6];
            for (int point = 0; point < path.PointCount; point++)
            {
                Vector3 incoming = (path.GetPoint(point) - path.GetPoint((point + path.PointCount - 1) % path.PointCount)).normalized;
                Vector3 outgoing = (path.GetPoint((point + 1) % path.PointCount) - path.GetPoint(point)).normalized;
                Vector3 tangent = incoming + outgoing;
                if (tangent.sqrMagnitude < 0.0001f) throw new ArgumentException("A rim cannot make a reversing cusp.");
                Vector3 right = Vector3.Cross(path.Up, tangent.normalized).normalized;
                Vector3 center = path.GetPoint(point) + right * lateralOffset + path.Up * height;
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    vertices[point * sides + side] = worldToLocal.MultiplyPoint3x4(center
                        + radius * (right * Mathf.Cos(angle) + path.Up * Mathf.Sin(angle)));
                    int a = point * sides + side;
                    int b = point * sides + (side + 1) % sides;
                    int c = ((point + 1) % path.PointCount) * sides + side;
                    int d = ((point + 1) % path.PointCount) * sides + (side + 1) % sides;
                    int index = (point * sides + side) * 6;
                    indices[index] = a; indices[index + 1] = b; indices[index + 2] = c;
                    indices[index + 3] = b; indices[index + 4] = d; indices[index + 5] = c;
                }
            }
            var mesh = new Mesh { name = "Conveyor rounded rim", indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.vertices = vertices;
                mesh.triangles = indices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { ConveyorObjectLifetime.DestroyOwned(mesh); throw; }
        }
    }
}
