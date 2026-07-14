using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public class TemplatedTrackingEvent : ScriptableObject
    {
        [Tooltip("Event name — used as the action ID when calling TrackTemplatedEvent(\"name\")")]
        [SerializeField]
        [HideInInspector]
        private string _eventName;

        [SerializeField]
        [HideInInspector]
        private List<TemplatedTrackingParameter> _parameters = new();

        public string EventName => _eventName;
        public IReadOnlyList<TemplatedTrackingParameter> Parameters => _parameters;
    }
}
