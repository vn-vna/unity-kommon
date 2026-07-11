using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using Com.Hapiga.Scheherazade.Common.ROAP.Editor;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor
{
    public sealed class DirectCmdForwardingWindow : EditorWindow
    {
        #region Constants
        private const string FileName = "__dcf__";
        private const int MaxHistory = 50;
        private const int DefaultAdbTimeoutMs = 15000;
        private const int MaxDetectedApps = 200;
        #endregion

        #region Enums & Nested Types
        private enum TargetMode
        {
            EditorPlayMode,
            AndroidAdb
        }

        private enum AppDetectionState
        {
            Idle,
            Detecting,
            Detected,
            Error
        }

        [Serializable]
        private sealed class DetectedAppInfo
        {
            public string packageId;
            public string dataPath;
            public bool dataPathExists;
            public string displayLabel;
        }

        [Serializable]
        private sealed class DetectionCacheData
        {
            public string deviceSerial;
            public string externalStoragePath;
            public List<string> detectedPackageIds = new();
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
        #endregion

        #region Serialized & Private Fields
        private static readonly EditorPrefsStore Prefs = new EditorPrefsStore("Scheherazade.DCF");

        private TargetMode _targetMode;
        private string _commands = string.Empty;
        private Vector2 _commandsScrollPos;
        private Vector2 _historyScrollPos;

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

        // Auto-detection fields
        private AppDetectionState _detectionState = AppDetectionState.Idle;
        private List<DetectedAppInfo> _detectedApps = new();
        private string _detectedExternalStoragePath;
        private bool _showHistoryPanel;

        // Per-session external storage cache (device serial -> path)
        private static readonly Dictionary<string, string> s_externalStorageCache = new();
        #endregion

        #region Unity Callbacks
        [MenuItem("Dev Menu/Tools/Direct Cmd Forwarding")]
        public static void ShowWindow()
        {
            GetWindow<DirectCmdForwardingWindow>("Direct Cmd Forwarding");
        }

private void OnEnable()
        {
            _targetMode = (TargetMode)Prefs.GetInt("TargetMode", (int)TargetMode.EditorPlayMode);
            _packageId = Prefs.Get("PackageId", PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
            _commands = Prefs.Get("LastCommands", string.Empty);
            _selectedDeviceIndex = Prefs.GetInt("SelectedDevice", -1);

            LoadHistory();
            LoadDetectionCache();

            ResolveAdbPath();
            RefreshDevicesAsync();
        }

private void OnDisable()
        {
            Prefs.SetInt("TargetMode", (int)_targetMode);
            Prefs.Set("PackageId", _packageId);
            Prefs.Set("LastCommands", _commands);
            Prefs.SetInt("SelectedDevice", _selectedDeviceIndex);

            if (_historyDirty)
                SaveHistory();

            PersistDetectionCache();
        }

        private void OnGUI()
        {
            DrawModernHeader();
            EditorGUILayout.Space(4f);
            DrawTargetSelector();
            EditorGUILayout.Space(6f);

            if (_targetMode == TargetMode.AndroidAdb)
            {
                DrawAndroidDevicePanel();
                EditorGUILayout.Space(6f);
            }

            DrawCommandEditor();

            if (_showHistoryPanel && _history.Count > 0)
            {
                EditorGUILayout.Space(4f);
                DrawHistoryPanel();
            }

            EditorGUILayout.Space(8f);
            DrawActionBar();
        }
        #endregion

        #region Public Methods
        #endregion

        #region UI - Header & Target
        private void DrawModernHeader()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(24f));

            GUIContent icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
            GUILayout.Label(icon, GUILayout.Width(22f), GUILayout.Height(22f));

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Direct Cmd Forwarding", EditorGuiStyles.HeaderTitle);
            EditorGUILayout.LabelField("Scheherazade debugging tool", EditorGuiStyles.HeaderSubtitle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            string badgeText = _targetMode == TargetMode.EditorPlayMode
                ? "\u25CF  Editor"
                : "\u25CF  Android ADB";
            Color badgeColor = _targetMode == TargetMode.EditorPlayMode
                ? new Color(0.3f, 0.7f, 0.3f)
                : new Color(0.3f, 0.5f, 0.9f);

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = badgeColor;
            EditorGUILayout.LabelField(badgeText, EditorGuiStyles.Badge, GUILayout.Width(90f));
            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();
        }

private void DrawTargetSelector()
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Target Mode", EditorGuiStyles.SectionHeader);

            EditorGUILayout.BeginHorizontal();

            bool isEditor = _targetMode == TargetMode.EditorPlayMode;
            Color oldBg = GUI.backgroundColor;
            if (isEditor)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f, 0.7f);

            if (GUILayout.Toggle(
                    isEditor,
                    "  \u25B6  Editor Play Mode",
                    isEditor ? EditorGuiStyles.ModeButtonActive : EditorGuiStyles.ModeButtonInactive,
                    GUILayout.Height(28f))
                && !isEditor)
            {
                _targetMode = TargetMode.EditorPlayMode;
                _statusMessage = string.Empty;
            }

            GUI.backgroundColor = oldBg;

            bool isAdb = _targetMode == TargetMode.AndroidAdb;
            oldBg = GUI.backgroundColor;
            if (isAdb)
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f, 0.7f);

            if (GUILayout.Toggle(
                    isAdb,
                    "  \u25C9  Android ADB",
                    isAdb ? EditorGuiStyles.ModeButtonActive : EditorGuiStyles.ModeButtonInactive,
                    GUILayout.Height(28f))
                && !isAdb)
            {
                _targetMode = TargetMode.AndroidAdb;
                _statusMessage = string.Empty;
            }

            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();

            if (_targetMode == TargetMode.EditorPlayMode)
            {
                EditorGUILayout.Space(4f);
                DrawInlinePathField("Persistent Path", GetEditorPersistentPath());

                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox(
                        "\u26A0  Editor is not in Play Mode. Commands will be consumed on next Play.",
                        MessageType.Warning);
                }
                else
                {
                    DrawInlineStatusIcon(true, "Play Mode active \u2014 commands consumed in real-time.");
                }
            }
            else if (_targetMode == TargetMode.AndroidAdb
                && !string.IsNullOrEmpty(_packageId)
                && _selectedDeviceIndex >= 0)
            {
                EditorGUILayout.Space(4f);
                string deviceSerial = _devices[_selectedDeviceIndex].Serial;
                string storage = GetExternalStorageForDevice(deviceSerial);
                string resolvedPath = BuildAppDataPath(storage, _packageId);
                DrawInlinePathField("Persistent Path", resolvedPath);

                if (_selectedDeviceIndex < 0)
                {
                    EditorGUILayout.HelpBox(
                        "\u26A0  No device selected. Commands cannot be sent.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region UI - Android Device Panel
private void DrawAndroidDevicePanel()
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);

            DrawAndroidPanelHeader();
            DrawDeviceSelector();
            DrawPackageIdRow();
            DrawResolvedPath();

            EditorGUILayout.EndVertical();
        }

        private void DrawAndroidPanelHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Android Device", EditorGuiStyles.SectionHeader);
            GUILayout.FlexibleSpace();

            bool adbAvailable = !string.IsNullOrEmpty(_adbPath);
            DrawInlineStatusIcon(adbAvailable,
                adbAvailable ? "ADB ready" : "ADB not found");

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f);
            EditorGUI.BeginDisabledGroup(_isSending);
            if (GUILayout.Button(
                    "\u21BB  Refresh",
                    EditorStyles.miniButton,
                    GUILayout.Width(80f)))
            {
                RefreshDevicesAsync();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();
        }

private void DrawDeviceSelector()
        {
            EditorGUILayout.BeginHorizontal();

            if (_devices.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Popup(0, new[] { "No devices found" });
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                string[] options = new string[_devices.Count + 1];
                options[0] = "-- Select device --";
                for (int i = 0; i < _devices.Count; i++)
                    options[i + 1] = _devices[i].DisplayName;

                int displayIndex = Mathf.Clamp(_selectedDeviceIndex + 1, 0, options.Length - 1);
                int newDisplay = EditorGUILayout.Popup(displayIndex, options);

                if (newDisplay != displayIndex)
                {
                    _selectedDeviceIndex = newDisplay - 1;
                    _detectionState = AppDetectionState.Idle;
                }
            }

            if (_devices.Count > 0)
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.6f);
                EditorGUILayout.LabelField(
                    _devices.Count.ToString(),
                    EditorGuiStyles.Badge,
                    GUILayout.Width(22f));
                GUI.backgroundColor = oldBg;
            }

            bool canExport = !string.IsNullOrEmpty(_packageId) && _selectedDeviceIndex >= 0;
            EditorGUI.BeginDisabledGroup(!canExport);
            Color oldBg2 = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
            if (GUILayout.Button(
                    "Export...",
                    EditorStyles.miniButton,
                    GUILayout.Width(60f)))
            {
                RoapDeviceInfo device = _devices[_selectedDeviceIndex];
                string storage = GetExternalStorageForDevice(device.Serial);
                string remoteDir = BuildAppDataPath(storage, _packageId);
                AdbCommandExportWindow.Show(device.Serial, _packageId, _commands, remoteDir);
            }
            GUI.backgroundColor = oldBg2;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

private void DrawPackageIdRow()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Package ID", GUILayout.Width(80f));

            string newPkg = EditorGUILayout.TextField(_packageId);
            if (newPkg != _packageId)
            {
                _packageId = newPkg;
                _detectionState = AppDetectionState.Idle;
            }

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f);
            bool canDetect = _detectionState != AppDetectionState.Detecting
                && _selectedDeviceIndex >= 0
                && !string.IsNullOrEmpty(_packageId);
            EditorGUI.BeginDisabledGroup(!canDetect);
            if (GUILayout.Button(
                    _detectionState == AppDetectionState.Detecting
                        ? "\u23F3 Detecting..."
                        : "\uD83D\uDD0D Auto Detect",
                    EditorStyles.miniButton,
                    GUILayout.Width(110f)))
            {
                DetectAppsAsync();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();
        }





        private void DrawResolvedPath()
        {
            if (string.IsNullOrEmpty(_packageId) || _selectedDeviceIndex < 0)
                return;

            EditorGUILayout.Space(4f);
            string deviceSerial = _devices[_selectedDeviceIndex].Serial;
            string storage = GetExternalStorageForDevice(deviceSerial);
            string resolvedPath = BuildAppDataPath(storage, _packageId);

            DrawInlinePathField("Remote Path", resolvedPath);
        }
        #endregion

        #region UI - Command Editor
        private void DrawCommandEditor()
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);

            DrawCommandEditorHeader();
            EditorGUILayout.LabelField(
                "Format:  command_name --param value -p value positional_arg  (one per line)",
                EditorGuiStyles.InlineStatus);



            _commandsScrollPos = EditorGUILayout.BeginScrollView(
                _commandsScrollPos,
                GUILayout.MinHeight(140f),
                GUILayout.MaxHeight(260f));

            _commands = EditorGUILayout.TextArea(
                _commands,
                EditorGuiStyles.CommandArea,
                GUILayout.ExpandHeight(true));

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawCommandEditorHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Commands", EditorGuiStyles.SectionHeader);
            GUILayout.FlexibleSpace();

            int lineCount = string.IsNullOrEmpty(_commands)
                ? 0
                : _commands.Split(new[] { '\n' }, StringSplitOptions.None).Length;

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 0.35f, 0.35f);
            EditorGUILayout.LabelField(
                $"{lineCount} line{(lineCount != 1 ? "s" : "")}",
                EditorGuiStyles.Badge,
                GUILayout.Width(56f));
            GUI.backgroundColor = oldBg;

            EditorGUI.BeginDisabledGroup(_history.Count == 0);
            oldBg = GUI.backgroundColor;
            GUI.backgroundColor = _showHistoryPanel
                ? new Color(0.3f, 0.55f, 0.9f)
                : new Color(0.4f, 0.4f, 0.4f);
            if (GUILayout.Button(
                    "\u2630 History",
                    EditorStyles.miniButton,
                    GUILayout.Width(70f)))
            {
                _showHistoryPanel = !_showHistoryPanel;
            }
            GUI.backgroundColor = oldBg;

            oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 0.3f, 0.3f);
            if (GUILayout.Button(
                    "\u2715 Clear",
                    EditorStyles.miniButton,
                    GUILayout.Width(60f)))
            {
                ClearHistory();
            }
            GUI.backgroundColor = oldBg;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawHistoryPanel()
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("History", EditorGuiStyles.SectionHeader);

            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
            if (GUILayout.Button("Hide", EditorStyles.miniButton, GUILayout.Width(50f)))
                _showHistoryPanel = false;
            GUI.backgroundColor = bg;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);

            _historyScrollPos = EditorGUILayout.BeginScrollView(
                _historyScrollPos,
                GUILayout.MinHeight(80f),
                GUILayout.MaxHeight(300f));

            for (int i = 0; i < _history.Count; i++)
                DrawHistoryEntry(i, _history[i]);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawHistoryEntry(int index, string entry)
        {
            string preview = TruncateToLines(entry, 3, 72);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(
                preview,
                EditorGuiStyles.HistoryEntry,
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndVertical();

            if (GUILayout.Button(
                    "\u21A9",
                    EditorStyles.miniButton,
                    GUILayout.Width(26f),
                    GUILayout.Height(26f)))
            {
                _commands = entry;
                GUI.FocusControl(null);
            }

            if (GUILayout.Button(
                    "+",
                    EditorStyles.miniButton,
                    GUILayout.Width(22f),
                    GUILayout.Height(26f)))
            {
                if (string.IsNullOrEmpty(_commands))
                    _commands = entry;
                else
                    _commands = _commands.TrimEnd() + "\n" + entry;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string TruncateToLines(string text, int maxLines, int maxLineWidth)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string[] lines = text.Split(
                new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            int count = 0;

            foreach (string line in lines)
            {
                if (count >= maxLines)
                {
                    sb.Append("...");
                    break;
                }

                string trimmed = line.TrimEnd();
                if (count > 0)
                    sb.AppendLine();

                if (trimmed.Length > maxLineWidth)
                    sb.Append(trimmed.Substring(0, maxLineWidth - 3) + "...");
                else
                    sb.Append(trimmed);

                count++;
            }

            return sb.ToString();
        }
        #endregion

        #region UI - Action Bar & Helpers
        private void DrawActionBar()
        {
            bool canSend = !_isSending;
            if (_targetMode == TargetMode.AndroidAdb)
            {
                canSend = canSend
                    && !string.IsNullOrEmpty(_packageId)
                    && _selectedDeviceIndex >= 0;
            }

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);

            EditorGUI.BeginDisabledGroup(!canSend);
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f, 0.65f, 0.25f);

            string buttonLabel = _isSending
                ? "\u23F3 Sending..."
                : "\u25B6  Send Commands";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(40f)))
                SendCommands();

            GUI.backgroundColor = oldBg;
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                Color statusColor = _statusType switch
                {
                    MessageType.Error => new Color(0.95f, 0.4f, 0.4f),
                    MessageType.Warning => new Color(0.95f, 0.85f, 0.3f),
                    _ => new Color(0.5f, 0.85f, 0.5f)
                };

                GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor },
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    padding = new RectOffset(8, 8, 4, 4)
                };
                EditorGUILayout.LabelField(_statusMessage, statusStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawInlinePathField(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(80f));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(path);
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(
                    "\uD83D\uDCCB",
                    EditorStyles.miniButton,
                    GUILayout.Width(26f)))
            {
                GUIUtility.systemCopyBuffer = path;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawInlineStatusIcon(bool isOk, string message)
        {
            string symbol = isOk ? "\u25CF" : "\u25CB";
            Color color = isOk
                ? new Color(0.3f, 0.8f, 0.3f)
                : new Color(0.8f, 0.4f, 0.3f);

            Color oldColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField(
                $"{symbol}  {message}",
                EditorGuiStyles.InlineStatus);
            GUI.color = oldColor;
        }
        #endregion

        #region Command Send Pipeline
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
                    Prefs.Set("LastCommands", _commands);
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

            string remoteDir = ResolveRemoteDirectory(serial);
            string remotePath = $"{remoteDir}/{FileName}";
            string tempFile = Path.GetTempFileName();

            try
            {
                File.WriteAllText(tempFile, commands, Encoding.UTF8);

                await RunAdbAsync(serial, $"shell mkdir -p {remoteDir}");

                AdbResult result = await RunAdbAsync(
                    serial,
                    $"push \"{tempFile}\" \"{remotePath}\"");

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

        /// <summary>
        /// Resolves the remote data directory, preferring the auto-detected
        /// external storage path. Falls back to /sdcard if unavailable.
        /// </summary>
        private string ResolveRemoteDirectory(string serial)
        {
            string storage = GetExternalStorageForDevice(serial);
            return BuildAppDataPath(storage, _packageId);
        }

        private string GetExternalStorageForDevice(string serial)
        {
            if (s_externalStorageCache.TryGetValue(serial, out string cached))
                return cached;

            return _detectedExternalStoragePath ?? "/sdcard";
        }
        #endregion

        #region Auto-Detection Pipeline
private async void DetectAppsAsync()
        {
            if (_detectionState == AppDetectionState.Detecting)
                return;

            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Count)
                return;

            RoapDeviceInfo device = _devices[_selectedDeviceIndex];
            _detectionState = AppDetectionState.Detecting;
            _detectedApps.Clear();
            _statusMessage = "Detecting...";
            _statusType = MessageType.Info;
            Repaint();

            try
            {
                string externalStorage = await DetectExternalStorageAsync(device.Serial);
                if (string.IsNullOrEmpty(externalStorage))
                {
                    _detectionState = AppDetectionState.Error;
                    _statusMessage = "Failed to detect external storage path.";
                    _statusType = MessageType.Error;
                    Repaint();
                    return;
                }

                string dataPath = BuildAppDataPath(externalStorage, _packageId);

                _detectedApps.Add(new DetectedAppInfo
                {
                    packageId = _packageId,
                    dataPath = dataPath,
                    dataPathExists = false,
                    displayLabel = $"{_packageId}  \u279C  {dataPath}"
                });

                _detectionState = AppDetectionState.Detected;
                _statusMessage = $"Resolved: {dataPath}";
                _statusType = MessageType.Info;

                PersistDetectionCache();
            }
            catch (TimeoutException)
            {
                _detectionState = AppDetectionState.Error;
                _statusMessage = "ADB command timed out. Check device connection.";
                _statusType = MessageType.Error;
            }
            catch (Exception ex)
            {
                _detectionState = AppDetectionState.Error;
                _statusMessage = $"Detection failed: {ex.Message}";
                _statusType = MessageType.Error;
            }
            finally
            {
                Repaint();
            }
        }

        private async Task<string> DetectExternalStorageAsync(string serial)
        {
            if (s_externalStorageCache.TryGetValue(serial, out string cached))
                return cached;

            AdbResult result = await RunAdbAsync(
                serial,
                "shell \"echo $EXTERNAL_STORAGE\"");

            if (result.ExitCode != 0)
                return string.Empty;

            string path = result.StandardOutput?.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                s_externalStorageCache[serial] = path;
                _detectedExternalStoragePath = path;
            }
            return path ?? string.Empty;
        }

        private static string BuildAppDataPath(string externalStorage, string packageId)
        {
            string root = (externalStorage ?? "/sdcard").TrimEnd('/');
            return $"{root}/Android/data/{packageId}/files";
        }
        #endregion

        #region Detection Persistence
        private void PersistDetectionCache()
        {
            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Count)
                return;

            string serial = _devices[_selectedDeviceIndex].Serial;
            if (!s_externalStorageCache.TryGetValue(serial, out string storage))
                return;

            try
            {
                DetectionCacheData cache = new DetectionCacheData
                {
                    deviceSerial = serial,
                    externalStoragePath = storage,
                    detectedPackageIds = new List<string>()
                };

                foreach (DetectedAppInfo app in _detectedApps)
                    cache.detectedPackageIds.Add(app.packageId);

                Prefs.SetJson("ExternalStorageCache", cache);
            }
            catch
            {
                // Non-critical persistence; suppress errors.
            }
        }

        private void LoadDetectionCache()
        {
            try
            {
                DetectionCacheData cache = Prefs.GetJson<DetectionCacheData>("ExternalStorageCache");
                if (cache == null || string.IsNullOrEmpty(cache.externalStoragePath))
                    return;

                s_externalStorageCache[cache.deviceSerial] = cache.externalStoragePath;
                _detectedExternalStoragePath = cache.externalStoragePath;
            }
            catch
            {
                // Non-critical load; use defaults.
            }
        }
        #endregion

        #region Device & ADB
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
                string sdkCandidate = Path.Combine(
                    sdkRoot, "platform-tools", executableName);
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
        #endregion

        #region History Management
        private void LoadHistory()
        {
            try
            {
                HistoryData data = Prefs.GetJson<HistoryData>("History");
                _history = data?.entries ?? new List<string>();
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
                Prefs.SetJson("History", data);
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

            _history.RemoveAll(
                e => string.Equals(e, commands, StringComparison.Ordinal));
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
            Prefs.Delete("History");
        }
        #endregion

        #region Nested Types - AdbCommandExportWindow
        private sealed class AdbCommandExportWindow : EditorWindow
        {
            private string _fullContent = string.Empty;
            private string _rawCommand = string.Empty;
            private string _fileContent = string.Empty;
            private Vector2 _scrollPos;
            private string _copyStatus = string.Empty;

            public static void Show(
                string deviceSerial,
                string packageId,
                string commands,
                string remoteDir)
            {
                AdbCommandExportWindow window = GetWindow<AdbCommandExportWindow>(
                    true, "Export ADB Command");
                window.minSize = new Vector2(420f, 320f);
                window.maxSize = new Vector2(700f, 600f);

                string remotePath = $"{remoteDir}/{FileName}";

                string fileContent = string.IsNullOrWhiteSpace(commands)
                    ? "# (empty)"
                    : commands.Trim();

                string base64Content = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(fileContent));

                window._rawCommand =
                    $"adb -s {deviceSerial} shell \"mkdir -p {remoteDir} && echo '{base64Content}' | base64 -d > {remotePath}\"";

                StringBuilder fullContentSb = new();
                fullContentSb.AppendLine("=== Executable Command ===");
                fullContentSb.Append(window._rawCommand);
                fullContentSb.AppendLine();
                fullContentSb.AppendLine();
                fullContentSb.AppendLine("=== Embedded File Content ===");
                fullContentSb.Append(fileContent);

                window._fullContent = fullContentSb.ToString();
                window._fileContent = fileContent;
            }

            private void OnGUI()
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    "ADB Command Export",
                    EditorStyles.boldLabel);

                EditorGUILayout.Space(2f);

                EditorGUILayout.LabelField(
                    "Copy and paste the command below into your terminal \u2014 it executes immediately.",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4f);

                _scrollPos = EditorGUILayout.BeginScrollView(
                    _scrollPos,
                    GUILayout.ExpandHeight(true));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextArea(
                    _fullContent,
                    GUILayout.ExpandHeight(true));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(4f);

                if (GUILayout.Button("Copy Command", GUILayout.Height(32f)))
                {
                    GUIUtility.systemCopyBuffer = _rawCommand;
                    _copyStatus = "Command copied \u2014 paste in terminal and press Enter.";
                }

                EditorGUILayout.Space(2f);

                if (GUILayout.Button(
                        "Copy File Content",
                        EditorStyles.miniButton))
                {
                    GUIUtility.systemCopyBuffer = _fileContent;
                    _copyStatus = "File content copied to clipboard.";
                }

                if (!string.IsNullOrEmpty(_copyStatus))
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(_copyStatus, MessageType.Info);
                }
            }
        }
        #endregion
    }
}
