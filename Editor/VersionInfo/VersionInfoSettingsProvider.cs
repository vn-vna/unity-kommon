using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.NoBuild.Editor;
using Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration;
using Com.Hapiga.Scheherazade.Common.VIC;
using Com.Hapiga.Scheherazade.Common.VIC.Consumers;
using Com.Hapiga.Scheherazade.Common.VIC.Providers;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Editor
{
    internal sealed class VersionInfoSettingsProvider : SettingsProvider
    {
        #region Constants
        private const string SettingsAssetPath =
            "Assets/Resources/VersionInfoConfiguration.asset";

        private const string ResourcesFolder = "Assets/Resources";

        private const string AssetFolder = "Assets/Resources/VersionInfo";

        private static readonly string[] TabNames =
            { "Configuration", "Provider", "Consumers", "Placeholders" };

        private static readonly (string key, string description)[] Placeholders =
        {
            ("{app-version}",     "Application.version"),
            ("{app-name}",        "Application.productName"),
            ("{app-bundle}",      "Platform bundle version (bundleVersionCode / buildNumber)"),
            ("{build-type}",      "dev (editor, dev builds) / prod (PRODUCTION_BUILD)"),
            ("{date}",            "yyyy-MM-dd (UTC). Custom: {date:yyMMdd}"),
            ("{time}",            "HHmmss (UTC). Custom: {time:HHmm}"),
            ("{datetime}",        "yyyy-MM-dd_HHmmss (UTC). Custom: {datetime:yyMMdd-HHmm}"),
            ("{project-name}",    "Project folder name"),
            ("{git-branch}",      "Current git branch"),
            ("{git-commit}",      "Short commit hash (7 chars)"),
            ("{git-commit-full}", "Full commit hash (40 chars)"),
            ("{platform}",        "Build target platform"),
            ("{flag-id}",         "NoBuild flag by id or index, e.g. {flag-debug}"),
        };
        #endregion

        #region Private Fields
        private SerializedObject _serializedSettings;

        private int _selectedTabIndex;

        private Vector2 _scrollPosition;

        private UnityEditor.Editor _providerEditor;

        private ScriptableObject _providerEditorTarget;

        private readonly Dictionary<Type, UnityEditor.Editor> _consumerEditors =
            new Dictionary<Type, UnityEditor.Editor>();

        private readonly Dictionary<Type, ScriptableObject> _consumerEditorTargets =
            new Dictionary<Type, ScriptableObject>();
        #endregion

        #region Constructor
        private VersionInfoSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }
        #endregion

        #region SettingsProvider Registration
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new VersionInfoSettingsProvider(
                "Project/Tools/Version Info",
                SettingsScope.Project,
                new[]
                {
                    "version", "info", "display", "canvas",
                    "build", "provider", "consumer", "pattern"
                }
            );
        }
        #endregion

        #region GUI
        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            VersionInfoConfiguration settings = GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            GUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Version Info Settings",
                EditorStyles.boldLabel
            );

            GUILayout.Space(4);
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
                    DrawConfigurationTab(settings);
                    break;
                case 1:
                    DrawProviderTab();
                    break;
                case 2:
                    DrawConsumersTab();
                    break;
                case 3:
                    DrawPlaceholdersTab(settings);
                    break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            DestroyProviderEditor();
            DestroyAllConsumerEditors();
        }
        #endregion

        #region Tab — Configuration
        private void DrawConfigurationTab(VersionInfoConfiguration settings)
        {
            EditorGUILayout.HelpBox(
                "Configure the version string pattern used at build time. "
                + "The pattern is resolved when a build starts and the result "
                + "is written to the Resources text file.",
                MessageType.None
            );

            GUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Version Pattern",
                    EditorStyles.miniBoldLabel
                );

                SerializedProperty patternProp = _serializedSettings
                    .FindProperty("_versionPattern");
                if (patternProp != null)
                {
                    EditorGUILayout.PropertyField(patternProp);
                }
            }

            GUILayout.Space(4);

            DrawPlaceholderReference();

            GUILayout.Space(4);

            DrawPatternPreview(settings);
        }

        private static void DrawPlaceholderReference()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Available Placeholders",
                    EditorStyles.miniBoldLabel
                );

                EditorGUI.indentLevel++;
                foreach ((string key, string desc) in Placeholders)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            key,
                            EditorStyles.miniBoldLabel,
                            GUILayout.Width(140)
                        );
                        EditorGUILayout.LabelField(
                            desc,
                            EditorStyles.miniLabel
                        );
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        private static void DrawPatternPreview(VersionInfoConfiguration settings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Preview",
                    EditorStyles.miniBoldLabel
                );

                string pattern = settings.VersionPattern;
                if (string.IsNullOrEmpty(pattern))
                {
                    EditorGUILayout.LabelField(
                        "Pattern is empty.",
                        EditorStyles.miniLabel
                    );
                    return;
                }

                string resolved = VersionNameResolver.Resolve(
                    pattern,
                    GetConfiguredProviders(settings));
                EditorGUILayout.LabelField(resolved, EditorStyles.wordWrappedLabel);

                GUILayout.Space(2);
                if (GUILayout.Button("Copy Resolved", GUILayout.Width(120)))
                {
                    EditorGUIUtility.systemCopyBuffer = resolved;
                }
            }
        }
        #endregion

        #region Tab — Provider
        private void DrawProviderTab()
        {
            EditorGUILayout.HelpBox(
                "Select how the version string is provided. "
                + "Only one provider can be active at a time. "
                + "If none is enabled, Application.version is used as fallback.",
                MessageType.None
            );

            GUILayout.Space(4);

            SerializedProperty providerProp = _serializedSettings
                .FindProperty("_provider");

            if (providerProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_provider' field.",
                    MessageType.Error
                );
                return;
            }

            ScriptableObject currentProvider = providerProp
                .objectReferenceValue as ScriptableObject;

            Type currentType = currentProvider?.GetType();

            DrawProviderRow(
                "Resource Text Asset Provider",
                typeof(ResourceTextAssetProvider),
                currentType == typeof(ResourceTextAssetProvider),
                currentProvider,
                providerProp
            );

            GUILayout.Space(4);

            DrawProviderRow(
                "Application Version Provider",
                typeof(ApplicationVersionProvider),
                currentType == typeof(ApplicationVersionProvider),
                currentProvider,
                providerProp
            );

            DrawCreateCustomButton(
                "New Custom Provider",
                typeof(IVersionInfoProvider));
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
                            DestroyProviderEditor();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset = FindOrCreateAsset(
                                providerType,
                                providerType.Name
                            );

                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            DestroyProviderEditor();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

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
                            DestroyProviderEditor();
                        }
                    }

                    GUILayout.Space(2);

                    DrawInlineInspector(
                        ref _providerEditor,
                        ref _providerEditorTarget,
                        currentProvider,
                        null
                    );
                }
            }
        }
        #endregion

        #region Tab — Consumers
        private void DrawConsumersTab()
        {
            EditorGUILayout.HelpBox(
                "Enable or disable version info consumers. "
                + "Each consumer type can be active at most once. "
                + "Multiple consumer types can be active simultaneously.",
                MessageType.None
            );

            GUILayout.Space(4);

            SerializedProperty consumersProp = _serializedSettings
                .FindProperty("_consumers");

            if (consumersProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_consumers' field.",
                    MessageType.Error
                );
                return;
            }

            DrawConsumerRow(
                "Default Canvas Consumer",
                typeof(DefaultCanvasConsumer),
                consumersProp
            );

            GUILayout.Space(4);

            DrawConsumerRow(
                "Prefab Consumer",
                typeof(PrefabVersionInfoConsumer),
                consumersProp
            );

            DrawCreateCustomButton(
                "New Custom Consumer",
                typeof(IVersionInfoConsumer));
        }

        private void DrawConsumerRow(
            string displayName,
            Type consumerType,
            SerializedProperty listProp
        )
        {
            int existingIndex = FindConsumerIndex(consumerType, listProp);
            bool isActive = existingIndex >= 0;

            SerializedProperty elementProp = isActive
                ? listProp.GetArrayElementAtIndex(existingIndex)
                : null;

            ScriptableObject consumer = elementProp
                ?.objectReferenceValue as ScriptableObject;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        displayName,
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    if (isActive && consumer != null)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            DestroyConsumerEditor(consumerType);
                            listProp.DeleteArrayElementAtIndex(existingIndex);
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset = FindOrCreateAsset(
                                consumerType,
                                consumerType.Name
                            );

                            int newIndex = listProp.arraySize;
                            listProp.InsertArrayElementAtIndex(newIndex);
                            SerializedProperty newElement = listProp
                                .GetArrayElementAtIndex(newIndex);
                            newElement.objectReferenceValue = asset;

                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && consumer != null)
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
                                "Delete the consumer asset from disk."))
                        {
                            DeleteAssetAndRemoveFromList(
                                consumer,
                                listProp,
                                existingIndex
                            );
                            DestroyConsumerEditor(consumerType);
                        }
                    }

                    GUILayout.Space(2);

                    DrawConsumerInlineInspector(consumerType, consumer);
                }
            }
        }

        private static int FindConsumerIndex(
            Type consumerType,
            SerializedProperty listProp)
        {
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty element = listProp.GetArrayElementAtIndex(i);
                ScriptableObject obj = element.objectReferenceValue
                    as ScriptableObject;

                if (obj != null && obj.GetType() == consumerType)
                {
                    return i;
                }
            }

            return -1;
        }

        private void DrawConsumerInlineInspector(
            Type consumerType,
            ScriptableObject asset)
        {
            if (asset == null)
            {
                return;
            }

            _consumerEditors.TryGetValue(
                consumerType, out UnityEditor.Editor editor);
            _consumerEditorTargets.TryGetValue(
                consumerType, out ScriptableObject target);

            if (editor == null || target != asset)
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }

                editor = UnityEditor.Editor.CreateEditor(asset);
                target = asset;

                _consumerEditors[consumerType] = editor;
                _consumerEditorTargets[consumerType] = target;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (editor != null)
                {
                    editor.OnInspectorGUI();
                }
            }
        }
        #endregion

        #region Tab — Placeholders
        private List<Type> _cachedPlaceholderProviderTypes;

        private void DrawPlaceholdersTab(VersionInfoConfiguration settings)
        {
            EditorGUILayout.HelpBox(
                "Configure custom placeholder providers. "
                + "Custom providers are checked before built-in ones "
                + "and can override any placeholder key. "
                + "Key-Value Provider lets you define simple key→value mappings; "
                + "for complex logic, implement "
                + "IVersionNamePlaceholderProvider on a ScriptableObject.",
                MessageType.None
            );

            GUILayout.Space(4);

            SerializedProperty providersProp = _serializedSettings
                .FindProperty("_placeholderProviders");

            if (providersProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_placeholderProviders' field.",
                    MessageType.Error
                );
                return;
            }

            List<Type> providerTypes = GetPlaceholderProviderTypes();

            if (providerTypes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No IVersionNamePlaceholderProvider implementations found.",
                    MessageType.Info
                );
                return;
            }

            foreach (Type type in providerTypes)
            {
                int existingIndex = FindProviderIndex(type, providersProp);
                bool isActive = existingIndex >= 0;
                ScriptableObject provider = isActive
                    ? providersProp.GetArrayElementAtIndex(existingIndex)
                        .objectReferenceValue as ScriptableObject
                    : null;

                DrawPlaceholderProviderRow(
                    type.Name,
                    type,
                    isActive,
                    provider,
                    providersProp,
                    existingIndex
                );
            }

            DrawCreateCustomButton(
                "New Custom Placeholder Provider",
                typeof(IVersionNamePlaceholderProvider));
        }

        private List<Type> GetPlaceholderProviderTypes()
        {
            if (_cachedPlaceholderProviderTypes != null)
            {
                return _cachedPlaceholderProviderTypes;
            }

            _cachedPlaceholderProviderTypes = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t =>
                    !t.IsAbstract
                    && !t.IsInterface
                    && !t.IsGenericTypeDefinition
                    && typeof(IVersionNamePlaceholderProvider).IsAssignableFrom(t)
                    && typeof(ScriptableObject).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();

            return _cachedPlaceholderProviderTypes;
        }

        private void DrawPlaceholderProviderRow(
            string displayName,
            Type providerType,
            bool isActive,
            ScriptableObject provider,
            SerializedProperty listProp,
            int existingIndex)
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

                    if (isActive && provider != null)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            listProp.DeleteArrayElementAtIndex(existingIndex);
                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset = FindOrCreateAsset(
                                providerType,
                                providerType.Name
                            );

                            int newIndex = listProp.arraySize;
                            listProp.InsertArrayElementAtIndex(newIndex);
                            SerializedProperty newElement = listProp
                                .GetArrayElementAtIndex(newIndex);
                            newElement.objectReferenceValue = asset;

                            _serializedSettings.ApplyModifiedProperties();
                            AssetDatabase.SaveAssets();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && provider != null)
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
                            DeleteAssetAndRemoveFromList(
                                provider,
                                listProp,
                                existingIndex
                            );
                        }
                    }

                    GUILayout.Space(2);
                    DrawInlineEditorForAsset(provider);
                }
            }
        }

        private static int FindProviderIndex(
            Type providerType,
            SerializedProperty listProp)
        {
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty element = listProp.GetArrayElementAtIndex(i);
                ScriptableObject obj = element.objectReferenceValue
                    as ScriptableObject;

                if (obj != null && obj.GetType() == providerType)
                {
                    return i;
                }
            }

            return -1;
        }

        private void DrawInlineEditorForAsset(ScriptableObject asset)
        {
            if (asset == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedObject so = new SerializedObject(asset);
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
        }

        private static IEnumerable<IVersionNamePlaceholderProvider>
            GetConfiguredProviders(VersionInfoConfiguration config)
        {
            if (config?.PlaceholderProviderAssets == null)
            {
                yield break;
            }

            foreach (ScriptableObject asset in config.PlaceholderProviderAssets)
            {
                if (asset is IVersionNamePlaceholderProvider provider)
                {
                    yield return provider;
                }
            }
        }
        #endregion

        #region Private Methods — Asset Management
        private static VersionInfoConfiguration GetOrCreateSettings()
        {
            VersionInfoConfiguration settings = AssetDatabase
                .LoadAssetAtPath<VersionInfoConfiguration>(SettingsAssetPath);

            if (settings != null)
            {
                return settings;
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Version Info Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info
            );

            if (GUILayout.Button(
                    "Create Version Info Configuration",
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

            VersionInfoConfiguration settings = ScriptableObject
                .CreateInstance<VersionInfoConfiguration>();

            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[VersionInfo] Created settings asset at '"
                + SettingsAssetPath + "'."
            );
        }

        private static ScriptableObject FindOrCreateAsset(
            Type assetType,
            string defaultName)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:" + assetType.Name
            );

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                    path,
                    assetType
                ) as ScriptableObject;

                if (asset != null)
                {
                    return asset;
                }
            }

            EnsureAssetFolder();

            string assetPath = AssetFolder + "/" + defaultName + ".asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            ScriptableObject newAsset = ScriptableObject
                .CreateInstance(assetType);

            newAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
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
                "Delete Asset",
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

        private void DeleteAssetAndRemoveFromList(
            ScriptableObject asset,
            SerializedProperty listProp,
            int index
        )
        {
            if (asset == null || index < 0)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Asset",
                $"Delete '{asset.name}' at '{assetPath}'?\n\n"
                + "This cannot be undone.",
                "Delete",
                "Cancel"
            );

            if (!confirmed)
            {
                return;
            }

            listProp.DeleteArrayElementAtIndex(index);
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

        private static void EnsureAssetFolder()
        {
            if (!Directory.Exists(AssetFolder))
            {
                Directory.CreateDirectory(AssetFolder);
            }
        }
        #endregion

        #region Private Methods — Drawing Utilities
        private void EnsureSerializedObject(VersionInfoConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
            {
                return;
            }

            DestroyProviderEditor();
            DestroyAllConsumerEditors();
            _serializedSettings = new SerializedObject(settings);
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

        private static bool DrawIconButton(
            string iconName,
            string fallbackText,
            string tooltip)
        {
            GUIContent content = EditorGUIUtility.IconContent(iconName);
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
        #endregion

        #region Private Methods — Editor Lifecycle
        private static void DrawCreateCustomButton(string label, Type targetType)
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
                    ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
            }
        }

        private void DestroyProviderEditor()
        {
            if (_providerEditor != null)
            {
                UnityEngine.Object.DestroyImmediate(_providerEditor);
                _providerEditor = null;
                _providerEditorTarget = null;
            }
        }

        private void DestroyConsumerEditor(Type consumerType)
        {
            if (_consumerEditors.TryGetValue(
                    consumerType,
                    out UnityEditor.Editor editor))
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }

                _consumerEditors.Remove(consumerType);
                _consumerEditorTargets.Remove(consumerType);
            }
        }

        private void DestroyAllConsumerEditors()
        {
            foreach (UnityEditor.Editor editor in _consumerEditors.Values)
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }
            }

            _consumerEditors.Clear();
            _consumerEditorTargets.Clear();
        }
        #endregion

        #region Nested Types
        /// <summary>
        /// Resolves version name placeholders following the BuildNameResolver pattern.
        /// Supports custom DateTime formats (e.g. {datetime:yyMMdd}) and
        /// NoBuild flag integration (e.g. {flag-debug}).
        /// </summary>
        internal static class VersionNameResolver
        {
            /// <summary>
            /// Register custom placeholder providers here.
            /// Checked before built-in resolvers — can override any key.
            /// </summary>
            public static readonly List<IVersionNamePlaceholderProvider>
                CustomProviders = new List<IVersionNamePlaceholderProvider>();

            private static readonly Regex PlaceholderRegex =
                new(@"\{([^{}]+)\}", RegexOptions.Compiled);

            private static NoBuildSettings _cachedNoBuildSettings;

            private static bool _noBuildSettingsSearched;

            public static string Resolve(string template)
            {
                return Resolve(template, null);
            }

            public static string Resolve(
                string template,
                IEnumerable<IVersionNamePlaceholderProvider> extraProviders)
            {
                if (string.IsNullOrEmpty(template))
                {
                    return "unknown";
                }

                return PlaceholderRegex.Replace(template, match =>
                {
                    string raw = match.Groups[1].Value.Trim();
                    string key;
                    string format;

                    int colonIndex = raw.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        key = raw.Substring(0, colonIndex).ToLowerInvariant();
                        format = raw.Substring(colonIndex + 1);
                    }
                    else
                    {
                        key = raw.ToLowerInvariant();
                        format = null;
                    }

                    return TryResolve(
                        key, format, raw, match.Value, extraProviders);
                });
            }

            private static string TryResolve(
                string key,
                string format,
                string raw,
                string fallback,
                IEnumerable<IVersionNamePlaceholderProvider> extraProviders)
            {
                // 1. Extra providers (from config) take top priority
                if (extraProviders != null)
                {
                    foreach (IVersionNamePlaceholderProvider provider
                        in extraProviders)
                    {
                        if (provider.TryResolve(key, format, out string value))
                        {
                            return value;
                        }
                    }
                }

                // 2. Static custom providers
                foreach (IVersionNamePlaceholderProvider provider
                    in CustomProviders)
                {
                    if (provider.TryResolve(key, format, out string value))
                    {
                        return value;
                    }
                }

                // 3. Built-in resolvers
                return ResolveBuiltIn(key, format, raw, fallback);
            }

            private static string ResolveBuiltIn(
                string key, string format, string raw, string fallback)
            {
                switch (key)
                {
                    case "app-version":
                        return Application.version;
                    case "app-name":
                        return Application.productName;
                    case "app-bundle":
                        return ResolveAppBundle();
                    case "build-type":
                        return ResolveBuildType();
                    case "date":
                        return DateTime.UtcNow.ToString(
                            format ?? "yyyy-MM-dd");
                    case "time":
                        return DateTime.UtcNow.ToString(
                            format ?? "HHmmss");
                    case "datetime":
                        return DateTime.UtcNow.ToString(
                            format ?? "yyyy-MM-dd_HHmmss");
                    case "project-name":
                        return ResolveProjectName();
                    case "git-branch":
                        return GitUtility.BranchName;
                    case "git-commit":
                        return GitUtility.ShortCommitHash;
                    case "git-commit-full":
                        return GitUtility.FullCommitHash;
                    case "platform":
                        return EditorUserBuildSettings.activeBuildTarget
                            .ToString();
                    default:
                        return ResolveDynamicKey(key, raw, fallback);
                }
            }

            private static string ResolveDynamicKey(
                string key, string raw, string fallback)
            {
                if (key.StartsWith("flag-") && key.Length > 5)
                {
                    string flagId = key.Substring(5);
                    return ResolveFlag(flagId);
                }

                return fallback;
            }

            private static string ResolveFlag(string flagId)
            {
                NoBuildSettings settings = GetNoBuildSettings();
                if (settings?.flagDefinitions == null)
                {
                    return "";
                }

                FlagDefinition flag = FindFlagById(flagId, settings);
                if (flag == null)
                {
                    return "";
                }

                bool isActive = IsFlagActive(flag, settings);
                return isActive
                    ? flag.trueFlag ?? ""
                    : flag.falseFlag ?? "";
            }

            private static FlagDefinition FindFlagById(
                string id, NoBuildSettings settings)
            {
                if (int.TryParse(id, out int index)
                    && index >= 0
                    && index < settings.flagDefinitions.Count)
                {
                    return settings.flagDefinitions[index];
                }

                foreach (FlagDefinition flag in settings.flagDefinitions)
                {
                    if (string.Equals(
                            flag.id,
                            id,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return flag;
                    }
                }

                return null;
            }

            private static bool IsFlagActive(
                FlagDefinition flag, NoBuildSettings settings)
            {
                HashSet<string> currentDefines =
                    ScriptDefinitionSwitcher.GetCurrentDefines();

                if (flag.type == FlagDefinitionType.Template)
                {
                    if (flag.scriptDefinitionSetIndex >= 0
                        && flag.scriptDefinitionSetIndex
                        < settings.scriptDefinitionSets.Count)
                    {
                        ScriptDefinitionSet set = settings
                            .scriptDefinitionSets[
                                flag.scriptDefinitionSetIndex];
                        if (set.slots != null
                            && flag.scriptDefinitionSlotIndex >= 0
                            && flag.scriptDefinitionSlotIndex
                            < set.slots.Count)
                        {
                            ScriptDefinitionSlot slot =
                                set.slots[flag.scriptDefinitionSlotIndex];
                            return slot.enabled
                                && !string.IsNullOrEmpty(
                                    slot.defineSymbol)
                                && currentDefines.Contains(
                                    slot.defineSymbol);
                        }
                    }
                }
                else
                {
                    return !string.IsNullOrEmpty(
                        flag.customDefineSymbol)
                        && currentDefines.Contains(
                            flag.customDefineSymbol);
                }

                return false;
            }

            private static NoBuildSettings GetNoBuildSettings()
            {
                if (_cachedNoBuildSettings != null
                    || _noBuildSettingsSearched)
                {
                    return _cachedNoBuildSettings;
                }

                _noBuildSettingsSearched = true;
                _cachedNoBuildSettings = AssetDatabase
                    .LoadAssetAtPath<NoBuildSettings>(
                        NoBuildSettings.AssetPath);
                return _cachedNoBuildSettings;
            }

            private static string ResolveBuildType()
            {
#if PRODUCTION_BUILD
                return "prod";
#else
                return "dev";
#endif
            }

            private static string ResolveAppBundle()
            {
#if UNITY_ANDROID
                return PlayerSettings.Android.bundleVersionCode.ToString();
#elif UNITY_IOS
                return PlayerSettings.iOS.buildNumber;
#else
                return PlayerSettings.bundleVersion;
#endif
            }

            private static string ResolveProjectName()
            {
                string projectPath = Path.GetDirectoryName(
                    Application.dataPath);
                if (string.IsNullOrEmpty(projectPath))
                {
                    return "unknown";
                }

                return Path.GetFileName(projectPath);
            }
        }
        #endregion
    }
}
