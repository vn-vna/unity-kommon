namespace Com.Hapiga.Scheherazade.Common.Logging
{
    /// <summary>
    /// Defines the severity levels for log messages.
    /// </summary>
    /// <remarks>
    /// Log levels are used to categorize messages by importance, allowing filtering and
    /// color-coding in the Unity console.
    /// </remarks>
    public enum LogLevel
    {
        /// <summary>
        /// Detailed diagnostic information for debugging.
        /// </summary>
        Debug,
        
        /// <summary>
        /// Informational messages about normal operations.
        /// </summary>
        Info,
        
        /// <summary>
        /// Warning messages indicating potential issues.
        /// </summary>
        Warning,
        
        /// <summary>
        /// Error messages indicating failures.
        /// </summary>
        Error,
        
        /// <summary>
        /// Critical errors requiring immediate attention.
        /// </summary>
        Critical
    }
}