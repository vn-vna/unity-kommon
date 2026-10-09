using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
        private const int InstallTimeout = 180000;
        private static bool _checked;
        private static string _adbPath;
        private static bool _availabilityChecked;
        private static bool _isAvailable;

        public static string CustomAdbPath
        {
            get => EditorPrefs.GetString("NoBuild_AdbPath", "");
            set
            {
                EditorPrefs.SetString("NoBuild_AdbPath", value ?? "");
                RefreshAvailability();
            }
        }

        public static Task<List<AdbDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            string adb = AdbPath;
            return Task.Run(() =>
            {
                ProcessExecutionResult result = NoBuildProcessRunner.Run(adb, "devices -l", Timeout, cancellationToken: cancellationToken);
                if (result.TimedOut || result.ExitCode != 0)
                    throw new InvalidOperationException("ADB device refresh failed: " + result.CombinedOutput);
                return ParseDevices(result.StandardOutput);
            }, cancellationToken);
        }

        internal static List<AdbDeviceInfo> ParseDevices(string output)
        {
            var devices = new List<AdbDeviceInfo>();
            foreach (string line in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || fields[0] == "List" || fields[0].StartsWith("*")) continue;
                string model = fields.FirstOrDefault(field => field.StartsWith("model:", StringComparison.Ordinal));
                devices.Add(new AdbDeviceInfo
                {
                    Serial = fields[0], State = fields[1],
                    Model = model == null ? fields[0] : model.Substring(6).Replace('_', ' ')
                });
            }
            return devices;
        }

        public static Task<ProcessExecutionResult> RunAsync(string args, int timeoutMs = Timeout,
            CancellationToken cancellationToken = default)
        {
            return NoBuildProcessRunner.RunAsync(AdbPath, args, timeoutMs, cancellationToken: cancellationToken);
        }

        public static async Task<InstallResult> InstallApkAsync(string path, string serial, CancellationToken cancellationToken)
        {
            ProcessExecutionResult result = await RunAsync($"-s \"{serial}\" install -r \"{path}\"", InstallTimeout, cancellationToken);
            return ToInstallResult(result);
        }

        internal static InstallResult ToInstallResult(ProcessExecutionResult result)
        {
            bool success = !result.TimedOut && result.ExitCode == 0 && !result.CombinedOutput.Contains("Failure [");
            return new InstallResult
            {
                Success = success,
                FailureKind = success ? InstallFailureKind.None : DeviceInstaller.ClassifyInstallFailure(result.CombinedOutput),
                Output = result.TimedOut ? "Installation timed out. Check the device connection and retry." : result.CombinedOutput
            };
        }

        public static Task<string> GetApkPackageAsync(string path, CancellationToken cancellationToken)
        {
            string tool = FindAapt();
            return ReadApkPackageAsync(tool, path, cancellationToken);
        }

        internal static bool IsValidPackageName(string package) => !string.IsNullOrWhiteSpace(package)
            && Regex.IsMatch(package, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$");

        private static async Task<string> ReadApkPackageAsync(string tool, string path, CancellationToken cancellationToken)
        {
            ProcessExecutionResult result = await NoBuildProcessRunner.RunAsync(tool,
                $"dump badging \"{path}\"", Timeout, cancellationToken: cancellationToken);
            Match match = Regex.Match(result.StandardOutput, @"package:\s+name='([^']+)'");
            if (result.TimedOut || result.ExitCode != 0 || !match.Success || !IsValidPackageName(match.Groups[1].Value))
                throw new InvalidOperationException("Cannot read APK package. Check the Android SDK build-tools path. " + result.StandardError);
            return match.Groups[1].Value;
        }

        private static string FindAapt()
        {
            string adbDirectory = Path.IsPathRooted(AdbPath) ? Path.GetDirectoryName(AdbPath) : null;
            string[] roots =
            {
                string.IsNullOrEmpty(adbDirectory) ? null : Path.GetDirectoryName(adbDirectory),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK"),
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string tools = Path.Combine(root, "build-tools");
                if (!Directory.Exists(tools)) continue;
                foreach (string folder in Directory.GetDirectories(tools).OrderByDescending(folder =>
                             Version.TryParse(Path.GetFileName(folder), out Version version) ? version : new Version(0, 0)))
                {
                    foreach (string executable in new[] { "aapt.exe", "aapt", "aapt2.exe", "aapt2" })
                    {
                        string candidate = Path.Combine(folder, executable);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            return "aapt";
        }

        public static async Task<List<WirelessDeviceInfo>> ScanWirelessDevicesAsync(CancellationToken cancellationToken)
        {
            ProcessExecutionResult result = await RunAsync("mdns services", 8000, cancellationToken);
            if (result.TimedOut || result.ExitCode != 0)
                throw new InvalidOperationException("Wireless discovery failed: " + result.CombinedOutput);
            var devices = new Dictionary<string, WirelessDeviceInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool connect = line.Contains("_adb-tls-connect._tcp");
                bool pair = line.Contains("_adb-tls-pairing._tcp");
                if ((!connect && !pair) || !TryParseWirelessService(line, out string serial, out string ip, out int port)) continue;
                devices.TryGetValue(serial, out WirelessDeviceInfo device);
                device.Serial = serial; device.IpAddress = ip;
                if (connect)
                {
                    device.Port = port;
                    device.IsConnected = IsWirelessConnected(device, AdbDeviceCache.Devices);
                }
                else device.PairingPort = port;
                devices[serial] = device;
            }
            return devices.Values.OrderBy(device => device.Serial).ToList();
        }

        internal static bool IsWirelessConnected(WirelessDeviceInfo wireless, IEnumerable<AdbDeviceInfo> devices) =>
            devices.Any(device => device.State == "device" && (device.Serial == wireless.Serial
                || device.Serial == $"{wireless.IpAddress}:{wireless.Port}"
                || (device.Serial?.StartsWith("adb-" + wireless.Serial + "-", StringComparison.Ordinal) == true)));

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

            string output = RunAdb($"-s {deviceSerial} install -r \"{apkPath}\"", InstallTimeout);
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
            if (!string.IsNullOrWhiteSpace(CustomAdbPath)) return CustomAdbPath;
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
