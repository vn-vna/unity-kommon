using System;
using System.Collections.Generic;
using System.Diagnostics;
using Com.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Scheherazade.Common.Logging
{
    [AddComponentMenu("Scheherazade/Logging/Quick Log")]
    public class QuickLog :
        SingletonBehavior<QuickLog>
    {
        public LoggingConfiguration Configuration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateQuickLoggerInstance()
        {
            GameObject quickLogger = new GameObject("QuickLogger");
            quickLogger.AddComponent<KeepAliveComponent>();
            quickLogger.AddComponent<QuickLog>();
        }

        protected override void Awake()
        {
            base.Awake();

            if (!Configuration)
            {
                Configuration = Resources.Load<LoggingConfiguration>("LoggingConfiguration");
            }
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
            string msg;
#if UNITY_EDITOR
            string color = $"<color=#{ColorUtility.ToHtmlStringRGBA(GetDebugColor(level)).ToLower()}>";
            msg = string.Format(
                "{0}[{1}]</color> {2}",
                color, tag, message
            );
#else
            string severity = GetSeverityPrefix(level);
            msg = string.Format(
                "{0}[{1}] {2}",
                severity, tag, message
            );
#endif
            LogLevel effectiveLevel = GetEffectiveLogLevel(level);
            switch (effectiveLevel)
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
                    UnityEngine.Debug.LogError(msg);
                    break;
            }
        }

        private static string GetSeverityPrefix(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => "[DEBUG]",
                LogLevel.Info => "[INFO]",
                LogLevel.Warning => "[WARN]",
                LogLevel.Error => "[ERROR]",
                LogLevel.Critical => "[CRITICAL]",
                _ => "[LOG]",
            };
        }

        private LogLevel GetEffectiveLogLevel(LogLevel level)
        {
            if (!Configuration)
            {
                return level;
            }

            switch (Configuration.modificationBehavior)
            {
                case LogModificationBehavior.WarningAsError:
                    if (level == LogLevel.Warning) return LogLevel.Error;
                    break;
                case LogModificationBehavior.ErrorAsWarning:
                    if (level == LogLevel.Error || level == LogLevel.Critical) return LogLevel.Warning;
                    break;
            }
            return level;
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
#if PLA_CREATIVE_BUILD && UNITY_LUNA
            EvaluateCreativeLogArguments(args);
#else
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Func<object> func)
                {
                    args[i] = func();
                }
            }
#endif

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

#if PLA_CREATIVE_BUILD || UNITY_EDITOR
        private static void EvaluateCreativeLogArguments(object[] args)
        {
            if (args == null) return;
            for (int index = 0; index < args.Length; index++)
            {
                object argument = args[index];
                if (argument == null) continue;
                // Bridge marks Func<T> IgnoreCast, erasing an `is`/`as` check.
                // GetType retains the actual runtime check against JavaScript Function.
                if (argument.GetType() != typeof(Func<object>)) continue;
                args[index] = ((Func<object>)argument)();
            }
        }
#endif

#if PLA_CREATIVE_BUILD
        // Avoid the native compiler's implicit Array.Empty<object>() for omitted
        // params arguments: Bridge exposes that method as an unsupported Array.empty.
        [HideInCallstack]
        public static void Log<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Info,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Log<T>(string message, LogLevel level)
            => Log(message, typeof(T).Name, level,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Debug<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Debug,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Info<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Info,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Warning<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Warning,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Error<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Error,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());

        [HideInCallstack]
        public static void Critical<T>(string message)
            => Log(message, typeof(T).Name, LogLevel.Critical,
                Com.Scheherazade.Common.Compatibility.CreativeArray.Empty<object>());
#endif

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
#if PLA_CREATIVE_BUILD
                if (!IsVisibleForCreativeLog(frameType)) continue;
#else
                if (!frameType.IsVisible) continue;
#endif
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

#if PLA_CREATIVE_BUILD
        // Declaring types cannot be pointer/by-ref types. The remaining Type.IsVisible
        // rules can use Bridge's real type metadata rather than its absent property.
        private static bool IsVisibleForCreativeLog(Type type)
        {
            if (type == null) return false;
            if (type.IsGenericParameter) return true;
            if (type.IsArray) return IsVisibleForCreativeLog(type.GetElementType());
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                    if (!IsVisibleForCreativeLog(argument)) return false;
            }
            while (type != null && type.IsNested)
            {
                if (!type.IsNestedPublic) return false;
                type = type.DeclaringType;
            }
            return type != null && type.IsPublic;
        }
#endif

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