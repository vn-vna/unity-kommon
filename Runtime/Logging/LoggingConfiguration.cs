using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Logging
{
    [CreateAssetMenu(fileName = "LoggingConfiguration", menuName = "Scheherazade/Logging/Logging Configuration")]
    public class LoggingConfiguration
        : ScriptableObject
    {
        public Color debugColor = Color.gray;
        public Color infoColor = Color.white;
        public Color warningColor = Color.yellow;
        public Color errorColor = Color.red;
        public Color criticalColor = Color.magenta;

        public LogLevel minimumLogLevel = LogLevel.Debug;
        public bool forceUsingWarningAsError = false;
    }
}