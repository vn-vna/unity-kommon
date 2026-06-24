using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.ROAP.Editor;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor
{
    public sealed class DirectCmdForwardingWindow : EditorWindow
    {
        private enum TargetMode
        {
            EditorPlayMode,
            AndroidAdb
        }

        private const string FileName = "__dcf__";
        private const string PrefTargetMode = "Scheherazade.DCF.TargetMode";
        private const string PrefPackageId = "Scheherazade.DCF.PackageId";
        private const string PrefLastCommands = "Scheherazade.DCF.LastCommands";
        private const string PrefSelectedDevice = "Scheherazade.DCF.SelectedDevice";
        private const string PrefHistory = "Scheherazade.DCF.History";
        private const int MaxHistory = 50;
        private const int DefaultAdbTimeoutMs = 15000;

        private TargetMode _targetMode;
        private string _commands = string.Empty;
        private Vector2 _commandsScrollPos;

        private List<RoapDeviceInfo> _devices = new();
        private int _selectedDeviceIndex = -1;
        private string _packageId = string.Empty;
        private bool _isSending;

        private List<string> _history = new();
        private int _selectedHistoryIndex = -1;
        private bool _historyDirty;

        private string _statusMessage = string.Empty;
        private MessageType _statusType = MessageType.Info;

        private string _adbPath;

        [MenuItem("Dev Menu/Tools/Direct Cmd Forwarding")]
        public static void ShowWindow()
        {
            GetWindow<DirectCmdForwardingWindow>("Direct Cmd Forwarding");
        }

        private void OnEnable()
        {
            _targetMode = (TargetMode)EditorPrefs.GetInt(PrefTargetMode, (int)TargetMode.EditorPlayMode);
            _packageId = EditorPrefs.GetString(PrefPackageId, PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
            _commands = EditorPrefs.GetString(PrefLastCommands, string.Empty);
            _selectedDeviceIndex = EditorPrefs.GetInt(PrefSelectedDevice, -1);

            LoadHistory();

            ResolveAdbPath();
            RefreshDevicesAsync();
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(PrefTargetMode, (int)_targetMode);
            EditorPrefs.SetString(PrefPackageId, _packageId);
            EditorPrefs.SetString(PrefLastCommands, _commands);
            EditorPrefs.SetInt(PrefSelectedDevice, _selectedDeviceIndex);

            if (_historyDirty)
                SaveHistory();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);

            DrawHeader();
            EditorGUILayout.Space(4f);
            DrawTargetSection();
            EditorGUILayout.Space(4f);

            if (_targetMode == TargetMode.AndroidAdb)
            {
                DrawAndroidSection();
                EditorGUILayout.Space(4f);
            }

            DrawCommandsSection();
            EditorGUILayout.Space(8f);
            DrawSendSection();
            EditorGUILayout.Space(4f);
            DrawStatusBox();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Direct Cmd Forwarding", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Sends debug commands via {FileName} file. One command per line.",
                EditorStyles.miniLabel);
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            TargetMode newMode = (TargetMode)EditorGUILayout.EnumPopup("Target", _targetMode);
            if (newMode != _targetMode)
            {
                _targetMode = newMode;
                _statusMessage = string.Empty;
            }

            if (_targetMode == TargetMode.EditorPlayMode)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Persistent Path", GetEditorPersistentPath());
                EditorGUI.EndDisabledGroup();

                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox(
                        "Editor is not in Play Mode. Commands sent will be consumed when Play Mode starts.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAndroidSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Android ADB", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("ADB Path", string.IsNullOrEmpty(_adbPath) ? "Not found" : _adbPath, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();

            string[] deviceNames = new string[_devices.Count];
            for (int i = 0; i < _devices.Count; i++)
                deviceNames[i] = _devices[i].DisplayName;

            if (_devices.Count == 0)
                deviceNames = new[] { "No devices found" };

            int displayIndex = _devices.Count > 0 ? _selectedDeviceIndex + 1 : 0;
            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Count)
                displayIndex = 0;

            int newDisplayIndex;
            if (_devices.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                newDisplayIndex = EditorGUILayout.Popup("Device", 0, deviceNames);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                string[] options = new string[_devices.Count + 1];
                options[0] = "-- Select device --";
                for (int i = 0; i < _devices.Count; i++)
                    options[i + 1] = _devices[i].DisplayName;

                newDisplayIndex = EditorGUILayout.Popup("Device", displayIndex, options);

                if (newDisplayIndex != displayIndex)
                    _selectedDeviceIndex = newDisplayIndex - 1;
            }

            EditorGUI.BeginDisabledGroup(_isSending);
            if (GUILayout.Button("Refresh", GUILayout.Width(64f)))
                RefreshDevicesAsync();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            string newPackageId = EditorGUILayout.TextField("Package ID", _packageId);
            if (newPackageId != _packageId)
            {
                _packageId = newPackageId;
                _selectedDeviceIndex = -1;
            }

            if (string.IsNullOrEmpty(_packageId))
            {
                EditorGUILayout.HelpBox("Package ID is required for Android target.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCommandsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Commands", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(_history.Count == 0);
            if (GUILayout.Button("Clear History", EditorStyles.miniButton, GUILayout.Width(90f)))
                ClearHistory();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Syntax: command_name --param value -p value positional_arg",
                EditorStyles.miniLabel);

            DrawHistorySelector();

            _commandsScrollPos = EditorGUILayout.BeginScrollView(
                _commandsScrollPos,
                GUILayout.MinHeight(120f),
                GUILayout.MaxHeight(240f));

            _commands = EditorGUILayout.TextArea(
                _commands,
                GUILayout.ExpandHeight(true));

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawHistorySelector()
        {
            if (_history.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();

            string[] historyOptions = new string[_history.Count + 1];
            historyOptions[0] = "-- History --";
            for (int i = 0; i < _history.Count; i++)
            {
                string entry = _history[i];
                string preview = entry.Length > 72
                    ? entry.Substring(0, 72) + "..."
                    : entry;
                historyOptions[i + 1] = preview;
            }

            int displayedIndex = _selectedHistoryIndex + 1;
            int newDisplayedIndex = EditorGUILayout.Popup("History", displayedIndex, historyOptions);

            if (newDisplayedIndex != displayedIndex)
            {
                _selectedHistoryIndex = newDisplayedIndex - 1;
                if (_selectedHistoryIndex >= 0 && _selectedHistoryIndex < _history.Count)
                {
                    _commands = _history[_selectedHistoryIndex];
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSendSection()
        {
            bool canSend = !_isSending;
            if (_targetMode == TargetMode.AndroidAdb)
                canSend = canSend && !string.IsNullOrEmpty(_packageId) && _selectedDeviceIndex >= 0;

            EditorGUI.BeginDisabledGroup(!canSend);

            string buttonLabel = _isSending ? "Sending..." : "Send Commands";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(32f)))
                SendCommands();

            EditorGUI.EndDisabledGroup();
        }

        private void DrawStatusBox()
        {
            if (string.IsNullOrEmpty(_statusMessage))
                return;

            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        private async void SendCommands()
        {
            if (_isSending)
                return;

            string commands = _commands?.Trim();
            if (string.IsNullOrEmpty(commands))
            {
                _statusMessage = "No commands to send.";
                _statusType = MessageType.Warning;
                return;
            }

            _isSending = true;
            _statusMessage = "Sending commands...";
            _statusType = MessageType.Info;
            Repaint();

            try
            {
                bool success = _targetMode == TargetMode.EditorPlayMode
                    ? SendToEditorPlayMode(commands)
                    : await SendToDeviceAsync(commands);

                if (success)
                {
                    _statusMessage = $"Commands sent successfully at {DateTime.Now:T}.";
                    _statusType = MessageType.Info;
                    EditorPrefs.SetString(PrefLastCommands, _commands);
                    AddToHistory(commands);
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Failed to send commands: {ex.Message}";
                _statusType = MessageType.Error;
            }
            finally
            {
                _isSending = false;
                Repaint();
            }
        }

        private bool SendToEditorPlayMode(string commands)
        {
            string filePath = GetEditorPersistentPath();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, commands, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _statusMessage = $"Failed to write to {filePath}: {ex.Message}";
                _statusType = MessageType.Error;
                return false;
            }
        }

        private async Task<bool> SendToDeviceAsync(string commands)
        {
            RoapDeviceInfo device = _devices[_selectedDeviceIndex];
            string serial = device.Serial;

            if (string.IsNullOrEmpty(_adbPath))
            {
                _statusMessage = "ADB not found. Check Android SDK path in Unity Preferences.";
                _statusType = MessageType.Error;
                return false;
            }

            string tempFile = Path.GetTempFileName();
            string remoteDir = $"/sdcard/Android/data/{_packageId}/files";
            string remotePath = $"{remoteDir}/{FileName}";

            try
            {
                File.WriteAllText(tempFile, commands, Encoding.UTF8);

                await RunAdbAsync(serial, $"shell mkdir -p {remoteDir}");

                AdbResult result = await RunAdbAsync(serial, $"push \"{tempFile}\" \"{remotePath}\"");

                if (result.ExitCode != 0)
                {
                    string error = string.IsNullOrEmpty(result.StandardError)
                        ? result.StandardOutput
                        : result.StandardError;

                    _statusMessage = $"ADB push failed (exit code {result.ExitCode}): {error.Trim()}";
                    _statusType = MessageType.Error;
                    return false;
                }

                return true;
            }
            catch (TimeoutException)
            {
                _statusMessage = "ADB command timed out. Check device connection.";
                _statusType = MessageType.Error;
                return false;
            }
            catch (Exception ex)
            {
                _statusMessage = $"ADB error: {ex.Message}";
                _statusType = MessageType.Error;
                return false;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private async void RefreshDevicesAsync()
        {
            _isSending = true;
            _statusMessage = "Refreshing devices...";
            _statusType = MessageType.Info;
            Repaint();

            try
            {
                _devices = await Task.Run(() => RoapAdbClient.GetDevices());
                _selectedDeviceIndex = Mathf.Clamp(_selectedDeviceIndex, -1, _devices.Count - 1);

                if (_devices.Count == 0)
                {
                    _statusMessage = "No devices found via ADB.";
                    _statusType = MessageType.Warning;
                }
                else
                {
                    _statusMessage = $"Found {_devices.Count} device(s).";
                    _statusType = MessageType.Info;
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Failed to refresh devices: {ex.Message}";
                _statusType = MessageType.Error;
            }
            finally
            {
                _isSending = false;
                Repaint();
            }
        }

        private static string GetEditorPersistentPath()
        {
            return Path.Combine(Application.persistentDataPath, FileName);
        }

        private void ResolveAdbPath()
        {
            string executableName =
#if UNITY_EDITOR_WIN
                "adb.exe";
#else
                "adb";
#endif

            string sdkRoot = GetUnityAndroidSdkRoot();
            if (!string.IsNullOrWhiteSpace(sdkRoot))
            {
                string sdkCandidate = Path.Combine(sdkRoot, "platform-tools", executableName);
                if (File.Exists(sdkCandidate))
                {
                    _adbPath = sdkCandidate;
                    return;
                }
            }

            if (TryFindOnPath(executableName, out string pathCandidate))
            {
                _adbPath = pathCandidate;
                return;
            }

            _adbPath = null;
        }

        private async Task<AdbResult> RunAdbAsync(string serial, string arguments)
        {
            return await Task.Run(() => RunAdb(serial, arguments));
        }

        private AdbResult RunAdb(string serial, string arguments)
        {
            if (string.IsNullOrEmpty(_adbPath))
                throw new InvalidOperationException("ADB not found.");

            string fullArguments = $"-s {serial} {arguments}";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                Arguments = fullArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process process = new Process { StartInfo = startInfo };

            StringBuilder stdoutBuilder = new();
            StringBuilder stderrBuilder = new();

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                    stdoutBuilder.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                    stderrBuilder.AppendLine(args.Data);
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to start adb at '{_adbPath}': {ex.Message}", ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(DefaultAdbTimeoutMs))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("ADB command timed out.");
            }

            process.WaitForExit();

            return new AdbResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdoutBuilder.ToString(),
                StandardError = stderrBuilder.ToString(),
            };
        }

        private static string GetUnityAndroidSdkRoot()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type settingsType = assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
                if (settingsType == null)
                    continue;

                PropertyInfo sdkRootProperty = settingsType.GetProperty(
                    "sdkRootPath",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (sdkRootProperty == null)
                    continue;

                return sdkRootProperty.GetValue(null) as string;
            }

            return EditorPrefs.GetString("AndroidSdkRoot");
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

        private void LoadHistory()
        {
            try
            {
                string json = EditorPrefs.GetString(PrefHistory, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    HistoryData data = JsonUtility.FromJson<HistoryData>(json);
                    _history = data?.entries ?? new List<string>();
                }
            }
            catch
            {
                _history = new List<string>();
            }

            _selectedHistoryIndex = _history.Count > 0 ? 0 : -1;
        }

        private void SaveHistory()
        {
            try
            {
                HistoryData data = new HistoryData { entries = _history };
                EditorPrefs.SetString(PrefHistory, JsonUtility.ToJson(data));
                _historyDirty = false;
            }
            catch
            {
            }
        }

        private void AddToHistory(string commands)
        {
            commands = commands.Trim();
            if (string.IsNullOrEmpty(commands))
                return;

            _history.RemoveAll(e => string.Equals(e, commands, StringComparison.Ordinal));
            _history.Insert(0, commands);

            if (_history.Count > MaxHistory)
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);

            _selectedHistoryIndex = 0;
            _historyDirty = true;

            try { SaveHistory(); } catch { }
        }

        private void ClearHistory()
        {
            _history.Clear();
            _selectedHistoryIndex = -1;
            _historyDirty = true;
            EditorPrefs.DeleteKey(PrefHistory);
        }

        [Serializable]
        private sealed class HistoryData
        {
            public List<string> entries = new();
        }

        private sealed class AdbResult
        {
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; }
            public string StandardError { get; set; }
        }
    }
}
