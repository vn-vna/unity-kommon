using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    public class PathMeshRenderer : MonoBehaviour
    {
        public enum PathMeshType
        {
            Plane,
            Surround
        }

        public enum PlaneType
        {
            Rounded,
            Sharp
        }

        public enum SurroundType
        {
            None,
            Capsule,
            Box
        }

        public PathController pathController;

        public MeshFilter meshFilter;

        public int pathResolution;

        public PathMeshType meshType;

        public PlaneType planeType;

        public SurroundType surroundType;

        public bool flipNormals;

        public Quaternion rotation;

        public float width;

        public float radius;

        public int radialSegments;

        private Mesh mesh;

        private void Start()
        {
            var path = pathController.GetPathPositions(pathResolution);
            var mesh = GenerateMesh(path.ToList());
            meshFilter.mesh = mesh;
        }

        private void Update()
        {
        }

        private void RefreshPreview()
        {
            mesh = GenerateMesh(pathController.GetPathPositions(pathResolution).ToList());
        }

        public Mesh GenerateMesh(List<Vector3> path)
        {
            switch (meshType)
            {
                case PathMeshType.Plane:
                    return GeneratePlaneMesh(path, width, rotation, 20);

                case PathMeshType.Surround:
                    return GenerateSurroundedMesh(path, radius, radialSegments, rotation);
            }

            return null;
        }

        public Mesh GeneratePlaneMesh(List<Vector3> path, float width, Quaternion rotation, int roundedSegments = 10)
        {
            if (path == null || path.Count < 2)
            {
                Debug.LogError("Path must have at least 2 points");
                return null;
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();

            var halfWidth = width * 0.5f;

            for (var i = 0; i < path.Count; i++)
            {
                var forward = Vector3.zero;

                if (i > 0 && i < path.Count - 1)
                {
                    // Compute a smooth forward vector using adjacent segments
                    var dir1 = (path[i] - path[i - 1]).normalized;
                    var dir2 = (path[i + 1] - path[i]).normalized;
                    forward = (dir1 + dir2).normalized;
                }
                else
                {
                    forward = i < path.Count - 1
                        ? (path[i + 1] - path[i]).normalized
                        : (path[i] - path[i - 1]).normalized;
                }

                // Compute the right vector using the bisector method
                Vector3 right;
                if (i == 0)
                {
                    // First point: Use simple cross product
                    right = Vector3.Cross(Vector3.up, forward).normalized;
                }
                else
                {
                    // Middle and last points: Use bisector method to ensure width stability
                    var prevForward = (path[i] - path[i - 1]).normalized;
                    var bisector = (prevForward + forward).normalized;

                    right = Vector3.Cross(Vector3.up, bisector).normalized;
                }

                // Ensure width consistency
                right *= halfWidth;

                var leftPoint = path[i] - right;
                var rightPoint = path[i] + right;

                vertices.Add(rotation * leftPoint);
                vertices.Add(rotation * rightPoint);

                var uvX = i / (float)(path.Count - 1);
                uvs.Add(new Vector2(uvX, 0));
                uvs.Add(new Vector2(uvX, 1));

                if (i > 0)
                {
                    var baseIndex = vertices.Count - 4;

                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 3);
                }
            }

            // Add rounded ends if required
            if (planeType == PlaneType.Rounded)
            {
                AddRoundedEnd(vertices, triangles, uvs, path[0], path[1], -halfWidth, rotation, roundedSegments, true);
                AddRoundedEnd(vertices, triangles, uvs, path[^1], path[^2], halfWidth, rotation, roundedSegments,
                    false);
            }

            var mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        public Mesh GenerateSurroundedMesh(List<Vector3> path, float radius, int radialSegments, Quaternion rotation)
        {
            if (path == null || path.Count < 2)
            {
                Debug.LogError("Path must have at least 2 points");
                return null;
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();

            var totalVerticesPerRing = radialSegments + 1; // Extra for UV seam
            var angleStep = Mathf.PI * 2 / radialSegments;

            for (var i = 0; i < path.Count; i++)
            {
                var forward = i < path.Count - 1
                    ? (path[i + 1] - path[i]).normalized
                    : (path[i] - path[i - 1]).normalized;

                // Compute stable up vector (avoiding flipping)
                var up = i == 0
                    ? Vector3.up
                    : Vector3.Cross(forward, Vector3.Cross(Vector3.up, forward)).normalized;
                var right = Vector3.Cross(forward, up).normalized;

                // Generate circular ring
                for (var j = 0; j <= radialSegments; j++) // Loop back to 0 for UV wrap
                {
                    var angle = j * angleStep;
                    var localPoint = Mathf.Cos(angle) * right + Mathf.Sin(angle) * up;
                    vertices.Add(rotation * (path[i] + localPoint * radius));
                    uvs.Add(new Vector2(j / (float)radialSegments, i / (float)(path.Count - 1)));
                }

                // Connect previous ring to create quads
                if (i > 0)
                {
                    var prevRingStart = (i - 1) * totalVerticesPerRing;
                    var currRingStart = i * totalVerticesPerRing;

                    for (var j = 0; j < radialSegments; j++)
                    {
                        var a = prevRingStart + j;
                        var b = prevRingStart + j + 1;
                        var c = currRingStart + j;
                        var d = currRingStart + j + 1;

                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);

                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(d);
                    }
                }
            }

            // Generate final pipe mesh
            var mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }


        private void AddRoundedEnd(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
            Vector3 center, Vector3 direction, float radius, Quaternion rotation, int segments, bool isStart)
        {
            var forward = (center - direction).normalized * (isStart ? -1 : 1);
            var right = Vector3.Cross(Vector3.up, forward).normalized * radius;

            var startIndex = vertices.Count;
            vertices.Add(rotation * center);
            uvs.Add(new Vector2(isStart ? 0 : 1, 0.5f));

            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.PI * (i / (float)segments);
                var point = center + Mathf.Cos(angle) * right + Mathf.Sin(angle) * forward * radius;
                vertices.Add(rotation * point);
                uvs.Add(new Vector2(isStart ? 0 : 1, i / (float)segments));

                if (i > 0)
                {
                    if (!flipNormals)
                    {
                        triangles.Add(startIndex);
                        triangles.Add(vertices.Count - 1);
                        triangles.Add(vertices.Count - 2);
                    }
                    else
                    {
                        triangles.Add(startIndex);
                        triangles.Add(vertices.Count - 2);
                        triangles.Add(vertices.Count - 1);
                    }
                }
            }
        }
    }
}