using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public static class ConveyorMeshBuilder
    {
        public static Mesh Build(ConveyorPath path, float width, float depth, Matrix4x4 worldToLocal)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            ConveyorValidation.Positive(width, nameof(width));
            ConveyorValidation.Positive(depth, nameof(depth));
            var vertices = new List<Vector3>(path.PointCount * 16);
            var triangles = new List<int>(path.PointCount * 24);
            var uv = new List<Vector2>(path.PointCount * 16);
            double distance = 0;
            for (int index = 0; index < path.PointCount; index++)
            {
                Vector3 a = path.GetPoint(index);
                Vector3 b = path.GetPoint((index + 1) % path.PointCount);
                Vector3 rightA = RightAt(path, index) * (width * 0.5f);
                Vector3 rightB = RightAt(path, (index + 1) % path.PointCount) * (width * 0.5f);
                Vector3 down = -path.Up * depth;
                Vector3 al = a - rightA, ar = a + rightA, bl = b - rightB, br = b + rightB;
                float start = (float)distance;
                distance += (b - a).magnitude;
                float end = (float)distance;
                AddQuad(vertices, triangles, uv, worldToLocal, al, bl, br, ar, start, end);
                AddQuad(vertices, triangles, uv, worldToLocal, ar, br, br + down, ar + down, start, end);
                AddQuad(vertices, triangles, uv, worldToLocal, bl, al, al + down, bl + down, start, end);
                AddQuad(vertices, triangles, uv, worldToLocal, al + down, ar + down, br + down, bl + down, start, end);
            }
            var mesh = new Mesh { name = "Conveyor generated belt", indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.SetUVs(0, uv);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { ConveyorObjectLifetime.DestroyOwned(mesh); throw; }
        }

        private static Vector3 RightAt(ConveyorPath path, int index)
        {
            Vector3 point = path.GetPoint(index);
            Vector3 incoming = (point - path.GetPoint((index + path.PointCount - 1) % path.PointCount)).normalized;
            Vector3 outgoing = (path.GetPoint((index + 1) % path.PointCount) - point).normalized;
            Vector3 tangent = incoming + outgoing;
            if (tangent.sqrMagnitude < 0.0001f) throw new ArgumentException("Belt geometry cannot make a reversing cusp.");
            return Vector3.Cross(path.Up, tangent.normalized).normalized;
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
            Matrix4x4 transform, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float start, float end)
        {
            int offset = vertices.Count;
            vertices.Add(transform.MultiplyPoint3x4(a)); vertices.Add(transform.MultiplyPoint3x4(b));
            vertices.Add(transform.MultiplyPoint3x4(c)); vertices.Add(transform.MultiplyPoint3x4(d));
            uv.Add(new Vector2(0, start)); uv.Add(new Vector2(0, end)); uv.Add(new Vector2(1, end)); uv.Add(new Vector2(1, start));
            triangles.Add(offset); triangles.Add(offset + 1); triangles.Add(offset + 2);
            triangles.Add(offset); triangles.Add(offset + 2); triangles.Add(offset + 3);
        }
    }
}
