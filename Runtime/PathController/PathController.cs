using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Extensions;
using UnityEngine;
using UnityEngine.Events;

namespace Com.Hapiga.Scheherazade.Common.PathController
{

    [AddComponentMenu("Scheherazade/Path Controller")]
    public class PathController : MonoBehaviour
    {
        public enum FlattenAxis { X, Y, Z }

        public List<PathWaypoint> waypoints = new List<PathWaypoint>();
        public Transform controlObject;
        public float duration = 3f;
        public bool teleportToFirstWaypoint = true;
        public bool showGizmo = true;
        public bool showGizmoSelected = true;
        public int gizmoResolution = 20;
        public Color pathColor = Color.blue;
        public Color anchorColor = Color.red;
        public Color handleColor = Color.green;
        public UnityEvent OnWaypointRemoved;
        public UnityEvent OnWaypointAdded;

        private void OnDrawGizmos()
        {
            if (!showGizmo) return;
            DrawPathGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmoSelected) return;
            DrawGizmo();
            DrawPathGizmo();
        }

        public Vector3[] GetPathPositions(int resolution = 30)
        {
            var positonsOnCurve = new Vector3[(waypoints.Count - 1) * resolution];
            GetPathPositionsNonAllocate(resolution, positonsOnCurve);
            return positonsOnCurve;
        }

        public void GetPathPositionsNonAllocate(int resolution, Vector3[] positionsOnCurve)
        {
            for (var i = 1; i < waypoints.Count; i++)
            {
                var bezierPoints = GetBezierPoints(i - 1, i);
                for (var j = 0; j < resolution; j++)
                {
                    var t = j / (float)resolution;
                    var pointOnCurve = GetBezierPoint(bezierPoints, t);
                    positionsOnCurve[(i - 1) * resolution + j] = pointOnCurve;
                }
            }
        }

        public void DrawAllGizmos()
        {
            if (!showGizmo) return;
            DrawPathGizmo();
            DrawGizmo();
        }

        private void DrawPathGizmo()
        {
            if (waypoints.Count > 1)
            {
                var gizmoPoints = GetPathPositions(gizmoResolution);

                Gizmos.color = pathColor;
                for (var i = 1; i < gizmoPoints.Length - 1; i++) Gizmos.DrawLine(gizmoPoints[i - 1], gizmoPoints[i]);
            }
        }

        private void DrawGizmo()
        {
            if (waypoints.Count > 1)
                foreach (var waypoint in waypoints)
                {
                    Gizmos.color = anchorColor;
                    Gizmos.DrawSphere(waypoint.point.position, 0.1f);
                    Gizmos.DrawSphere(waypoint.handle1.position, 0.1f);
                    Gizmos.DrawSphere(waypoint.handle2.position, 0.1f);

                    Gizmos.color = handleColor;
                    Gizmos.DrawLine(waypoint.point.position, waypoint.handle1.position);
                    Gizmos.DrawLine(waypoint.point.position, waypoint.handle2.position);
                }
        }

        private Vector3 GetBezierPoint(Vector3[] bezierPoints, float t)
        {
            var p0 = bezierPoints[0];
            var p1 = bezierPoints[1];
            var p2 = bezierPoints[2];
            var p3 = bezierPoints[3];

            return Mathf.Pow(1 - t, 3) * p0 + 3 * Mathf.Pow(1 - t, 2) * t * p1 + 3 * (1 - t) * Mathf.Pow(t, 2) * p2 +
                   Mathf.Pow(t, 3) * p3;
        }

        private Vector3[] GetBezierPoints(int index1, int index2)
        {
            var wp1 = waypoints[index1];
            var wp2 = waypoints[index2];

            var p0 = wp1.point.position;
            var p1 = wp1.handle2.position;
            var p2 = wp2.handle1.position;
            var p3 = wp2.point.position;

            return new[] { p0, p1, p2, p3 };
        }

        public void AddWaypoint(Vector3? point = null, Vector3? handle1 = null, Vector3? handle2 = null)
        {
            var waypointObject = new GameObject("Waypoint");
            waypointObject.transform.SetParent(transform);
            waypointObject.transform.localPosition = point ?? Vector3.zero;
            waypointObject.transform.localScale = Vector3.one;
            waypointObject.AddComponent<PathControllerWaypoint>().controller = this;

            var handle1Object = new GameObject("Handle 1");
            handle1Object.transform.SetParent(waypointObject.transform);
            handle1Object.transform.localPosition = handle1 ?? Vector3.zero;
            handle1Object.transform.localScale = Vector3.one;
            handle1Object.AddComponent<PathControllerWaypointHandle>().controller = this;

            var handle2Object = new GameObject("Handle 2");
            handle2Object.transform.SetParent(waypointObject.transform);
            handle2Object.transform.localPosition = handle2 ?? Vector3.zero;
            handle2Object.transform.localScale = Vector3.one;
            handle2Object.AddComponent<PathControllerWaypointHandle>().controller = this;

            // If current path controller using RectTransform, convert the waypoint and its handle to RectTransform
            if (transform is RectTransform)
            {
                waypointObject.AddComponent<RectTransform>();
                handle1Object.AddComponent<RectTransform>();
                handle2Object.AddComponent<RectTransform>();
            }

            RefreshWaypoints();

            OnWaypointAdded.Invoke();
        }

        public void RefreshWaypoints()
        {
            var wps = new List<PathWaypoint>();

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var point = child.GetComponent<PathControllerWaypoint>();
                point.controller = this;
                if (point == null) continue;

                if (point.transform.childCount != 2) continue;

                var handle1 = point.transform.GetChild(0).gameObject.GetComponent<PathControllerWaypointHandle>();
                var handle2 = point.transform.GetChild(1).gameObject.GetComponent<PathControllerWaypointHandle>();

                handle1.controller = this;
                handle2.controller = this;

                if (handle1 == null || handle2 == null) continue;

                wps.Add(new PathWaypoint
                {
                    point = point.transform,
                    handle1 = handle1.transform,
                    handle2 = handle2.transform
                });
            }

            waypoints = wps;
        }

        public void RemoveWaypoint(PathControllerWaypoint pathControllerWaypoint)
        {
            PathWaypoint? selectedWaypoint = null;

            foreach (var waypoint in waypoints)
                if (waypoint.point.gameObject == pathControllerWaypoint.gameObject)
                {
                    selectedWaypoint = waypoint;
                    break;
                }

#if UNITY_EDITOR
            DestroyImmediate(pathControllerWaypoint.gameObject);
#else
            Destroy(pathControllerWaypoint.gameObject);
#endif

            RefreshWaypoints();
            OnWaypointRemoved.Invoke();
        }

        public void FlattenWaypointsOnPlane(FlattenAxis axis)
        {
            foreach (var waypoint in waypoints)
                switch (axis)
                {
                    case FlattenAxis.X:
                        waypoint.point.localPosition = waypoint.point.position.WithX(0);
                        break;
                    case FlattenAxis.Y:
                        waypoint.point.localPosition = waypoint.point.position.WithY(0);
                        break;
                    case FlattenAxis.Z:
                        waypoint.point.localPosition = waypoint.point.position.WithZ(0);
                        break;
                }
        }

        public void FlattenWaypointOnPlaneYZ()
        {
            FlattenWaypointsOnPlane(FlattenAxis.X);
        }

        public void FlattenWaypointOnPlaneXZ()
        {
            FlattenWaypointsOnPlane(FlattenAxis.Y);
        }

        public void FlattenWaypointOnPlaneXY()
        {
            FlattenWaypointsOnPlane(FlattenAxis.Z);
        }

        public static void GetAnchoredPosition(Vector3[] pathPositions, float anchoredLength, float pathLength, out Vector3 followingPosition, out Vector3 facingDirection)
        {
            anchoredLength = Mathf.Clamp(anchoredLength, 0, pathLength);
            float currentPathLength = 0;
            float lastPathLength = 0;

            var lastIndex = 0;
            for (var i = 1; i < pathPositions.Length; i++)
            {
                currentPathLength += (pathPositions[i] - pathPositions[i - 1]).magnitude;
                if (currentPathLength > anchoredLength) break;
                lastPathLength = currentPathLength;
                lastIndex = i;
            }

            var anchoredPosition = pathPositions[lastIndex];
            facingDirection = Vector3.zero;

            if (lastIndex < pathPositions.Length - 1)
            {
                var momentum = pathPositions[lastIndex + 1] - pathPositions[lastIndex];
                momentum = momentum.normalized * (anchoredLength - lastPathLength);
                anchoredPosition += momentum;
                facingDirection = momentum;
            }

            followingPosition = anchoredPosition;
        }
    }
}