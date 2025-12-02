using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    public class PathControllerWaypoint : MonoBehaviour
    {
        public PathController controller;

        private void OnDrawGizmosSelected()
        {
            controller.DrawAllGizmos();
        }

        private void RemoveWaypoint()
        {
            controller.RemoveWaypoint(this);
        }

        private void AddWaypoint()
        {
            controller.AddWaypoint(transform.localPosition);
        }
    }
}