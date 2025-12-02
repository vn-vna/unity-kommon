using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [CreateAssetMenu(
        fileName = "AppMetricaTrackingConfiguration", 
        menuName = "Dev Menu/Integration/AppMetrica Tracking Configuration"
    )]
    public class AppMetricaTrackingConfiguration : ScriptableObject
    {
        public string ApiKey => apiKey;
        public float IapMultiplier => iapMultiplier;

        [SerializeField]
        private string apiKey;

        [SerializeField]
        private float iapMultiplier;
    }
}