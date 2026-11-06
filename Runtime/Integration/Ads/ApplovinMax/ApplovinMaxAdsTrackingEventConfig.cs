using System;
using Com.Scheherazade.Common.Integration.Tracking;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.Ads
{
    [Serializable]
    public class ApplovinMaxAdsTrackingEventConfig
    {
        #region Interfaces & Properties

        public ApplovinMaxAdsTrackingEventType Type => type;

        public bool IsEnabled => enabled;

        public string ActionId => actionId;

        public ActionSeverity Severity => severity;

        #endregion

        #region Serialized Fields

        [SerializeField]
        private ApplovinMaxAdsTrackingEventType type;

        [SerializeField]
        private bool enabled = true;

        [SerializeField]
        private string actionId;

        [SerializeField]
        private ActionSeverity severity = ActionSeverity.Debug;

        #endregion
    }
}
