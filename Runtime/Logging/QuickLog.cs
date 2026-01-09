using System;
using System.Collections.Generic;
using System.Diagnostics;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Logging
{
    /// <summary>
    /// Singleton logging system providing formatted, color-coded console output with filtering.
    /// </summary>
    /// <remarks>
    /// QuickLog offers static methods for easy logging from anywhere in the codebase.
    /// It supports automatic tag generation from calling type, log level filtering, and custom colors.
    /// Define NO_LOGGING to completely disable logging in builds.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Log with explicit type tag
    /// QuickLog.Info&lt;MyClass&gt;("Initialization complete");
    /// QuickLog.Error&lt;MyClass&gt;("Failed to load: {0}", fileName);
    /// 
    /// // Log with automatic tag from stack trace
    /// QuickLog.SDebug("Player health: {0}", health);
    /// QuickLog.SWarning("Low memory detected");
    /// 
    /// // Log with custom tag and level
    /// QuickLog.Log("Custom message", "MyTag", LogLevel.Info);
    /// </code>
    /// </example>
    [AddComponentMenu("Scheherazade/Logging/Quick Log")]
    public class QuickLog :
        SingletonBehavior<QuickLog>
    {
        /// <summary>
        /// Gets or sets the logging configuration containing colors and settings.
        /// </summary>
        public LoggingConfiguration Configuration;

        protected override void Awake()
        {
            base.Awake();

        }

        private Color GetDebugColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => Configuration ? Configuration.debugColor : Color.gray,
                LogLevel.Info => Configuration ? Configuration.infoColor : Color.white,
                LogLevel.Warning => Configuration ? Configuration.warningColor : Color.yellow,
                LogLevel.Error => Configuration ? Configuration.errorColor : Color.red,
                LogLevel.Critical => Configuration ? Configuration.criticalColor : Color.magenta,
                _ => Color.white,
            };
        }

        [HideInCallstack]
        private void LogMessage(string message, string tag = null, LogLevel level = LogLevel.Info)
        {
            string color = "";
            string et = "";

#if UNITY_EDITOR
            color = $"<color=#{ColorUtility.ToHtmlStringRGBA(GetDebugColor(level)).ToLower()}>";
            et = "</color>";
#endif
            string msg = string.Format(
                "{0}[{1}] {2}{3}",
                color, tag, message, et
            );
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(msg);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(msg);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    if (Configuration.forceUsingWarningAsError)
                    {
                        UnityEngine.Debug.LogWarning(msg);
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(msg);
                    }
                    break;
            }
        }


        /// <summary>
        /// Logs a message with the specified tag and level.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="tag">Optional tag to identify the message source.</param>
        /// <param name="level">The log level (default: Info).</param>
        /// <param name="args">Optional format arguments. Func&lt;object&gt; values are evaluated lazily.</param>
        /// <remarks>
        /// This is the core logging method. Messages below the configured minimum log level are filtered out.
        /// </remarks>
        [HideInCallstack]
        public static void Log(
            string message, string tag = null,
            LogLevel level = LogLevel.Info,
            object[] args = null
        )
        {
#if !NO_LOGGING
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Func<object> func)
                {
                    args[i] = func();
                }
            }

            string msg = args != null && args.Length > 0 ? string.Format(message, args) : message;

            if (Instance == null || Instance.Configuration == null)
            {
                UnityEngine.Debug.Log($"[{tag}] - {level} - {msg}");
                return;
            }

            if (level < Instance.Configuration.minimumLogLevel)
            {
                return;
            }

            Instance.LogMessage(msg, tag, level);
#endif
        }

        /// <summary>
        /// Logs a message with a tag derived from the specified type.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="level">The log level (default: Info).</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        // ReSharper disable Unity.PerformanceAnalysis
        public static void Log<T>(string message, LogLevel level = LogLevel.Info, params object[] args)
        {
            Log(message, typeof(T).Name, level, args);
        }

        /// <summary>
        /// Logs a debug message with the specified type tag.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void Debug<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Debug, args);
        }

        /// <summary>
        /// Logs an info message with the specified type tag.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void Info<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Info, args);
        }

        /// <summary>
        /// Logs a warning message with the specified type tag.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void Warning<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Warning, args);
        }

        /// <summary>
        /// Logs an error message with the specified type tag.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void Error<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Error, args);
        }

        /// <summary>
        /// Logs a critical message with the specified type tag.
        /// </summary>
        /// <typeparam name="T">The type to use for the tag.</typeparam>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void Critical<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Critical, args);
        }

        /// <summary>
        /// Logs a message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="level">The log level (default: Debug).</param>
        /// <param name="args">Format arguments.</param>
        /// <remarks>
        /// The 'S' prefix means "Stack" - the tag is automatically extracted from the calling method's type.
        /// This is convenient but slightly slower than explicit tags due to stack trace analysis.
        /// </remarks>
        [HideInCallstack]
        public static void SLog(string message, LogLevel level = LogLevel.Debug, params object[] args)
        {
#if !NO_LOGGING
            StackTrace stackTrace = new StackTrace();
            Type callingType = null;
            foreach (StackFrame frame in stackTrace.GetFrames())
            {
                Type frameType = frame.GetMethod().DeclaringType;
                if (frameType == typeof(QuickLog)) continue;
                if (!frameType.IsVisible) continue;
                if (frameType.FullName.StartsWith("<")) continue;
                if (frameType.FullName.Contains("System.")) continue;
                callingType = frameType;
                break;
            }

            if (callingType != null)
            {
                Log(message, callingType.Name, level, args);
            }
#endif
        }

        /// <summary>
        /// Logs a debug message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void SDebug(string message, params object[] args)
        {
            SLog(message, LogLevel.Debug, args);
        }

        /// <summary>
        /// Logs an info message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void SInfo(string message, params object[] args)
        {
            SLog(message, LogLevel.Info, args);
        }

        /// <summary>
        /// Logs a warning message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void SWarning(string message, params object[] args)
        {
            SLog(message, LogLevel.Warning, args);
        }

        /// <summary>
        /// Logs an error message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void SError(string message, params object[] args)
        {
            SLog(message, LogLevel.Error, args);
        }

        /// <summary>
        /// Logs a critical message with tag automatically determined from the call stack.
        /// </summary>
        /// <param name="message">The message format string.</param>
        /// <param name="args">Format arguments.</param>
        [HideInCallstack]
        public static void SCritical(string message, params object[] args)
        {
            SLog(message, LogLevel.Critical, args);
        }

    }
}