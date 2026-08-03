using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.UserIdentity;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity.Editor
{
    internal sealed class UserIdentitySettingsProvider : SettingsProvider
    {
        #region Constants

        private const string ProviderAssetFolder = "Assets/Resources/Integration";

        private const string PlatformTabPrefKey =
            "UserIdentitySettingsProvider_PlatformTab";

        private static readonly string[] PlatformFieldNames =
            { "_androidProviders", "_iosProviders" };

        private static readonly string[] PlatformNames =
            { "Android", "iOS" };

        #endregion

        #region Private Fields

        private SerializedObject _serializedSettings;
        private int _selectedPlatformIndex;
        private Vector2 _scrollPosition;

        private Dictionary<ScriptableObject, UnityEditor.Editor>
            _inlineEditorCache;

        private static Type[] _cachedProviderTypes;

        private GUIContent[] _platformTabContents;

        #endregion

        #region Constructor

        private UserIdentitySettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _selectedPlatformIndex = EditorPrefs.GetInt(PlatformTabPrefKey, 0);

            _platformTabContents = new[]
            {
                EditorGUIUtility.IconContent("BuildSettings.Android"),
                EditorGUIUtility.IconContent("BuildSettings.iPhone"),
            };

            _platformTabContents[0].text = " Android";
            _platformTabContents[1].text = " iOS";
        }

        #endregion

        #region SettingsProvider Registration

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UserIdentitySettingsProvider(
                "Project/Frameworks/User Identity",
                SettingsScope.Project,
                new[]
                {
                    "identity", "user", "login", "account",
                    "authentication", "profile"
                }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            UserIdentityConfiguration settings = GetOrCreateSettings();
            if (settings == null) return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            EditorGUILayout.Space();
            DrawStatusBar(settings);
            EditorGUILayout.Space();

            int newPlatformTab = GUILayout.Toolbar(
                _selectedPlatformIndex, _platformTabContents, GUILayout.Height(24));
            if (newPlatformTab != _selectedPlatformIndex)
            {
                _selectedPlatformIndex = newPlatformTab;
                EditorPrefs.SetInt(PlatformTabPrefKey, _selectedPlatformIndex);
            }
            EditorGUILayout.Space();

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(dividerRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawProvidersTab();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            DrawGeneralFields();

            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            EditorPrefs.SetInt(PlatformTabPrefKey, _selectedPlatformIndex);
            DestroyInlineEditors();
            _cachedProviderTypes = null;
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar(UserIdentityConfiguration config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int providerCount = config.HasAnyProvider
                    ? config.Providers.Count
                    : 0;
                bool valid = providerCount > 0;

                Color statusColor = valid ? Color.green : Color.yellow;
                string statusText = valid ? "Ready" : "Incomplete";

                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor }
                };

                EditorGUILayout.LabelField(
                    $"Status: {statusText}  |  "
                    + $"{providerCount} provider(s) configured "
                    + $"(anonymous fallback is implicit)",
                    style
                );
            }
        }

        #endregion

        #region Tab — Providers

        private void DrawProvidersTab()
        {
            EditorGUILayout.HelpBox(
                "Providers are configured per platform. List order is the "
                + "priority — index 0 ranks highest, reorder with the "
                + "up/down buttons. An anonymous provider is implied at "
                + "the bottom when none is configured.",
                MessageType.None);
            EditorGUILayout.Space();

            SerializedProperty listProp =
                _serializedSettings.FindProperty(PlatformFieldNames[_selectedPlatformIndex]);

            if (listProp == null || !listProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find '{PlatformFieldNames[_selectedPlatformIndex]}' field.",
                    MessageType.Error);
                return;
            }

            if (listProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No providers configured for {PlatformNames[_selectedPlatformIndex]}. "
                    + "Enable one below.",
                    MessageType.Warning);
            }

            Type[] providerTypes = GetCachedProviderTypes(typeof(IIdentityProvider));
            if (providerTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing IIdentityProvider found.",
                    MessageType.Info);
                return;
            }

            var configured = new List<ScriptableObject>();
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty element = listProp.GetArrayElementAtIndex(i);
                var provider = element.objectReferenceValue as ScriptableObject;
                if (provider == null) continue;
                configured.Add(provider);

                DrawConfiguredProviderCard(listProp, i, provider);
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);

            foreach (Type providerType in providerTypes)
            {
                bool isConfigured = configured.Any(asset =>
                    providerType.IsAssignableFrom(asset.GetType()));

                if (isConfigured) continue;

                DrawEnableCard(providerType, listProp);
                EditorGUILayout.Space(2);
            }
        }

        private void DrawConfiguredProviderCard(
            SerializedProperty listProp,
            int index,
            ScriptableObject provider)
        {
            bool isAnonymous = provider is UserAnonymousIdentityProvider;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        ObjectNames.NicifyVariableName(provider.GetType().Name),
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    string badge = isAnonymous ? "Anonymous" : "Login Required";
                    Color badgeColor = isAnonymous
                        ? new Color(0.25f, 0.45f, 0.65f, 0.8f)
                        : new Color(0.7f, 0.5f, 0.15f, 0.8f);

                    GUI.backgroundColor = badgeColor;
                    GUILayout.Label(
                        badge, EditorStyles.miniButton,
                        GUILayout.Width(110));
                    GUI.backgroundColor = Color.white;

                    using (new EditorGUI.DisabledScope(index == 0))
                    {
                        if (GUILayout.Button(
                                "\u25B2", EditorStyles.miniButtonLeft,
                                GUILayout.Width(22), GUILayout.Height(18)))
                        {
                            listProp.MoveArrayElement(index, index - 1);
                        }
                    }

                    using (new EditorGUI.DisabledScope(
                               index >= listProp.arraySize - 1))
                    {
                        if (GUILayout.Button(
                                "\u25BC", EditorStyles.miniButtonRight,
                                GUILayout.Width(22), GUILayout.Height(18)))
                        {
                            listProp.MoveArrayElement(index, index + 1);
                        }
                    }

                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button(
                            "\u2717", EditorStyles.miniButton,
                            GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        listProp.DeleteArrayElementAtIndex(index);
                        CleanNullArrayEntries(listProp);
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.Space(4);
                DrawInlineInspectorFor(provider);
            }
        }

        private void DrawEnableCard(
            Type providerType,
            SerializedProperty listProp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        ObjectNames.NicifyVariableName(providerType.Name),
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Enable", GUILayout.Width(70)))
                    {
                        ScriptableObject asset =
                            FindOrCreateProviderAsset(providerType);
                        AddToProviderList(listProp, asset);
                        EditorGUIUtility.PingObject(asset);
                    }
                }
            }
        }

        #endregion

        #region General Fields

        private void DrawGeneralFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "General", EditorStyles.boldLabel);
                GUILayout.Space(2);

                SerializedProperty deviceNameProp =
                    _serializedSettings.FindProperty("_deviceDisplayName");
                if (deviceNameProp != null)
                {
                    EditorGUILayout.PropertyField(
                        deviceNameProp,
                        new GUIContent("Anonymous Display Name"));
                }

                SerializedProperty autoLinkProp =
                    _serializedSettings.FindProperty("_autoLinkAuthenticatedOnInit");
                if (autoLinkProp != null)
                {
                    EditorGUILayout.PropertyField(
                        autoLinkProp,
                        new GUIContent("Auto-Link Authenticated on Init"));
                }
            }
        }

        #endregion

        #region Provider Helpers

        private static Type[] GetCachedProviderTypes(Type interfaceType)
        {
            _cachedProviderTypes ??= ScanProviderTypes(interfaceType);
            return _cachedProviderTypes;
        }

        private static Type[] ScanProviderTypes(Type interfaceType)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(type =>
                    type.IsClass && !type.IsAbstract &&
                    typeof(ScriptableObject).IsAssignableFrom(type) &&
                    interfaceType.IsAssignableFrom(type))
                .OrderBy(type => type.FullName)
                .ToArray();
        }

        private ScriptableObject FindOrCreateProviderAsset(Type providerType)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + providerType.Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(
                    path, providerType) as ScriptableObject;
                if (asset != null) return asset;
            }

            var newAsset = ScriptableObject.CreateInstance(providerType);
            newAsset.name = providerType.Name;

            if (!Directory.Exists(ProviderAssetFolder))
            {
                Directory.CreateDirectory(ProviderAssetFolder);
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(ProviderAssetFolder, providerType.Name + ".asset"));
            AssetDatabase.CreateAsset(newAsset, assetPath);
            AssetDatabase.SaveAssets();

            return newAsset;
        }

        private void AddToProviderList(
            SerializedProperty listProp,
            ScriptableObject asset)
        {
            listProp.arraySize++;
            SerializedProperty element =
                listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            element.objectReferenceValue = asset;
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_serializedSettings.targetObject);
            AssetDatabase.SaveAssets();
            DestroyInlineEditors();
        }

        private static void CleanNullArrayEntries(SerializedProperty arrayProp)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                if (arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue == null)
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                }
            }
        }

        #endregion

        #region Asset Helpers

        private static UserIdentityConfiguration GetOrCreateSettings()
        {
            var settings = UserIdentityConfiguration.Instance;
            if (settings != null) return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No User Identity Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button(
                    "Create User Identity Configuration",
                    GUILayout.Height(30)))
            {
                UserIdentityConfiguration.CreateOrMoveToDesignatedPath();
            }

            return null;
        }

        private void EnsureSerializedObject(
            UserIdentityConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
                return;

            DestroyInlineEditors();
            _serializedSettings = new SerializedObject(settings);
        }

        #endregion

        #region Inline Inspector

        private void DrawInlineInspectorFor(ScriptableObject target)
        {
            if (target == null) return;

            _inlineEditorCache ??=
                new Dictionary<ScriptableObject, UnityEditor.Editor>();

            if (!_inlineEditorCache.TryGetValue(
                    target, out UnityEditor.Editor editor))
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                _inlineEditorCache[target] = editor;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                editor.OnInspectorGUI();
            }
        }

        private void DestroyInlineEditors()
        {
            if (_inlineEditorCache == null) return;

            foreach (var editor in _inlineEditorCache.Values)
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);
            }

            _inlineEditorCache.Clear();
        }

        #endregion
    }
}
