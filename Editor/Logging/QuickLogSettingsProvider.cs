using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Logging.Editor
{
    internal sealed class QuickLogSettingsProvider : SettingsProvider
    {
        #region Constants
        private const string SettingsAssetPath = "Assets/Resources/LoggingConfiguration.asset";
        private const string ResourcesFolder = "Assets/Resources";
        private const string HeaderLabel = "Quick Log Settings";
        private const string LogLevelsSectionLabel = "Log Levels";
        private const string ColorsSectionLabel = "Colors";

        private static readonly ColorPalette[] Palettes = new ColorPalette[]
        {
            new ColorPalette(
                "Default",
                new Color(0.5f, 0.5f, 0.5f),
                Color.white,
                new Color(1f, 0.92f, 0.016f),
                Color.red,
                Color.magenta
            ),
            new ColorPalette(
                "VS Code",
                new Color(0f, 0.75f, 1f),
                new Color(0.416f, 0.6f, 0.333f),
                new Color(0.808f, 0.569f, 0.471f),
                new Color(0.957f, 0.278f, 0.278f),
                new Color(0.773f, 0.525f, 0.753f)
            ),
            new ColorPalette(
                "Pastel",
                new Color(0.565f, 0.643f, 0.682f),
                new Color(0.506f, 0.831f, 0.98f),
                new Color(1f, 0.8f, 0.502f),
                new Color(0.937f, 0.604f, 0.604f),
                new Color(0.808f, 0.576f, 0.847f)
            ),
            new ColorPalette(
                "High Contrast",
                new Color(0.667f, 0.667f, 0.667f),
                Color.white,
                new Color(1f, 0.843f, 0f),
                new Color(1f, 0.267f, 0.267f),
                new Color(1f, 0.267f, 1f)
            ),
        };
        #endregion

        #region Private Fields
        private SerializedObject _serializedSettings;
        private bool _logLevelsSectionExpanded = true;
        private bool _colorsSectionExpanded = true;
        private int _selectedPaletteIndex = -1;
        #endregion

        #region Constructor
        private QuickLogSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        { }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new QuickLogSettingsProvider(
                "Project/Tools/Quick Log",
                SettingsScope.Project,
                new[] { "quick", "log", "logging", "debug", "color" }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            DrawHeader();

            LoggingConfiguration settings = GetOrCreateSettings();
            if (settings == null)
                return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            DrawLogLevelsSection();
            DrawColorsSection();

            _serializedSettings.ApplyModifiedProperties();
        }
        #endregion

        #region Private Methods — Asset Management
        private static LoggingConfiguration GetOrCreateSettings()
        {
            LoggingConfiguration settings =
                AssetDatabase.LoadAssetAtPath<LoggingConfiguration>(SettingsAssetPath);
            if (settings != null)
                return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Quick Log Configuration asset found. " +
                "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button("Create Quick Log Configuration", GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourcesFolder))
                Directory.CreateDirectory(ResourcesFolder);

            LoggingConfiguration settings =
                ScriptableObject.CreateInstance<LoggingConfiguration>();

            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[QuickLog] Created settings asset at '{SettingsAssetPath}'.");
        }
        #endregion

        #region Private Methods — Drawing
        private static void DrawHeader()
        {
            EditorGUILayout.LabelField(HeaderLabel, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Configure the Quick Log system. Changes take effect immediately at runtime. " +
                "Color settings only apply in the Editor.",
                MessageType.None);
        }

        private void EnsureSerializedObject(LoggingConfiguration settings)
        {
            if (_serializedSettings != null &&
                _serializedSettings.targetObject == settings)
                return;

            _serializedSettings = new SerializedObject(settings);
        }

        private void DrawLogLevelsSection()
        {
            _logLevelsSectionExpanded = EditorGUILayout.Foldout(
                _logLevelsSectionExpanded,
                LogLevelsSectionLabel,
                true);

            if (!_logLevelsSectionExpanded)
                return;

            EditorGUI.indentLevel++;
            DrawProperty("minimumLogLevel", "Minimum Log Level");
            DrawProperty("forceUsingWarningAsError", "Force Warnings as Errors");
            EditorGUI.indentLevel--;
        }

        private void DrawColorsSection()
        {
            _colorsSectionExpanded = EditorGUILayout.Foldout(
                _colorsSectionExpanded,
                ColorsSectionLabel,
                true);

            if (!_colorsSectionExpanded)
                return;

            EditorGUI.indentLevel++;

            DrawPaletteSelector();
            EditorGUILayout.Space(4);

            DrawColorProperty("debugColor", "Debug");
            DrawColorProperty("infoColor", "Info");
            DrawColorProperty("warningColor", "Warning");
            DrawColorProperty("errorColor", "Error");
            DrawColorProperty("criticalColor", "Critical");

            EditorGUI.indentLevel--;
        }

        private void DrawPaletteSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Color Palette");

            string[] paletteNames = new string[Palettes.Length + 1];
            paletteNames[0] = "Custom";
            for (int i = 0; i < Palettes.Length; i++)
                paletteNames[i + 1] = Palettes[i].Name;

            int displayIndex = _selectedPaletteIndex + 1;
            int newDisplayIndex = EditorGUILayout.Popup(displayIndex, paletteNames);

            if (newDisplayIndex != displayIndex)
            {
                int newPaletteIndex = newDisplayIndex - 1;
                if (newPaletteIndex >= 0)
                {
                    ApplyPalette(Palettes[newPaletteIndex]);
                    _selectedPaletteIndex = newPaletteIndex;
                }
                else
                {
                    _selectedPaletteIndex = -1;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyPalette(ColorPalette palette)
        {
            ApplyColorProperty("debugColor", palette.Debug);
            ApplyColorProperty("infoColor", palette.Info);
            ApplyColorProperty("warningColor", palette.Warning);
            ApplyColorProperty("errorColor", palette.Error);
            ApplyColorProperty("criticalColor", palette.Critical);
        }

        private void ApplyColorProperty(string propertyName, Color color)
        {
            SerializedProperty prop = _serializedSettings.FindProperty(propertyName);
            if (prop != null)
            {
                prop.colorValue = color;
            }
        }

        private void DrawColorProperty(string propertyName, string label)
        {
            SerializedProperty prop = _serializedSettings.FindProperty(propertyName);
            if (prop == null)
            {
                EditorGUILayout.LabelField(label, "<missing>");
                return;
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        private void DrawProperty(string propertyName, string label)
        {
            SerializedProperty prop = _serializedSettings.FindProperty(propertyName);
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

        #region Nested Types
        private readonly struct ColorPalette
        {
            public readonly string Name;
            public readonly Color Debug;
            public readonly Color Info;
            public readonly Color Warning;
            public readonly Color Error;
            public readonly Color Critical;

            public ColorPalette(
                string name,
                Color debug,
                Color info,
                Color warning,
                Color error,
                Color critical
            )
            {
                Name = name;
                Debug = debug;
                Info = info;
                Warning = warning;
                Error = error;
                Critical = critical;
            }
        }
        #endregion
    }
}
