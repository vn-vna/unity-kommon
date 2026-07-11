// ═══════════════════════════════════════════════════════════
// ── AdbPathResolver ───────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Consolidated ADB and Android SDK path resolution.
    /// Replaces the duplicated logic previously spread across
    /// RoapAdbClient, AdbUtility, and DirectCmdForwardingWindow.
    /// </summary>
    public static class AdbPathResolver
    {
        #region Private Fields

        private static string _cachedAdbPath;
        private static string _cachedSdkRoot;
        private static bool _adbChecked;
        private static bool _sdkChecked;

        #endregion

        #region Public Properties

        /// <summary>
        /// Returns the full path to the ADB executable, or null if not found.
        /// Result is cached after first lookup.
        /// </summary>
        public static string AdbPath
        {
            get
            {
                if (!_adbChecked)
                {
                    _adbChecked = true;
                    _cachedAdbPath = FindAdb();
                }
                return _cachedAdbPath;
            }
        }

        /// <summary>
        /// Returns the Android SDK root path, or null if not found.
        /// </summary>
        public static string SdkRoot
        {
            get
            {
                if (!_sdkChecked)
                {
                    _sdkChecked = true;
                    _cachedSdkRoot = ResolveSdkRoot();
                }
                return _cachedSdkRoot;
            }
        }

        /// <summary>
        /// Returns true if ADB is available on this machine.
        /// </summary>
        public static bool IsAdbAvailable => !string.IsNullOrEmpty(AdbPath);

        #endregion

        #region Public Methods

        /// <summary>
        /// Invalidates the cached ADB/SDK paths, forcing re-resolution on next access.
        /// </summary>
        public static void InvalidateCache()
        {
            _adbChecked = false;
            _sdkChecked = false;
            _cachedAdbPath = null;
            _cachedSdkRoot = null;
        }

        /// <summary>
        /// Returns a human-readable display string for the ADB location.
        /// </summary>
        public static string GetDisplayString()
        {
            return IsAdbAvailable ? AdbPath : "adb not found";
        }

        #endregion

        #region Private Methods

        private static string FindAdb()
        {
            string executableName = Application.platform == RuntimePlatform.WindowsEditor
                ? "adb.exe"
                : "adb";

            // 1. SDK root / platform-tools
            string sdkRoot = SdkRoot;
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                string candidate = Path.Combine(sdkRoot, "platform-tools", executableName);
                if (File.Exists(candidate))
                    return candidate;
            }

            // 2. PATH environment variable
            if (TryFindOnPath(executableName, out string pathCandidate))
                return pathCandidate;

            return null;
        }

        private static string ResolveSdkRoot()
        {
            // 1. Unity's internal Android SDK settings (reflection)
            string unitySdk = GetUnityAndroidSdkRoot();
            if (!string.IsNullOrEmpty(unitySdk) && Directory.Exists(unitySdk))
                return unitySdk;

            // 2. ANDROID_SDK_ROOT / ANDROID_HOME environment variables
            string envSdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (!string.IsNullOrEmpty(envSdk) && Directory.Exists(envSdk))
                return envSdk;

            // 3. EditorPrefs fallback
            string prefSdk = EditorPrefs.GetString("AndroidSdkRoot");
            if (!string.IsNullOrEmpty(prefSdk) && Directory.Exists(prefSdk))
                return prefSdk;

            return null;
        }

        private static string GetUnityAndroidSdkRoot()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type settingsType = assembly.GetType(
                    "UnityEditor.Android.AndroidExternalToolsSettings");
                if (settingsType == null)
                    continue;

                PropertyInfo sdkRootProperty = settingsType.GetProperty(
                    "sdkRootPath",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (sdkRootProperty == null)
                    continue;

                return sdkRootProperty.GetValue(null) as string;
            }

            return null;
        }

        private static bool TryFindOnPath(string executableName, out string path)
        {
            string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator);

            foreach (string dir in paths)
            {
                string candidate = Path.Combine(dir.Trim(), executableName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            path = null;
            return false;
        }

        #endregion
    }
}
