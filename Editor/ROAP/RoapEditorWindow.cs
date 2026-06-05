using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ROAP.Editor
{
    public sealed class RoapEditorWindow : EditorWindow
    {
        private Vector2 _parameterScrollPosition;
        private List<RoapDeviceInfo> _devices = new List<RoapDeviceInfo>();
        private List<RoapLauncherInfo> _launchers = new List<RoapLauncherInfo>();
        private int _selectedPaletteIndex = -1;
        private bool _isRefreshingDevices;
        private bool _isRefreshingLaunchers;
        private bool _isLaunching;
        private string _paletteName = "Default";
        private string _statusMessage = "Refresh devices to begin.";
        private MessageType _statusType = MessageType.Info;

        private static RoapPaletteStore Store => RoapPaletteStore.instance;

        [MenuItem("Dev Menu/Tools/Remotely Open Android App")]
        public static void ShowWindow()
        {
            GetWindow<RoapEditorWindow>("ROAP");
        }

        private void OnEnable()
        {
            Store.EnsureInitialized(RoapAdbClient.GetDefaultPackageId());
            EnsureParameterListExists();
            _selectedPaletteIndex = Store.Palettes.Count > 0 ? 0 : -1;
            _paletteName = _selectedPaletteIndex >= 0
                ? Store.Palettes[_selectedPaletteIndex].name
                : "Default";

            RefreshDevicesAsync(true);
        }

        private void OnGUI()
        {
            Store.EnsureInitialized(RoapAdbClient.GetDefaultPackageId());
            EnsureParameterListExists();

            EditorGUILayout.Space(8f);
            DrawHeader();
            EditorGUILayout.Space(8f);
            DrawStatusBox();
            EditorGUILayout.Space(4f);
            DrawPackageSection();
            EditorGUILayout.Space(8f);
            DrawDeviceSection();
            EditorGUILayout.Space(8f);
            DrawLauncherSection();
            EditorGUILayout.Space(8f);
            DrawParametersSection();
            EditorGUILayout.Space(8f);
            DrawPaletteSection();
            EditorGUILayout.Space(8f);
            DrawLaunchSection();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Remotely Open Android App", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"adb: {RoapAdbClient.GetAdbPathForDisplay()}",
                EditorStyles.miniLabel
            );
        }

        private void DrawStatusBox()
        {
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        private void DrawPackageSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Application", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string packageId = EditorGUILayout.TextField("Package Id", Store.PackageIdOverride);
            if (EditorGUI.EndChangeCheck())
            {
                Store.PackageIdOverride = packageId;
                Store.SelectedLauncherComponent = string.Empty;
                SaveStore();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Android PlayerSettings"))
                {
                    Store.PackageIdOverride = RoapAdbClient.GetDefaultPackageId();
                    Store.SelectedLauncherComponent = string.Empty;
                    SaveStore();
                    SetStatus("Restored the package id from Android PlayerSettings.", MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(_isRefreshingLaunchers || !HasReadySelectedDevice()))
                {
                    if (GUILayout.Button("Refresh Launchers"))
                    {
                        RefreshLaunchersAsync();
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDeviceSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Device", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_devices.Count == 0))
                {
                    int selectedIndex = GetSelectedDeviceIndex();
                    string[] labels = _devices.Count == 0
                        ? new[] { "No devices detected" }
                        : _devices.Select(device => device.DisplayName).ToArray();
                    int nextIndex = EditorGUILayout.Popup(
                        "Target Device",
                        Mathf.Max(selectedIndex, 0),
                        labels
                    );
                    if (_devices.Count > 0 && nextIndex != selectedIndex)
                    {
                        Store.SelectedDeviceSerial = _devices[nextIndex].Serial;
                        Store.SelectedLauncherComponent = string.Empty;
                        SaveStore();
                        RefreshLaunchersAsync();
                    }
                }

                using (new EditorGUI.DisabledScope(_isRefreshingDevices))
                {
                    if (GUILayout.Button("Refresh Devices", GUILayout.Width(120f)))
                    {
                        RefreshDevicesAsync(true);
                    }
                }
            }

            if (_devices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No adb devices were detected. Connect a device or start an emulator, then refresh.",
                    MessageType.Warning
                );
            }
            else
            {
                RoapDeviceInfo selectedDevice = GetSelectedDevice();
                if (selectedDevice != null && !selectedDevice.IsReady)
                {
                    EditorGUILayout.HelpBox(
                        $"Selected device is '{selectedDevice.State}'. Launching requires a device in the 'device' state.",
                        MessageType.Warning
                    );
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLauncherSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Launcher Activity", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_launchers.Count == 0))
                {
                    int selectedIndex = GetSelectedLauncherIndex();
                    string[] labels = _launchers.Count == 0
                        ? new[] { "No launcher activities detected" }
                        : _launchers.Select(launcher => launcher.DisplayName).ToArray();
                    int nextIndex = EditorGUILayout.Popup(
                        "Launcher",
                        Mathf.Max(selectedIndex, 0),
                        labels
                    );
                    if (_launchers.Count > 0 && nextIndex != selectedIndex)
                    {
                        Store.SelectedLauncherComponent = _launchers[nextIndex].ComponentName;
                        SaveStore();
                    }
                }

                using (new EditorGUI.DisabledScope(_isRefreshingLaunchers || !HasReadySelectedDevice()))
                {
                    if (GUILayout.Button("Detect", GUILayout.Width(120f)))
                    {
                        RefreshLaunchersAsync();
                    }
                }
            }

            if (_launchers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Launcher activities appear here after detection. The default launcher is tagged with '(default)'.",
                    MessageType.Info
                );
            }
            else
            {
                RoapLauncherInfo selectedLauncher = GetSelectedLauncher();
                if (selectedLauncher != null)
                {
                    EditorGUILayout.SelectableLabel(
                        selectedLauncher.ComponentName,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight)
                    );
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawParametersSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each enabled row is sent as an adb extra when the app launches.",
                MessageType.None
            );

            _parameterScrollPosition = EditorGUILayout.BeginScrollView(
                _parameterScrollPosition,
                GUILayout.MinHeight(180f)
            );

            List<RoapLaunchParameter> parameters = Store.CurrentParameters;
            int removeIndex = -1;
            for (int i = 0; i < parameters.Count; i++)
            {
                RoapLaunchParameter parameter = parameters[i];
                if (parameter == null)
                {
                    parameters[i] = new RoapLaunchParameter();
                    parameter = parameters[i];
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                using (new EditorGUILayout.HorizontalScope())
                {
                    parameter.enabled = EditorGUILayout.Toggle(parameter.enabled, GUILayout.Width(18f));
                    parameter.key = EditorGUILayout.TextField("Key", parameter.key);
                    parameter.type = (RoapParameterType)EditorGUILayout.EnumPopup(
                        "Type",
                        parameter.type,
                        GUILayout.Width(190f)
                    );
                    if (GUILayout.Button("Remove", GUILayout.Width(80f)))
                    {
                        removeIndex = i;
                    }
                }

                parameter.value = EditorGUILayout.TextField("Value", parameter.value);
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
            {
                parameters.RemoveAt(removeIndex);
                SaveStore();
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Parameter"))
                {
                    parameters.Add(new RoapLaunchParameter());
                    SaveStore();
                }

                if (GUILayout.Button("Clear Parameters"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Clear Parameters",
                            "Remove all current launch parameters?",
                            "Clear",
                            "Cancel"
                        ))
                    {
                        parameters.Clear();
                        parameters.Add(new RoapLaunchParameter());
                        SaveStore();
                    }
                }
            }

            EditorGUILayout.EndVertical();

            if (GUI.changed)
            {
                SaveStore();
            }
        }

        private void DrawPaletteSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Parameter Palette", EditorStyles.boldLabel);

            string[] paletteNames = Store.Palettes.Count == 0
                ? new[] { "No saved palettes" }
                : Store.Palettes.Select(palette => palette.name).ToArray();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Store.Palettes.Count == 0))
                {
                    int nextIndex = EditorGUILayout.Popup(
                        "Saved Palette",
                        Mathf.Max(_selectedPaletteIndex, 0),
                        paletteNames
                    );
                    if (Store.Palettes.Count > 0 && nextIndex != _selectedPaletteIndex)
                    {
                        _selectedPaletteIndex = nextIndex;
                        _paletteName = Store.Palettes[nextIndex].name;
                    }
                }
            }

            _paletteName = EditorGUILayout.TextField("Palette Name", _paletteName);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Current To Palette"))
                {
                    SaveCurrentAsPalette();
                }

                using (new EditorGUI.DisabledScope(_selectedPaletteIndex < 0 || _selectedPaletteIndex >= Store.Palettes.Count))
                {
                    if (GUILayout.Button("Load Palette"))
                    {
                        LoadSelectedPalette();
                    }

                    if (GUILayout.Button("Delete Palette"))
                    {
                        DeleteSelectedPalette();
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLaunchSection()
        {
            using (new EditorGUI.DisabledScope(
                       _isLaunching ||
                       !HasReadySelectedDevice() ||
                       string.IsNullOrWhiteSpace(Store.SelectedLauncherComponent) ||
                       string.IsNullOrWhiteSpace(Store.PackageIdOverride)
                   ))
            {
                if (GUILayout.Button("Launch App", GUILayout.Height(32f)))
                {
                    LaunchAppAsync();
                }
            }
        }

        private void SaveCurrentAsPalette()
        {
            string paletteName = string.IsNullOrWhiteSpace(_paletteName)
                ? "Default"
                : _paletteName.Trim();

            RoapParameterPalette palette = new RoapParameterPalette
            {
                name = paletteName,
                parameters = CloneParameters(Store.CurrentParameters),
            };

            int existingIndex = Store.Palettes.FindIndex(
                item => string.Equals(item.name, paletteName, StringComparison.OrdinalIgnoreCase)
            );
            if (existingIndex >= 0)
            {
                Store.Palettes[existingIndex] = palette;
                _selectedPaletteIndex = existingIndex;
                SetStatus($"Updated palette '{paletteName}'.", MessageType.Info);
            }
            else
            {
                Store.Palettes.Add(palette);
                _selectedPaletteIndex = Store.Palettes.Count - 1;
                SetStatus($"Saved palette '{paletteName}'.", MessageType.Info);
            }

            SaveStore();
        }

        private void LoadSelectedPalette()
        {
            if (_selectedPaletteIndex < 0 || _selectedPaletteIndex >= Store.Palettes.Count)
            {
                return;
            }

            Store.CurrentParameters.Clear();
            Store.CurrentParameters.AddRange(CloneParameters(Store.Palettes[_selectedPaletteIndex].parameters));
            if (Store.CurrentParameters.Count == 0)
            {
                Store.CurrentParameters.Add(new RoapLaunchParameter());
            }

            SaveStore();
            SetStatus($"Loaded palette '{Store.Palettes[_selectedPaletteIndex].name}'.", MessageType.Info);
        }

        private void DeleteSelectedPalette()
        {
            if (_selectedPaletteIndex < 0 || _selectedPaletteIndex >= Store.Palettes.Count)
            {
                return;
            }

            string paletteName = Store.Palettes[_selectedPaletteIndex].name;
            if (!EditorUtility.DisplayDialog(
                    "Delete Palette",
                    $"Delete palette '{paletteName}'?",
                    "Delete",
                    "Cancel"
                ))
            {
                return;
            }

            Store.Palettes.RemoveAt(_selectedPaletteIndex);
            _selectedPaletteIndex = Store.Palettes.Count == 0
                ? -1
                : Mathf.Clamp(_selectedPaletteIndex - 1, 0, Store.Palettes.Count - 1);
            _paletteName = _selectedPaletteIndex >= 0
                ? Store.Palettes[_selectedPaletteIndex].name
                : "Default";

            SaveStore();
            SetStatus($"Deleted palette '{paletteName}'.", MessageType.Info);
        }

        private async void RefreshDevicesAsync(bool refreshLaunchersAfterward)
        {
            if (_isRefreshingDevices)
            {
                return;
            }

            _isRefreshingDevices = true;
            SetStatus("Refreshing adb devices...", MessageType.Info);
            Repaint();

            try
            {
                List<RoapDeviceInfo> devices = await Task.Run(RoapAdbClient.GetDevices);
                _devices = devices;
                ChooseBestDeviceSelection();

                if (_devices.Count == 0)
                {
                    _launchers.Clear();
                    Store.SelectedLauncherComponent = string.Empty;
                    SaveStore();
                    SetStatus("No Android devices detected by adb.", MessageType.Warning);
                }
                else
                {
                    SetStatus($"Detected {_devices.Count} adb device(s).", MessageType.Info);
                    if (refreshLaunchersAfterward)
                    {
                        RefreshLaunchersAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _devices.Clear();
                _launchers.Clear();
                Store.SelectedLauncherComponent = string.Empty;
                SaveStore();
                SetStatus(ex.Message, MessageType.Error);
            }
            finally
            {
                _isRefreshingDevices = false;
                Repaint();
            }
        }

        private async void RefreshLaunchersAsync()
        {
            if (_isRefreshingLaunchers)
            {
                return;
            }

            RoapDeviceInfo selectedDevice = GetSelectedDevice();
            if (selectedDevice == null)
            {
                SetStatus("Select an adb device before refreshing launchers.", MessageType.Warning);
                return;
            }

            if (!selectedDevice.IsReady)
            {
                SetStatus(
                    $"Selected device '{selectedDevice.Serial}' is '{selectedDevice.State}'.",
                    MessageType.Warning
                );
                return;
            }

            _isRefreshingLaunchers = true;
            SetStatus("Detecting launcher activities...", MessageType.Info);
            Repaint();

            try
            {
                List<RoapLauncherInfo> launchers = await Task.Run(
                    () => RoapAdbClient.GetLaunchers(selectedDevice.Serial, Store.PackageIdOverride)
                );
                _launchers = launchers;
                ChooseBestLauncherSelection();
                SetStatus($"Detected {_launchers.Count} launcher activit{(_launchers.Count == 1 ? "y" : "ies")}.", MessageType.Info);
            }
            catch (Exception ex)
            {
                _launchers.Clear();
                Store.SelectedLauncherComponent = string.Empty;
                SaveStore();
                SetStatus(ex.Message, MessageType.Error);
            }
            finally
            {
                _isRefreshingLaunchers = false;
                Repaint();
            }
        }

        private async void LaunchAppAsync()
        {
            if (_isLaunching)
            {
                return;
            }

            RoapDeviceInfo selectedDevice = GetSelectedDevice();
            RoapLauncherInfo selectedLauncher = GetSelectedLauncher();
            if (selectedDevice == null || selectedLauncher == null)
            {
                SetStatus("Pick both a device and launcher before launching.", MessageType.Warning);
                return;
            }

            _isLaunching = true;
            SetStatus("Launching Android app over adb...", MessageType.Info);
            Repaint();

            try
            {
                string output = await Task.Run(
                    () => RoapAdbClient.LaunchApp(
                        selectedDevice.Serial,
                        Store.PackageIdOverride,
                        selectedLauncher.ComponentName,
                        Store.CurrentParameters
                    )
                );
                SetStatus(output, MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, MessageType.Error);
            }
            finally
            {
                _isLaunching = false;
                Repaint();
            }
        }

        private void EnsureParameterListExists()
        {
            if (Store.CurrentParameters.Count == 0)
            {
                Store.CurrentParameters.Add(new RoapLaunchParameter());
                SaveStore();
            }
        }

        private void ChooseBestDeviceSelection()
        {
            if (_devices.Count == 0)
            {
                Store.SelectedDeviceSerial = string.Empty;
                SaveStore();
                return;
            }

            RoapDeviceInfo exactMatch = _devices.FirstOrDefault(
                device => string.Equals(device.Serial, Store.SelectedDeviceSerial, StringComparison.OrdinalIgnoreCase)
            );
            if (exactMatch != null)
            {
                return;
            }

            RoapDeviceInfo readyDevice = _devices.FirstOrDefault(device => device.IsReady);
            Store.SelectedDeviceSerial = readyDevice?.Serial ?? _devices[0].Serial;
            SaveStore();
        }

        private void ChooseBestLauncherSelection()
        {
            if (_launchers.Count == 0)
            {
                Store.SelectedLauncherComponent = string.Empty;
                SaveStore();
                return;
            }

            RoapLauncherInfo exactMatch = _launchers.FirstOrDefault(
                launcher => string.Equals(launcher.ComponentName, Store.SelectedLauncherComponent, StringComparison.OrdinalIgnoreCase)
            );
            if (exactMatch != null)
            {
                return;
            }

            RoapLauncherInfo defaultLauncher = _launchers.FirstOrDefault(launcher => launcher.IsResolvedDefault);
            Store.SelectedLauncherComponent = defaultLauncher?.ComponentName ?? _launchers[0].ComponentName;
            SaveStore();
        }

        private int GetSelectedDeviceIndex()
        {
            return _devices.FindIndex(
                device => string.Equals(device.Serial, Store.SelectedDeviceSerial, StringComparison.OrdinalIgnoreCase)
            );
        }

        private RoapDeviceInfo GetSelectedDevice()
        {
            int selectedIndex = GetSelectedDeviceIndex();
            return selectedIndex >= 0 && selectedIndex < _devices.Count
                ? _devices[selectedIndex]
                : null;
        }

        private bool HasReadySelectedDevice()
        {
            return GetSelectedDevice()?.IsReady == true;
        }

        private int GetSelectedLauncherIndex()
        {
            return _launchers.FindIndex(
                launcher => string.Equals(launcher.ComponentName, Store.SelectedLauncherComponent, StringComparison.OrdinalIgnoreCase)
            );
        }

        private RoapLauncherInfo GetSelectedLauncher()
        {
            int selectedIndex = GetSelectedLauncherIndex();
            return selectedIndex >= 0 && selectedIndex < _launchers.Count
                ? _launchers[selectedIndex]
                : null;
        }

        private static List<RoapLaunchParameter> CloneParameters(IEnumerable<RoapLaunchParameter> parameters)
        {
            List<RoapLaunchParameter> clone = new List<RoapLaunchParameter>();
            if (parameters == null)
            {
                return clone;
            }

            foreach (RoapLaunchParameter parameter in parameters)
            {
                if (parameter == null)
                {
                    continue;
                }

                clone.Add(parameter.Clone());
            }

            return clone;
        }

        private void SaveStore()
        {
            Store.SaveStore();
        }

        private void SetStatus(string message, MessageType messageType)
        {
            _statusMessage = string.IsNullOrWhiteSpace(message)
                ? "Ready."
                : message;
            _statusType = messageType;
        }
    }
}
