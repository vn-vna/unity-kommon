using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    internal static class AdbUtility
    {
        private const string Fallback = "unknown";
        private const int Timeout = 10000;
        private const int AvailabilityTimeout = 2000;
        private static bool _checked;
        private static string _adbPath;
        private static bool _availabilityChecked;
        private static bool _isAvailable;

        public static string AdbPath
        {
            get
            {
                if (!_checked) { _checked = true; _adbPath = FindAdb(); }
                return _adbPath;
            }
        }

        public static bool IsAvailable
        {
            get
            {
                if (_availabilityChecked)
                {
                    return _isAvailable;
                }

                _availabilityChecked = true;
                if (string.IsNullOrEmpty(AdbPath))
                {
                    return false;
                }

                if (File.Exists(AdbPath))
                {
                    _isAvailable = true;
                    return true;
                }

                try
                {
                    ProcessExecutionResult result =
                        NoBuildProcessRunner.Run(
                            AdbPath,
                            "version",
                            AvailabilityTimeout);
                    _isAvailable = !result.TimedOut
                        && result.ExitCode == 0;
                }
                catch
                {
                    _isAvailable = false;
                }

                return _isAvailable;
            }
        }

        public static void RefreshAvailability()
        {
            _checked = false;
            _adbPath = null;
            _availabilityChecked = false;
            _isAvailable = false;
        }

        public static List<AdbDeviceInfo> GetDevices()
        {
            var devices = new List<AdbDeviceInfo>();
            if (!IsAvailable) return devices;
            string output = RunAdb("devices");
            if (string.IsNullOrEmpty(output)) return devices;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    devices.Add(new AdbDeviceInfo
                    {
                        Serial = parts[0],
                        State = parts[1]
                    });
                }
            }

            // Fill in model names
            for (int i = 0; i < devices.Count; i++)
            {
                AdbDeviceInfo device = devices[i];
                device.Model = device.State == "device"
                    ? GetDeviceProperty(
                        device.Serial,
                        "ro.product.model")
                    : device.State;
                devices[i] = device;
            }
            return devices;
        }

        public static InstallResult InstallApk(string apkPath, string deviceSerial)
        {
            if (!File.Exists(apkPath))
            {
                Debug.LogError("APK not found: " + apkPath);
                return new InstallResult
                {
                    Success = false,
                    FailureKind = InstallFailureKind.Other,
                    Output = "APK file not found."
                };
            }

            string output = RunAdb($"-s {deviceSerial} install -r \"{apkPath}\"");
            bool success = output != null && output.Contains("Success");
            return new InstallResult
            {
                Success = success,
                FailureKind = success
                    ? InstallFailureKind.None
                    : DeviceInstaller.ClassifyInstallFailure(output),
                Output = output ?? ""
            };
        }

        public static bool UninstallApp(string deviceSerial, string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            string output = RunAdb($"-s {deviceSerial} uninstall \"{packageName}\"");
            bool success = output != null && output.Contains("Success");
            if (!success)
            {
                Debug.LogWarning(
                    $"[NoBuild] adb uninstall of '{packageName}' "
                    + $"on {deviceSerial}: {(output ?? "no output").Trim()}");
            }
            return success;
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
            if (!IsAvailable)
            {
                return new List<WirelessDeviceInfo>();
            }

            string output = RunAdb(
                "mdns services",
                timeoutMs: 8000);
            if (string.IsNullOrEmpty(output))
            {
                return new List<WirelessDeviceInfo>();
            }

            var devices = new Dictionary<
                string,
                WirelessDeviceInfo>(
                    StringComparer.OrdinalIgnoreCase);
            string[] lines = output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                bool isConnectService = line.Contains(
                    "_adb-tls-connect._tcp");
                bool isPairingService = line.Contains(
                    "_adb-tls-pairing._tcp");
                if (!isConnectService && !isPairingService)
                {
                    continue;
                }

                if (!TryParseWirelessService(
                        line,
                        out string serial,
                        out string ipAddress,
                        out int port))
                {
                    continue;
                }

                devices.TryGetValue(
                    serial,
                    out WirelessDeviceInfo device);
                device.Serial = serial;
                device.IpAddress = ipAddress;
                if (isConnectService)
                {
                    device.Port = port;
                    device.IsConnected = IsDeviceConnected(
                        serial,
                        $"{ipAddress}:{port}");
                }
                else
                {
                    device.PairingPort = port;
                }

                devices[serial] = device;
            }

            return devices.Values
                .OrderBy(device => device.Serial)
                .ToList();
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

        private static bool TryParseWirelessService(
            string line,
            out string serial,
            out string ipAddress,
            out int port)
        {
            serial = string.Empty;
            ipAddress = string.Empty;
            port = 0;

            string[] parts = line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            string instance = parts[0];
            if (!instance.StartsWith(
                    "adb-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int lastDash = instance.LastIndexOf('-');
            serial = lastDash > 4
                ? instance.Substring(4, lastDash - 4)
                : instance.Substring(4);

            string endpoint = parts[parts.Length - 1];
            int separator = endpoint.LastIndexOf(':');
            if (separator <= 0
                || !int.TryParse(
                    endpoint.Substring(separator + 1),
                    out port))
            {
                return false;
            }

            ipAddress = endpoint.Substring(0, separator);
            return !string.IsNullOrEmpty(serial)
                && !string.IsNullOrEmpty(ipAddress);
        }

        private static bool IsDeviceConnected(
            string serial,
            string endpoint)
        {
            List<AdbDeviceInfo> devices = GetDevices();
            foreach (AdbDeviceInfo device in devices)
            {
                if (string.Equals(
                        device.Serial,
                        serial,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        device.Serial,
                        endpoint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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

        private static string RunAdb(
            string args,
            int timeoutMs = Timeout)
        {
            try
            {
                ProcessExecutionResult result =
                    NoBuildProcessRunner.Run(
                        AdbPath,
                        args,
                        timeoutMs);

                if (result.TimedOut)
                {
                    Debug.LogError(
                        $"[NoBuild] adb command timed out after "
                        + $"{timeoutMs}ms.");
                    return null;
                }

                if (result.ExitCode != 0)
                {
                    Debug.LogWarning(
                        $"[NoBuild] adb exited with code "
                        + $"{result.ExitCode}: "
                        + $"{result.CombinedOutput}");
                }

                return result.CombinedOutput;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[NoBuild] Failed to run adb: "
                    + $"{ex.Message}");
                return null;
            }
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
        public int PairingPort;
        public bool IsConnected;
        public string Endpoint => Port > 0
            ? $"{IpAddress}:{Port}"
            : $"{IpAddress} (pairing only)";
        public string DisplayName =>
            $"{Serial} ({Endpoint})";
    }
}
