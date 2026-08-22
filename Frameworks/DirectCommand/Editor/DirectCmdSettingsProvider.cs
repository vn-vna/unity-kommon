using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Hapiga.Scheherazade.Editor
{
    internal sealed partial class DirectCmdSettingsProvider : SettingsProvider
    {
        #region Constants
        private const string SettingsAssetPath = "Assets/Resources/DirectCmdSettings.asset";
        private const string ResourcesFolder = "Assets/Resources";
        private const string TabPrefKey = "DirectCmdSettingsProvider.SelectedTab";

        private static readonly string[] TabNames = { "Settings", "Command Forwarding" };
        #endregion

        #region Private Fields
        private SerializedObject _serializedSettings;
        private int _selectedTabIndex;
        private Vector2 _settingsScrollPosition;
        #endregion

        #region Constructor
        private DirectCmdSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _selectedTabIndex = EditorPrefs.GetInt(TabPrefKey, 0);
        }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new DirectCmdSettingsProvider(
                "Project/Frameworks/Direct Command",
                SettingsScope.Project,
                new[]
                {
                    "dcf", "direct", "cmd", "command", "forwarding",
                    "poll", "ticker", "adb", "android"
                }
            );
        }
        #endregion

        #region Unity Callbacks
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            InitializeCommandForwarding();
        }

        public override void OnDeactivate()
        {
            PersistCommandForwarding();
            EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            base.OnDeactivate();
        }
        #endregion

        #region Public Methods
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            DrawHeader();
            EditorGUILayout.Space(6f);

            int selectedTab = GUILayout.Toolbar(_selectedTabIndex, TabNames, GUILayout.Height(26f));
            if (selectedTab != _selectedTabIndex)
            {
                _selectedTabIndex = selectedTab;
                EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
                GUI.FocusControl(null);
            }

            DrawDivider();
            EditorGUILayout.Space(6f);

            if (_selectedTabIndex == 0)
            {
                DrawSettingsTab();
                return;
            }

            DrawCommandForwardingTab();
        }

        [MenuItem("Dev Menu/Tools/Direct Command")]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Frameworks/Direct Command");
        }
        #endregion

        #region Private Methods
        private void DrawSettingsTab()
        {
            DirectCmdSettings settings = GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            _settingsScrollPosition = EditorGUILayout.BeginScrollView(_settingsScrollPosition);
            DrawPollingCard(settings);
            EditorGUILayout.Space(6f);
            DrawDefaultCommandsCard();
            EditorGUILayout.EndScrollView();

            _serializedSettings.ApplyModifiedProperties();
        }

        private static void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorGuiStyles.Card))
            {
                GUIContent icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
                GUILayout.Label(icon, GUILayout.Width(24f), GUILayout.Height(24f));

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Direct Command", EditorGuiStyles.HeaderTitle);
                    EditorGUILayout.LabelField(
                        "Configure commands and forward them to the Editor or Android devices.",
                        EditorGuiStyles.HeaderSubtitle
                    );
                }
            }
        }

        private static void DrawDivider()
        {
            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private void DrawPollingCard(DirectCmdSettings settings)
        {
            using (new EditorGUILayout.VerticalScope(EditorGuiStyles.Card))
            {
                EditorGUILayout.LabelField("Polling", EditorGuiStyles.SectionHeader);
                EditorGUILayout.LabelField(
                    settings.PollEveryFrame
                        ? "The command file is checked every frame."
                        : $"The command file is checked every {settings.PollInterval:0.##} seconds.",
                    EditorGuiStyles.InlineStatus
                );
                EditorGUILayout.Space(4f);

                DrawProperty("pollEveryFrame", "Poll Every Frame");
                using (new EditorGUI.DisabledScope(
                           _serializedSettings.FindProperty("pollEveryFrame").boolValue))
                {
                    DrawProperty("pollInterval", "Poll Interval (Seconds)");
                }
            }
        }

        private void DrawDefaultCommandsCard()
        {
            using (new EditorGUILayout.VerticalScope(EditorGuiStyles.Card))
            {
                EditorGUILayout.LabelField("Built-In Commands", EditorGuiStyles.SectionHeader);
                EditorGUILayout.LabelField(
                    "Disable a group to keep its commands registered but block execution at runtime.",
                    EditorGuiStyles.InlineStatus
                );
                EditorGUILayout.Space(4f);

                DrawProperty("enableAdsCommands", "Ads  ·  ads show ...");
                DrawProperty("enableIapCommands", "IAP  ·  iap buy --product <id>");
                DrawProperty("enableTrackingCommands", "Tracking  ·  tracking enabled on/off");
                DrawProperty(
                    "enableTrackingFilterCommands",
                    "Tracking Filter  ·  tracking filtered on/off"
                );
            }
        }

        private static DirectCmdSettings GetOrCreateSettings()
        {
            DirectCmdSettings settings = DirectCmdSettings.Instance;
            if (settings != null)
            {
                return settings;
            }

            settings = AssetDatabase.LoadAssetAtPath<DirectCmdSettings>(SettingsAssetPath);
            if (settings != null)
            {
                return settings;
            }

            using (new EditorGUILayout.VerticalScope(EditorGuiStyles.Card))
            {
                EditorGUILayout.HelpBox(
                    "No Direct Command Settings asset was found. Create one to configure "
                    + "polling and the built-in command groups.",
                    MessageType.Info
                );

                if (GUILayout.Button("Create Direct Command Settings", GUILayout.Height(28f)))
                {
                    CreateSettingsAsset();
                }
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourcesFolder))
            {
                Directory.CreateDirectory(ResourcesFolder);
            }

            var settings = ScriptableObject.CreateInstance<DirectCmdSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(settings);
        }

        private void EnsureSerializedObject(DirectCmdSettings settings)
        {
            if (_serializedSettings != null && _serializedSettings.targetObject == settings)
            {
                return;
            }

            _serializedSettings = new SerializedObject(settings);
        }

        private void DrawProperty(string propertyName, string label)
        {
            SerializedProperty property = _serializedSettings.FindProperty(propertyName);
            if (property == null)
            {
                EditorGUILayout.HelpBox(
                    $"Missing serialized property '{propertyName}'.",
                    MessageType.Error
                );
                return;
            }

            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
        #endregion
    }
}
