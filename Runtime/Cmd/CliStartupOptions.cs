#if !NO_CLI_STARTUP_OPTIONSA

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common
{
    [AddComponentMenu("Scheherazade/Cmd/CLI Startup Options")]
    public class CliStartupOptions :
        SingletonBehavior<CliStartupOptions>
    {
        public const string EditorArgumentsFileName = "cli-args";

        public IReadOnlyDictionary<string, string> Options => _options;

        private readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateCliStartupOptions()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject cliStartupOptions = new GameObject(nameof(CliStartupOptions));
            cliStartupOptions.AddComponent<KeepAliveComponent>();
            var cso = cliStartupOptions.AddComponent<CliStartupOptions>();
            cso.Reload();
        }

        public bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value);
        }

        public string GetValueOrDefault(string key, string defaultValue = null)
        {
            return _options.TryGetValue(key, out string value)
                ? value
                : defaultValue;
        }

        public void Reload()
        {
            _options.Clear();

            foreach (KeyValuePair<string, string> option in ReadStartupOptions())
            {
                _options[option.Key] = option.Value;
            }

            QuickLog.Debug<CliStartupOptions>(
                "Loaded {0} startup option(s): [{1}]",
                _options.Count,
                (Func<object>) DisplayOptionsForDebug
            );
        }

        private string DisplayOptionsForDebug()
        {
            return string.Join(
                ", ",
                _options.Select(option => $"{option.Key}={option.Value}")
            );
        }

        private IEnumerable<KeyValuePair<string, string>> ReadStartupOptions()
        {
#if UNITY_EDITOR
            return ReadEditorOptions();
#elif UNITY_ANDROID
            return ReadAndroidOptions();
#else
            return ReadCommandLineOptions();
#endif
        }

        private IEnumerable<KeyValuePair<string, string>> ReadCommandLineOptions()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 1; i < args.Length; i++)
            {
                string token = args[i];
                if (!TryGetArgumentKey(token, out string key))
                {
                    continue;
                }

                if (TrySplitInlineAssignment(key, out string inlineKey, out string inlineValue))
                {
                    yield return new KeyValuePair<string, string>(inlineKey, inlineValue);
                    continue;
                }

                string value = bool.TrueString;
                if (i + 1 < args.Length && !IsArgumentToken(args[i + 1]))
                {
                    value = TrimWrappedQuotes(args[i + 1]);
                    i++;
                }

                yield return new KeyValuePair<string, string>(key, value);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> ReadEditorOptions()
        {
            string filePath = Path.Combine(Application.dataPath, EditorArgumentsFileName);
            if (!File.Exists(filePath))
            {
                yield break;
            }

            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(filePath))
            {
                lineNumber++;
                if (!TryParseIniLine(rawLine, out string key, out string value))
                {
                    if (!IsIgnorableIniLine(rawLine))
                    {
                        QuickLog.Warning<CliStartupOptions>(
                            "Ignoring malformed startup option at {0}:{1}: {2}",
                            filePath,
                            lineNumber,
                            rawLine
                        );
                    }
                    continue;
                }

                key = key.TrimStart('-');
                if (string.IsNullOrWhiteSpace(key))
                {
                    QuickLog.Warning<CliStartupOptions>(
                        "Ignoring empty startup option key at {0}:{1}.",
                        filePath,
                        lineNumber
                    );
                    continue;
                }

                yield return new KeyValuePair<string, string>(key, value);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerable<KeyValuePair<string, string>> ReadAndroidOptions()
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject intent = activity?.Call<AndroidJavaObject>("getIntent");
            using AndroidJavaObject extras = intent?.Call<AndroidJavaObject>("getExtras");
            if (extras == null)
            {
                yield break;
            }

            using AndroidJavaObject keySet = extras.Call<AndroidJavaObject>("keySet");
            using AndroidJavaObject iterator = keySet?.Call<AndroidJavaObject>("iterator");
            if (iterator == null)
            {
                yield break;
            }

            while (iterator.Call<bool>("hasNext"))
            {
                string key = iterator.Call<string>("next");
                using AndroidJavaObject valueObject = extras.Call<AndroidJavaObject>("get", key);
                string value = valueObject?.Call<string>("toString") ?? string.Empty;
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
#endif

        private static bool TryParseIniLine(string rawLine, out string key, out string value)
        {
            key = null;
            value = null;

            if (IsIgnorableIniLine(rawLine))
            {
                return false;
            }

            string line = rawLine.Trim();
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf(':');
            }

            if (separatorIndex < 0)
            {
                return false;
            }

            key = line.Substring(0, separatorIndex).Trim();
            value = TrimWrappedQuotes(line.Substring(separatorIndex + 1).Trim());
            return true;
        }

        private static bool TryGetArgumentKey(string token, out string key)
        {
            key = null;
            if (!IsArgumentToken(token))
            {
                return false;
            }

            key = token.TrimStart('-');
            return !string.IsNullOrWhiteSpace(key);
        }

        private static bool TrySplitInlineAssignment(string token, out string key, out string value)
        {
            int separatorIndex = token.IndexOf('=');
            if (separatorIndex < 0)
            {
                key = token;
                value = null;
                return false;
            }

            key = token.Substring(0, separatorIndex).Trim();
            value = TrimWrappedQuotes(token.Substring(separatorIndex + 1).Trim());
            return true;
        }

        private static bool IsArgumentToken(string token)
        {
            return !string.IsNullOrWhiteSpace(token) && token.StartsWith("-");
        }

        private static bool IsIgnorableIniLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return true;
            }

            string line = rawLine.Trim();
            return
                line.StartsWith(";") ||
                line.StartsWith("#") ||
                (line.StartsWith("[") && line.EndsWith("]"));
        }

        private static string TrimWrappedQuotes(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2)
            {
                return value;
            }

            return value[0] == '"' && value[value.Length - 1] == '"'
                ? value.Substring(1, value.Length - 2)
                : value;
        }
    }
}

#endif