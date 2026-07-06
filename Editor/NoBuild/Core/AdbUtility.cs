using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    internal static class AdbUtility
    {
        private const string Fallback = "unknown";
        private const int Timeout = 10000;
        private static bool _checked;
        private static string _adbPath;

        public static string AdbPath
        {
            get
            {
                if (!_checked) { _checked = true; _adbPath = FindAdb(); }
                return _adbPath;
            }
        }

        public static List<AdbDeviceInfo> GetDevices()
        {
            var devices = new List<AdbDeviceInfo>();
            if (string.IsNullOrEmpty(AdbPath)) return devices;
            string output = RunAdb("devices");
            if (string.IsNullOrEmpty(output)) return devices;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2 && parts[1] == "device")
                    devices.Add(new AdbDeviceInfo { Serial = parts[0], State = parts[1] });
            }

            // Fill in model names
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                d.Model = GetDeviceProperty(d.Serial, "ro.product.model");
                devices[i] = d;
            }
            return devices;
        }

        public static bool InstallApk(string apkPath, string deviceSerial)
        {
            if (!File.Exists(apkPath)) { Debug.LogError("APK not found: " + apkPath); return false; }
            string output = RunAdb($"-s {deviceSerial} install -r \"{apkPath}\"");
            return output != null && output.Contains("Success");
        }

        public static bool LaunchApp(string deviceSerial, string packageName)
        {
            string output = RunAdb($"-s {deviceSerial} shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1");
            return output != null && !output.Contains("Error");
        }

        public static string GetPackageName() =>
            PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);

        private static string GetDeviceProperty(string serial, string prop)
        {
            string output = RunAdb($"-s {serial} shell getprop {prop}");
            return string.IsNullOrEmpty(output) ? serial : output.Trim();
        }

        private static string RunAdb(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = AdbPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                p.WaitForExit(Timeout);
                return p.StandardOutput.ReadToEnd();
            }
            catch { return null; }
        }

        private static string FindAdb()
        {
            var paths = new[]
            {
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"),
            };
            foreach (var p in paths) if (File.Exists(p)) return p;
            // Try ANDROID_SDK_ROOT env
            var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                var p = Path.Combine(sdkRoot, "platform-tools/adb");
                if (File.Exists(p)) return p;
                p += ".exe"; if (File.Exists(p)) return p;
            }

            // fallback: plain "adb" on PATH
            return "adb";
        }
    }

    public struct AdbDeviceInfo
    {
        public string Serial;
        public string Model;
        public string State;
        public string DisplayName =>
            string.IsNullOrEmpty(Model) || Model == Serial ? Serial : $"{Model} ({Serial})";
    }
}
