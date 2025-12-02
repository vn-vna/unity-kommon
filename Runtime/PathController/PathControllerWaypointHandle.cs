using Unity.Collections;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    public class PathControllerWaypointHandle : MonoBehaviour
    {
        [SerializeField]
        [ReadOnly]
        public PathController controller;

        private void OnDrawGizmosSelected()
        {
            controller.DrawAllGizmos();
        }
    }
}