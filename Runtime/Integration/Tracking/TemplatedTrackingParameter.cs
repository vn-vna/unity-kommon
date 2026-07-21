using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Serializable]
    public class TemplatedTrackingParameter
    {
        [Tooltip("Unique identifier — matched against [TrackingParamGetter(\"id\")] methods")]
        [SerializeField]
        private string _parameterId;

        [Tooltip("User-friendly display name — used as the key when sending the final event")]
        [SerializeField]
        private string _name;

        [Tooltip("Optional description for documentation")]
        [TextArea(1, 3)]
        [SerializeField]
        private string _description;

        [Tooltip("Fallback value used when no getter or factory provides this parameter")]
        [SerializeField]
        private string _defaultValue;

        public string ParameterId =>
            !string.IsNullOrEmpty(_parameterId) ? _parameterId : _name;

        public string Name => _name;
        public string Description => _description;
        public string DefaultValue => _defaultValue;
    }
}
