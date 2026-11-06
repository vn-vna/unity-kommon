using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    [InitializeOnLoad]
    internal static class AdbDeviceCache
    {
        private static Task<List<AdbDeviceInfo>> _refresh;
        public static List<AdbDeviceInfo> Devices { get; private set; } = new();
        public static string Error { get; private set; } = "";
        public static bool IsRefreshing => _refresh != null;
        public static bool HasChecked { get; private set; }
        public static double LastRefresh { get; private set; }

        static AdbDeviceCache() => EditorApplication.update += HandleUpdate;

        public static void EnsureFresh()
        {
            if (!IsRefreshing && (!HasChecked || EditorApplication.timeSinceStartup - LastRefresh > 10)) Refresh();
        }

        public static void Refresh()
        {
            if (IsRefreshing) return;
            Error = "";
            _refresh = AdbUtility.GetDevicesAsync();
            NoBuildToolbarState.RequestRepaint();
        }

        private static void HandleUpdate()
        {
            if (_refresh == null || !_refresh.IsCompleted) return;
            try
            {
                Devices = _refresh.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Devices = new List<AdbDeviceInfo>();
                Error = exception.Message;
            }
            _refresh = null;
            HasChecked = true;
            LastRefresh = EditorApplication.timeSinceStartup;
            NoBuildToolbarState.RequestRepaint();
        }
    }
}
