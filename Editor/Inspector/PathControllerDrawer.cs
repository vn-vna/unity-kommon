using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    [CustomEditor(typeof(PathController))]
    public class PathControllerDrawer : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Path Controller", EditorStyles.boldLabel);
            PathController pathController = (PathController)target;

            pathController.controlObject = (Transform)EditorGUILayout.ObjectField("Control Object", pathController.controlObject, typeof(Transform), true);
            pathController.duration = EditorGUILayout.FloatField("Duration", pathController.duration);
            pathController.teleportToFirstWaypoint = EditorGUILayout.Toggle("Teleport to First Waypoint", pathController.teleportToFirstWaypoint);

            if (GUILayout.Button("Add Waypoint"))
            {
                pathController.AddWaypoint();
            }

            if (GUILayout.Button("Refresh Waypoints"))
            {
                pathController.RefreshWaypoints();
            }

        }
    }
}