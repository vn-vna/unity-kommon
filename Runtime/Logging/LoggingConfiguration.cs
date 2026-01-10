using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Logging
{
    /// <summary>
    /// Configuration settings for the QuickLog logging system.
    /// </summary>
    /// <remarks>
    /// This ScriptableObject stores logging preferences including console colors for each log level
    /// and minimum log level filtering.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create configuration asset via menu: Assets > Create > Scheherazade > Logging > Logging Configuration
    /// // Then assign to QuickLog component in scene
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "LoggingConfiguration", menuName = "Scheherazade/Logging/Logging Configuration")]
    public class LoggingConfiguration
        : ScriptableObject
    {
        /// <summary>
        /// Color for debug level messages in the Unity console.
        /// </summary>
        public Color debugColor = Color.gray;
        
        /// <summary>
        /// Color for info level messages in the Unity console.
        /// </summary>
        public Color infoColor = Color.white;
        
        /// <summary>
        /// Color for warning level messages in the Unity console.
        /// </summary>
        public Color warningColor = Color.yellow;
        
        /// <summary>
        /// Color for error level messages in the Unity console.
        /// </summary>
        public Color errorColor = Color.red;
        
        /// <summary>
        /// Color for critical level messages in the Unity console.
        /// </summary>
        public Color criticalColor = Color.magenta;

        /// <summary>
        /// Minimum log level to display. Messages below this level are filtered out.
        /// </summary>
        public LogLevel minimumLogLevel = LogLevel.Debug;
        
        /// <summary>
        /// If true, error and critical messages use LogWarning instead of LogError to avoid stopping in the editor.
        /// </summary>
        public bool forceUsingWarningAsError = false;
    }
}