using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Chrono;
using Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono.Editor
{
    internal sealed class ChronoDirectorSettingsProvider : SettingsProvider
    {
        #region Constants
        private const string SettingsAssetPath =
            "Assets/Resources/ChronoConfiguration.asset";

        private const string ResourcesFolder = "Assets/Resources";

        private const string ChronoAssetFolder = "Assets/Resources/Chrono";

        private const string ChronoNtpDefine = "CHRONO_NTP";

        private static readonly string[] TabNames =
            { "Director", "Provider", "Persisters" };
        #endregion

        #region Private Fields
        private SerializedObject _serializedSettings;

        private int _selectedTabIndex;

        private Vector2 _scrollPosition;

        private UnityEditor.Editor _timeProviderEditor;

        private ScriptableObject _timeProviderEditorTarget;
        #endregion

        #region Constructor
        private ChronoDirectorSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new ChronoDirectorSettingsProvider(
                "Project/Tools/Chrono Director",
                SettingsScope.Project,
                new[]
                {
                    "chrono", "time", "timer", "schedule",
                    "mock", "ntp", "persist", "online"
                }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            ChronoConfiguration settings = GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Chrono Director Settings",
                EditorStyles.boldLabel
            );

            GUILayout.Space(4);
            DrawTimePreview();

            GUILayout.Space(6);
            _selectedTabIndex = GUILayout.Toolbar(
                _selectedTabIndex,
                TabNames
            );
            GUILayout.Space(4);

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f)
            );
            GUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0:
                    DrawDirectorTab();
                    break;
                case 1:
                    DrawProviderTab();
                    break;
                case 2:
                    DrawPersistersTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            DestroyTimeProviderEditor();
        }
        #endregion

        #region Tab — Director
        private void DrawDirectorTab()
        {
            EditorGUILayout.HelpBox(
                "Configure the Chrono Director. The time provider controls "
                + "how time flows (real, mock, or NTP). Persisters save the "
                + "last-online timestamp across sessions.",
                MessageType.None
            );

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Config Asset",
                    GUILayout.Width(EditorGUIUtility.labelWidth)
                );
                EditorGUILayout.LabelField(
                    SettingsAssetPath,
                    EditorStyles.miniLabel
                );
            }

            GUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Online Marker",
                    EditorStyles.miniBoldLabel
                );

                DrawSerializedProperty(
                    "_onlineMarkerIntervalMs",
                    "Interval (ms)"
                );
            }
        }
        #endregion

        #region Tab — Provider
        private void DrawProviderTab()
        {
            EditorGUILayout.HelpBox(
                "Select the time provider for the Chrono Director. "
                + "Only one provider can be active at a time. "
                + "If none is enabled, SystemTimeProvider is used at runtime.",
                MessageType.None
            );

            GUILayout.Space(4);

            SerializedProperty timeProviderProp = _serializedSettings
                .FindProperty("_timeProvider");

            if (timeProviderProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_timeProvider' field.",
                    MessageType.Error
                );
                return;
            }

            ScriptableObject currentProvider = timeProviderProp
                .objectReferenceValue as ScriptableObject;

            Type currentType = currentProvider?.GetType();

            DrawProviderRow(
                "System Time Provider",
                typeof(SystemTimeProvider),
                currentType == typeof(SystemTimeProvider),
                currentProvider,
                timeProviderProp
            );

            GUILayout.Space(4);

            DrawProviderRow(
                "Mock Time Provider",
                typeof(MockTimeProvider),
                currentType == typeof(MockTimeProvider),
                currentProvider,
                timeProviderProp
            );

            GUILayout.Space(4);

#if CHRONO_NTP
            DrawProviderRow(
                "NTP Time Provider",
                typeof(NtpTimeProvider),
                currentType == typeof(NtpTimeProvider),
                currentProvider,
                timeProviderProp
            );
#else
            DrawNtpLockedRow(timeProviderProp);
#endif

            DrawCreateCustomButton(
                "New Custom Time Provider",
                typeof(TimeProviderBase),
                ScriptTemplateGenerator.GenerationMode.AbstractClassInheritance);
        }

        private void DrawProviderRow(
            string displayName,
            Type providerType,
            bool isActive,
            ScriptableObject currentProvider,
            SerializedProperty prop
        )
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row: name + button
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        displayName,
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    if (isActive && currentProvider != null)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            prop.objectReferenceValue = null;
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            DestroyTimeProviderEditor();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset = FindOrCreateProviderAsset(
                                providerType,
                                providerType.Name
                            );

                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            DestroyTimeProviderEditor();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                // Configuration section (only for active provider)
                if (isActive && currentProvider != null)
                {
                    GUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Configuration of " + displayName,
                            EditorStyles.miniBoldLabel
                        );

                        GUILayout.FlexibleSpace();

                        if (DrawIconButton(
                                "TreeEditor.Trash",
                                "\u2717",
                                "Delete the provider asset from disk."))
                        {
                            DeleteAssetAndClearField(
                                currentProvider,
                                prop
                            );
                            DestroyTimeProviderEditor();
                        }
                    }

                    GUILayout.Space(2);

                    DrawInlineInspector(
                        ref _timeProviderEditor,
                        ref _timeProviderEditorTarget,
                        currentProvider,
                        null // no inner header, we already have one
                    );
                }
            }
        }

        private void DrawNtpLockedRow(SerializedProperty prop)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "NTP Time Provider",
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    Color oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.9f, 0.7f, 0.3f);

                    if (GUILayout.Button(
                            "Enable " + ChronoNtpDefine,
                            GUILayout.Width(160)))
                    {
                        EnableDefine(ChronoNtpDefine);
                    }

                    GUI.backgroundColor = oldColor;
                }

                EditorGUILayout.LabelField(
                    "Add the " + ChronoNtpDefine
                    + " scripting define to unlock this provider.",
                    EditorStyles.miniLabel
                );
            }
        }

        private ScriptableObject FindOrCreateProviderAsset(
            Type providerType,
            string defaultName)
        {
            // Search for existing asset of this type
            string[] guids = AssetDatabase.FindAssets(
                "t:" + providerType.Name
            );

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                    path,
                    providerType
                ) as ScriptableObject;

                if (asset != null)
                {
                    return asset;
                }
            }

            // Create new asset
            EnsureChronoAssetFolder();

            string assetPath = ChronoAssetFolder + "/" + defaultName + ".asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            ScriptableObject newAsset = ScriptableObject
                .CreateInstance(providerType);

            newAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
        }

        private void DrawTimePreview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Current Time",
                    EditorStyles.miniBoldLabel
                );

                if (!Application.isPlaying)
                {
                    EditorGUILayout.LabelField(
                        "Enter Play Mode to see live time from the active provider.",
                        EditorStyles.miniLabel
                    );
                    return;
                }

                ChronoDirector director = ChronoDirector.Instance;
                if (director == null)
                {
                    EditorGUILayout.LabelField(
                        "ChronoDirector not initialized yet.",
                        EditorStyles.miniLabel
                    );
                    return;
                }

                ITimeProvider provider = director.TimeProvider;
                if (provider == null)
                {
                    EditorGUILayout.LabelField(
                        "No time provider active.",
                        EditorStyles.miniLabel
                    );
                    return;
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    "Provider",
                    provider.GetType().Name,
                    EditorStyles.miniLabel
                );

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Local",
                        provider.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    );

                    EditorGUILayout.TextField(
                        "UTC",
                        provider.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    );

                    EditorGUILayout.TextField(
                        "Unix (ms)",
                        provider.UnixTimeMilliseconds.ToString("N0")
                    );
                }

                EditorGUI.indentLevel--;
            }
        }
        #endregion

        #region Tab — Persisters
        private void DrawPersistersTab()
        {
            EditorGUILayout.HelpBox(
                "Select the persister for saving the last-online timestamp. "
                + "Only one persister can be active at a time. "
                + "Use Null Persister if no persistence is needed.",
                MessageType.None
            );

            GUILayout.Space(4);

            SerializedProperty persisterProp = _serializedSettings
                .FindProperty("_persister");

            if (persisterProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_persister' field.",
                    MessageType.Error
                );
                return;
            }

            ScriptableObject currentPersister = persisterProp
                .objectReferenceValue as ScriptableObject;

            Type currentType = currentPersister?.GetType();

            DrawPersisterRow(
                "Null Persister",
                typeof(NullChronoPersister),
                currentType == typeof(NullChronoPersister),
                currentPersister,
                persisterProp
            );

            GUILayout.Space(4);

            DrawPersisterRow(
                "PlayerPrefs Persister",
                typeof(PlayerPrefsChronoPersister),
                currentType == typeof(PlayerPrefsChronoPersister),
                currentPersister,
                persisterProp
            );

            GUILayout.Space(4);

            DrawPersisterRow(
                "JSON Persister",
                typeof(JsonChronoPersister),
                currentType == typeof(JsonChronoPersister),
                currentPersister,
                persisterProp
            );

            DrawCreateCustomButton(
                "New Custom Persister",
                typeof(IChronoPersister),
                ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
        }

        private void DrawPersisterRow(
            string displayName,
            Type persisterType,
            bool isActive,
            ScriptableObject currentPersister,
            SerializedProperty prop
        )
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        displayName,
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    if (isActive && currentPersister != null)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            prop.objectReferenceValue = null;
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset = FindOrCreatePersisterAsset(
                                persisterType,
                                persisterType.Name
                            );

                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && currentPersister != null)
                {
                    GUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Configuration of " + displayName,
                            EditorStyles.miniBoldLabel
                        );

                        GUILayout.FlexibleSpace();

                        if (DrawIconButton(
                                "TreeEditor.Trash",
                                "\u2717",
                                "Delete the persister asset from disk."))
                        {
                            DeleteAssetAndClearField(
                                currentPersister,
                                prop
                            );
                        }
                    }

                    GUILayout.Space(2);

                    DrawInlineInspectorForPersister(currentPersister);
                }
            }
        }

        private ScriptableObject FindOrCreatePersisterAsset(
            Type persisterType,
            string defaultName)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:" + persisterType.Name
            );

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                    path,
                    persisterType
                ) as ScriptableObject;

                if (asset != null)
                {
                    return asset;
                }
            }

            EnsureChronoAssetFolder();

            string assetPath = ChronoAssetFolder + "/" + defaultName + ".asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            ScriptableObject newAsset = ScriptableObject
                .CreateInstance(persisterType);

            newAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
        }
        #endregion

        #region Private Methods — Asset Management
        private static ChronoConfiguration GetOrCreateSettings()
        {
            ChronoConfiguration settings = AssetDatabase
                .LoadAssetAtPath<ChronoConfiguration>(SettingsAssetPath);

            if (settings != null)
            {
                return settings;
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Chrono Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info
            );

            if (GUILayout.Button(
                    "Create Chrono Configuration",
                    GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourcesFolder))
            {
                Directory.CreateDirectory(ResourcesFolder);
            }

            ChronoConfiguration settings = ScriptableObject
                .CreateInstance<ChronoConfiguration>();

            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[ChronoDirector] Created settings asset at '"
                + SettingsAssetPath + "'."
            );
        }
        #endregion

        #region Private Methods — Asset Operations
        private void CreateAndAssignProviderAsset<T>(
            SerializedProperty prop,
            string defaultName
        ) where T : ScriptableObject
        {
            EnsureChronoAssetFolder();

            string defaultPath = ChronoAssetFolder + "/" + defaultName + ".asset";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create " + typeof(T).Name,
                defaultName,
                "asset",
                "Choose a location for the new provider asset.",
                ChronoAssetFolder
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            prop.objectReferenceValue = asset;
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_serializedSettings.targetObject);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(asset);
        }

        private void DeleteAssetAndClearField(
            ScriptableObject asset,
            SerializedProperty prop
        )
        {
            if (asset == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                $"Delete '{asset.name}' at '{assetPath}'?\n\n"
                + "This cannot be undone.",
                "Delete",
                "Cancel"
            );

            if (!confirmed)
            {
                return;
            }

            prop.objectReferenceValue = null;
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_serializedSettings.targetObject);

            if (!AssetDatabase.DeleteAsset(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "Delete Failed",
                    $"Unity could not delete '{assetPath}'.",
                    "OK"
                );
                return;
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureChronoAssetFolder()
        {
            if (!Directory.Exists(ChronoAssetFolder))
            {
                Directory.CreateDirectory(ChronoAssetFolder);
            }
        }
        #endregion

        #region Private Methods — Scripting Defines
        private static bool IsDefineEnabled(string define)
        {
            return GetCurrentDefines().Contains(define);
        }

        private static void EnableDefine(string define)
        {
            HashSet<string> defines = GetCurrentDefines();
            if (defines.Contains(define))
            {
                return;
            }

            defines.Add(define);
            ApplyScriptingDefines(defines);

            Debug.Log(
                "[ChronoDirector] Enabled scripting define: " + define
                + ". Unity will recompile."
            );
        }

        private static HashSet<string> GetCurrentDefines()
        {
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );

            string definesString = PlayerSettings
                .GetScriptingDefineSymbols(target);

            return new HashSet<string>(
                definesString.Split(
                    new[] { ';' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            );
        }

        private static void ApplyScriptingDefines(
            HashSet<string> defines)
        {
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup
            );

            PlayerSettings.SetScriptingDefineSymbols(
                target,
                string.Join(";", defines)
            );
        }
        #endregion

        #region Private Methods — Drawing Utilities
        private void EnsureSerializedObject(ChronoConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
            {
                return;
            }

            DestroyTimeProviderEditor();
            _serializedSettings = new SerializedObject(settings);
        }

        private void DrawSerializedProperty(
            string propertyName,
            string label)
        {
            SerializedProperty prop = _serializedSettings
                .FindProperty(propertyName);

            if (prop == null)
            {
                EditorGUILayout.LabelField(
                    label,
                    $"<missing property: {propertyName}>"
                );
                return;
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        private static void DrawInlineInspector(
            ref UnityEditor.Editor cachedEditor,
            ref ScriptableObject cachedTarget,
            ScriptableObject asset,
            string header)
        {
            if (asset == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!string.IsNullOrEmpty(header))
                {
                    EditorGUILayout.LabelField(
                        header,
                        EditorStyles.miniBoldLabel
                    );
                }

                if (cachedEditor == null || cachedTarget != asset)
                {
                    if (cachedEditor != null)
                    {
                        UnityEngine.Object.DestroyImmediate(cachedEditor);
                    }

                    cachedEditor = UnityEditor.Editor.CreateEditor(asset);
                    cachedTarget = asset;
                }

                if (cachedEditor != null)
                {
                    cachedEditor.OnInspectorGUI();
                }
            }
        }

        private static void DrawInlineInspectorForPersister(
            ScriptableObject persister)
        {
            SerializedObject so = new SerializedObject(persister);
            so.Update();

            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(prop, true);
            }

            so.ApplyModifiedProperties();
        }

        private static bool DrawIconButton(
            string iconName,
            string fallbackText,
            string tooltip)
        {
            var content = EditorGUIUtility.IconContent(iconName);
            if (content == null || content.image == null)
            {
                content = new GUIContent(fallbackText, tooltip);
            }
            else
            {
                content.tooltip = tooltip;
            }

            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(24),
                GUILayout.Height(18)
            );
        }

        private static void DrawCreateCustomButton(
            string label,
            Type targetType,
            ScriptTemplateGenerator.GenerationMode mode)
        {
            GUILayout.Space(8);

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));

            GUILayout.Space(4);

            if (GUILayout.Button(label, GUILayout.Height(25)))
            {
                ScriptTemplateGenerator.CreatePluginScript(
                    null,
                    "Assets/",
                    targetType,
                    mode);
            }
        }

        private void DestroyTimeProviderEditor()
        {
            if (_timeProviderEditor != null)
            {
                UnityEngine.Object.DestroyImmediate(_timeProviderEditor);
                _timeProviderEditor = null;
                _timeProviderEditorTarget = null;
            }
        }
        #endregion
    }
}
