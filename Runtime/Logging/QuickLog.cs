using System;
using System.Collections.Generic;
using System.Diagnostics;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Logging
{
    [AddComponentMenu("Scheherazade/Logging/Quick Log")]
    public class QuickLog :
        SingletonBehavior<QuickLog>
    {
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


        // ReSharper disable Unity.PerformanceAnalysis
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
                if (args[i] is Func<object> func) args[i] = func();
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

        [HideInCallstack]
        // ReSharper disable Unity.PerformanceAnalysis
        public static void Log<T>(string message, LogLevel level = LogLevel.Info, params object[] args)
        {
            Log(message, typeof(T).Name, level, args);
        }

        [HideInCallstack]
        public static void Debug<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Debug, args);
        }

        [HideInCallstack]
        public static void Info<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Info, args);
        }

        [HideInCallstack]
        public static void Warning<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Warning, args);
        }

        [HideInCallstack]
        public static void Error<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Error, args);
        }

        [HideInCallstack]
        public static void Critical<T>(string message, params object[] args)
        {
            Log(message, typeof(T).Name, LogLevel.Critical, args);
        }

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

        [HideInCallstack]
        public static void SDebug(string message, params object[] args)
        {
            SLog(message, LogLevel.Debug, args);
        }

        [HideInCallstack]
        public static void SInfo(string message, params object[] args)
        {
            SLog(message, LogLevel.Info, args);
        }

        [HideInCallstack]
        public static void SWarning(string message, params object[] args)
        {
            SLog(message, LogLevel.Warning, args);
        }

        [HideInCallstack]
        public static void SError(string message, params object[] args)
        {
            SLog(message, LogLevel.Error, args);
        }

        [HideInCallstack]
        public static void SCritical(string message, params object[] args)
        {
            SLog(message, LogLevel.Critical, args);
        }

    }
}