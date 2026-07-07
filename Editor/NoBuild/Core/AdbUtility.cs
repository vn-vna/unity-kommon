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

        // ══════════════════════════════════════════════════
        // ── Wireless ADB
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Discovers wireless ADB devices via mDNS.
        /// </summary>
        public static List<WirelessDeviceInfo> ScanWirelessDevices()
        {
            var result = new List<WirelessDeviceInfo>();
            if (string.IsNullOrEmpty(AdbPath)) return result;

            string output = RunAdb("mdns services",
                timeoutMs: 8000);
            if (string.IsNullOrEmpty(output)) return result;

            var lines = output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.Contains("_adb-tls-connect._tcp"))
                    continue;

                var parts = line.Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                // Extract serial from instance name:
                // "adb-<serial>-<random>" → <serial> is
                // everything after "adb-" and before last "-"
                string instance = parts[0];
                string serial = "";
                if (instance.StartsWith("adb-"))
                {
                    int lastDash = instance.LastIndexOf('-');
                    if (lastDash > 4)
                        serial = instance.Substring(
                            4, lastDash - 4);
                    else
                        serial = instance.Substring(4);
                }

                // Parse ip:port
                string[] ipp = parts[parts.Length - 1]
                    .Split(':');
                if (ipp.Length != 2) continue;
                if (!int.TryParse(ipp[1],
                        out int port)) continue;

                // Check if already connected
                bool isConnected = IsDeviceConnected(
                    serial);

                result.Add(new WirelessDeviceInfo
                {
                    Serial = serial,
                    IpAddress = ipp[0],
                    Port = port,
                    IsConnected = isConnected
                });
            }

            return result;
        }

        /// <summary>
        /// Connect to a wireless ADB device.
        /// </summary>
        public static bool ConnectWireless(
            string ip, int port)
        {
            string output = RunAdb(
                $"connect {ip}:{port}");
            if (output == null) return false;
            return output.Contains("connected")
                || output.Contains("already");
        }

        /// <summary>
        /// Disconnect a wireless ADB device.
        /// </summary>
        public static bool DisconnectWireless(
            string ip, int port)
        {
            string output = RunAdb(
                $"disconnect {ip}:{port}");
            if (output == null) return false;
            return output.Contains("disconnected")
                || output.Contains("no such device");
        }

        /// <summary>
        /// Pair with a wireless ADB device
        /// (Android 11+ wireless debugging).
        /// </summary>
        public static bool PairDevice(
            string ip, int port, string pairingCode)
        {
            string output = RunAdb(
                $"pair {ip}:{port} {pairingCode}");
            if (output == null) return false;
            return output.Contains("Successfully");
        }

        private static bool IsDeviceConnected(
            string serial)
        {
            var devices = GetDevices();
            foreach (var d in devices)
            {
                if (d.Serial == serial) return true;
            }
            return false;
        }

        // ══════════════════════════════════════════════════
        // ── Internal
        // ══════════════════════════════════════════════════

        private static string GetDeviceProperty(string serial, string prop)
        {
            string output = RunAdb($"-s {serial} shell getprop {prop}");
            return string.IsNullOrEmpty(output) ? serial : output.Trim();
        }

        private static string RunAdb(string args,
            int timeoutMs = Timeout)
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
                p.WaitForExit(timeoutMs);
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

    public struct WirelessDeviceInfo
    {
        public string Serial;
        public string IpAddress;
        public int Port;
        public bool IsConnected;
        public string Endpoint => $"{IpAddress}:{Port}";
        public string DisplayName =>
            $"{Serial} ({Endpoint})";
    }
}
