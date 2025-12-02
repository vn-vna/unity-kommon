using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.PathController
{
    [Serializable]
    public struct PathWaypoint
    {
        public Transform point;
        public Transform handle1;
        public Transform handle2;
    }
}