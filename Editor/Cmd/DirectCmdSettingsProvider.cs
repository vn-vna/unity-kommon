using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Editor
{
    internal sealed class DirectCmdSettingsProvider : SettingsProvider
    {
        #region Private Fields
        private const string SettingsAssetPath = "Assets/Resources/DirectCmdSettings.asset";
        private const string ResourcesFolder = "Assets/Resources";
        private const string TickerSectionLabel = "Ticker Configuration";
        private const string CommandsSectionLabel = "Default Commands";

        private SerializedObject _serializedSettings;
        private bool _tickerSectionExpanded = true;
        private bool _commandsSectionExpanded = true;
        #endregion

        #region Constructor
        private DirectCmdSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new DirectCmdSettingsProvider(
                "Project/Tools/Direct Command",
                SettingsScope.Project,
                new[] { "dcf", "direct", "cmd", "command", "forwarding", "poll", "ticker" }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            DrawHeader();

            DirectCmdSettings settings = GetOrCreateSettings();
            if (settings == null)
                return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            DrawTickerSection();
            DrawCommandsSection();

            _serializedSettings.ApplyModifiedProperties();
        }
        #endregion

        #region Private Methods — Asset Management
        private static DirectCmdSettings GetOrCreateSettings()
        {
            DirectCmdSettings settings = DirectCmdSettings.Instance;

            if (settings != null)
                return settings;

            settings = AssetDatabase.LoadAssetAtPath<DirectCmdSettings>(SettingsAssetPath);
            if (settings != null)
                return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Direct Command Settings asset found. " +
                "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button("Create Direct Command Settings", GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourcesFolder))
                Directory.CreateDirectory(ResourcesFolder);

            DirectCmdSettings settings =
                ScriptableObject.CreateInstance<DirectCmdSettings>();

            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[DirectCmdSettings] Created settings asset at '{SettingsAssetPath}'.");
        }
        #endregion

        #region Private Methods — Drawing
        private static void DrawHeader()
        {
            EditorGUILayout.LabelField(
                "Direct Command Settings",
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "Configure the DirectCmdForwarding ticker and enable or disable " +
                "built-in default commands. Changes take effect at runtime.",
                MessageType.None);
        }

        private void EnsureSerializedObject(DirectCmdSettings settings)
        {
            if (_serializedSettings != null &&
                _serializedSettings.targetObject == settings)
                return;

            _serializedSettings = new SerializedObject(settings);
        }

        private void DrawTickerSection()
        {
            _tickerSectionExpanded = EditorGUILayout.Foldout(
                _tickerSectionExpanded,
                TickerSectionLabel,
                true);

            if (!_tickerSectionExpanded)
                return;

            EditorGUI.indentLevel++;

            DrawProperty("pollEveryFrame", "Poll Every Frame");
            DrawProperty("pollInterval", "Poll Interval (seconds)");

            EditorGUI.indentLevel--;
        }

        private void DrawCommandsSection()
        {
            _commandsSectionExpanded = EditorGUILayout.Foldout(
                _commandsSectionExpanded,
                CommandsSectionLabel,
                true);

            if (!_commandsSectionExpanded)
                return;

            EditorGUI.indentLevel++;

            DrawProperty("enableAdsCommands", "Ads Commands (ads show ...)");
            DrawProperty("enableIapCommands", "IAP Commands (iap buy ...)");
            DrawProperty(
                "enableTrackingCommands",
                "Tracking Commands (tracking enabled on/off)");
            DrawProperty(
                "enableTrackingFilterCommands",
                "Tracking Filter Commands (tracking filtered on/off)");

            EditorGUI.indentLevel--;
        }

        private void DrawProperty(string propertyName, string label)
        {
            SerializedProperty prop =
                _serializedSettings.FindProperty(propertyName);

            if (prop == null)
            {
                EditorGUILayout.LabelField(
                    label,
                    $"<missing property: {propertyName}>");
                return;
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }
        #endregion
    }
}
