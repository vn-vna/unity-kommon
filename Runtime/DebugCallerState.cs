using System;
using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller
{
    public static class DebugCallerState
    {
        public const string PlayerPrefsKey = "Com.Scheherazade.DebugCaller.Enabled";
        public const string VersionSuffix = " [debug enabled]";

        public static event Action StateChanged;

        public static bool IsEnabled => PlayerPrefs.GetInt(PlayerPrefsKey, 0) == 1;

        public static void Enable()
        {
            if (IsEnabled)
            {
                return;
            }

            PlayerPrefs.SetInt(PlayerPrefsKey, 1);
            PlayerPrefs.Save();
            StateChanged?.Invoke();
        }

        public static void Clear()
        {
            if (!PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
            StateChanged?.Invoke();
        }

        public static string FormatVersion(string version)
        {
            string baseVersion = version ?? string.Empty;
            if (!IsEnabled || baseVersion.EndsWith(VersionSuffix, StringComparison.Ordinal))
            {
                return baseVersion;
            }

            return baseVersion + VersionSuffix;
        }
    }
}
