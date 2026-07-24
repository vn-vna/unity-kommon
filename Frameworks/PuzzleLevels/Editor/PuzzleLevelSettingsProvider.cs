using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor
{
    public class PuzzleLevelSettingsProvider : SettingsProvider
    {
        private const string ConfigAssetPath =
            "Assets/Resources/AsyncResourceLoaderConfiguration.asset";
        private const string ManagerDefaultFolder =
            "Assets/Resources";
        private const string ManagerAssetPath =
            "Assets/Resources/PuzzleLevelManager.asset";
        private const string OverrideConfigDefaultFolder =
            "Assets/Resources";
        private const string OverrideConfigAssetName =
            "PuzzleLevelOverrideConfig";
        private const string ProviderDefaultFolder =
            "Assets/Resources";

        private static readonly string[] TabNames =
            { "Manager", "Providers", "Overrides" };

        private AsyncResourceLoadingConfiguration _config;
        private AsyncResourceLoaderSettingsProvider.ConcreteManagerInfo _managerInfo;
        private int _tabIndex;
        private Vector2 _scrollPosition;

        // Provider cache — avoids per-frame SerializedObject + AssetDatabase lookups
        private ScriptableObject _cachedManagerAsset;
        private readonly Dictionary<Type, ScriptableObject> _providerCache
            = new Dictionary<Type, ScriptableObject>();

        // Cached editors — avoids per-frame Editor.CreateEditor (Odin caching issue)
        private readonly Dictionary<ScriptableObject, UnityEditor.Editor>
            _cachedEditors = new Dictionary<ScriptableObject, UnityEditor.Editor>();

        private PuzzleLevelSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PuzzleLevelSettingsProvider(
                "Project/Tools/Puzzle Levels",
                SettingsScope.Project,
                new[] { "puzzle", "level", "preload", "override", "catalog" }
            );
        }

        public override void OnGUI(string searchContext)
        {
            try
            {
                base.OnGUI(searchContext);

                _config = GetOrCreateConfiguration();
                if (_config == null)
                {
                    EditorGUILayout.HelpBox(
                        "Failed to create or load "
                        + "AsyncResourceLoaderConfiguration.",
                        MessageType.Error);
                    return;
                }

                _managerInfo = AsyncResourceLoaderSettingsProvider.FindManagerInfo(
                    typeof(PuzzleLevelManager), _config);

                DrawManagerCardHeader();
                GUILayout.Space(4);

                _tabIndex = GUILayout.Toolbar(_tabIndex, TabNames);
                GUILayout.Space(4);

                Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(dividerRect,
                    new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                switch (_tabIndex)
                {
                    case 0:
                        DrawManagerTab();
                        break;
                    case 1:
                        DrawProvidersTab();
                        break;
                    case 2:
                        DrawOverridesTab();
                        break;
                }

                EditorGUILayout.EndScrollView();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PuzzleLevelSettings] {ex}");
            }
        }

        #region Manager Card Header

        private void DrawManagerCardHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool hasManager = _managerInfo != null
                    && _managerInfo.IsAttached;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Puzzle Level Manager", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    string badgeText = hasManager ? " ACTIVE " : " MISSING ";
                    Color badgeColor = hasManager
                        ? new Color(0.2f, 0.7f, 0.2f)
                        : new Color(0.8f, 0.3f, 0.2f);

                    GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        normal = { textColor = Color.white },
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        fixedWidth = 90
                    };

                    Color prevColor = GUI.backgroundColor;
                    GUI.backgroundColor = badgeColor;
                    GUILayout.Label(badgeText, badgeStyle, GUILayout.Height(20));
                    GUI.backgroundColor = prevColor;
                }

                if (hasManager)
                {
                    EditorGUILayout.LabelField(
                        "Resource Type", _managerInfo.ResourceType.Name);
                    EditorGUILayout.LabelField(
                        "Manager Type", _managerInfo.ConcreteType.FullName);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No PuzzleLevelManager found in the configuration. "
                        + "Create one to enable level loading.",
                        MessageType.Warning);
                }
            }
        }

        #endregion

        #region Manager Tab

        private void DrawManagerTab()
        {
            bool hasManager = _managerInfo != null
                && _managerInfo.IsAttached;

            if (!hasManager)
            {
                DrawCreateManagerSection();
                return;
            }

            if (!_cachedEditors.TryGetValue(
                    _managerInfo.AttachedAsset, out UnityEditor.Editor editor)
                || editor == null)
            {
                editor = UnityEditor.Editor.CreateEditor(
                    _managerInfo.AttachedAsset);
                _cachedEditors[_managerInfo.AttachedAsset] = editor;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "MANAGER CONFIGURATION", EditorStyles.miniBoldLabel);
                editor.OnInspectorGUI();
            }
        }

        private void DrawCreateManagerSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Setup", EditorStyles.miniBoldLabel);
                GUILayout.Space(8);

                EditorGUILayout.HelpBox(
                    "Creates the manager asset directly in "
                    + "Assets/Resources/.",
                    MessageType.Info);

                GUILayout.Space(8);

                EditorGUI.BeginDisabledGroup(EditorApplication.isCompiling);

                if (GUILayout.Button(
                        "Create Puzzle Level Manager",
                        GUILayout.Height(36)))
                {
                    CreateAndAttachManager();
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private void CreateAndAttachManager()
        {
            // Ensure Resources folder exists
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            PuzzleLevelManager manager
                = ScriptableObject.CreateInstance<PuzzleLevelManager>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                ManagerAssetPath);

            AssetDatabase.CreateAsset(manager, assetPath);
            AssetDatabase.SaveAssets();

            AttachManagerToConfig(manager);

            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();

            _managerInfo = AsyncResourceLoaderSettingsProvider.FindManagerInfo(
                typeof(PuzzleLevelManager), _config);

            Selection.activeObject = manager;
            EditorGUIUtility.PingObject(manager);

            Debug.Log($"[PuzzleLevelSettings] Created manager at '{assetPath}'.");
        }

        private void AttachManagerToConfig(PuzzleLevelManager manager)
        {
            if (_config == null || manager == null) return;

            SerializedObject serializedConfig = new SerializedObject(_config);
            var listProp = serializedConfig.FindProperty("managerAssets");
            if (listProp == null) return;

            int newIndex = listProp.arraySize++;
            listProp.GetArrayElementAtIndex(newIndex)
                .objectReferenceValue = manager;
            serializedConfig.ApplyModifiedProperties();
        }

        #endregion

        #region Providers Tab

        private void DrawProvidersTab()
        {
            bool hasManager = _managerInfo != null
                && _managerInfo.IsAttached;

            if (!hasManager)
            {
                EditorGUILayout.HelpBox(
                    "Create a PuzzleLevelManager in the Manager tab first.",
                    MessageType.Info);
                return;
            }

            DrawPuzzleLevelProviderSection();
        }

        #region Puzzle Level Provider Cards

        private struct PuzzleLevelProviderCard
        {
            public string DisplayName;
            public Type ProviderType;
            public string[] RequiredDefines;

            public PuzzleLevelProviderCard(
                string displayName, Type providerType,
                string[] requiredDefines = null)
            {
                DisplayName = displayName;
                ProviderType = providerType;
                RequiredDefines = requiredDefines;
            }
        }

        private static readonly PuzzleLevelProviderCard[] ProviderCards =
        {
            new("Resources Folder", typeof(PuzzleLevelResourceFolderProvider)),
            new("Cached (LRU)",     typeof(PuzzleLevelCachedProvider)),
            new("Streaming Assets", typeof(PuzzleLevelStreamingAssetProvider)),
            new("Downloadable",     typeof(PuzzleLevelDownloadableProvider)),
            new("Reference Table",  typeof(PuzzleLevelReferenceTableProvider)),
#if UNITY_ADDRESSABLES
            new("Addressable",      typeof(PuzzleLevelAddressableProvider),
                new[] { "UNITY_ADDRESSABLES" }),
#endif
        };

        private void DrawPuzzleLevelProviderSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Puzzle Level Providers",
                    EditorStyles.miniBoldLabel);

                GUILayout.Space(4);

                foreach (PuzzleLevelProviderCard card in ProviderCards)
                {
                    DrawPuzzleLevelProviderCard(card);
                    GUILayout.Space(4);
                }
            }
        }

        private void DrawPuzzleLevelProviderCard(
            PuzzleLevelProviderCard card)
        {
            ScriptableObject existingAsset
                = FindProviderInArray(card.ProviderType);
            bool isEnabled = existingAsset != null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string[] missingDefines = GetMissingDefines(
                    card.RequiredDefines);

                // -- Header row --
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        card.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (isEnabled)
                    {
                        if (GUILayout.Button("Disable",
                                GUILayout.Width(70)))
                        {
                            DisablePuzzleLevelProvider(
                                card.ProviderType);
                            return;
                        }
                    }
                    else if (missingDefines.Length > 0)
                    {
                        EditorGUI.BeginDisabledGroup(
                            EditorApplication.isCompiling);
                        if (GUILayout.Button(
                                $"Enable {string.Join(", ", missingDefines)}",
                                GUILayout.Width(180)))
                        {
                            EnablePuzzleLevelProviderDefineSymbols(
                                missingDefines);
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    else
                    {
                        EditorGUI.BeginDisabledGroup(
                            EditorApplication.isCompiling);
                        if (GUILayout.Button("Enable",
                                GUILayout.Width(70)))
                        {
                            EnablePuzzleLevelProvider(card);
                            return;
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }

                // -- Body --
                if (isEnabled)
                {
                    GUILayout.Space(4);
                    DrawPuzzleLevelProviderBody(card, existingAsset);
                }
                else if (missingDefines.Length > 0)
                {
                    EditorGUILayout.HelpBox(
                        "Requires scripting define(s): "
                        + string.Join(", ", missingDefines),
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Not yet enabled. Click Enable to create "
                        + "and assign.",
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawPuzzleLevelProviderBody(
            PuzzleLevelProviderCard card,
            ScriptableObject existingAsset)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    existingAsset.name.ToUpperInvariant(),
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                // Use cached editor to avoid per-frame CreateEditor (Odin issue)
                if (!_cachedEditors.TryGetValue(
                        existingAsset, out UnityEditor.Editor editor)
                    || editor == null)
                {
                    editor = UnityEditor.Editor.CreateEditor(existingAsset);
                    _cachedEditors[existingAsset] = editor;
                }

                if (editor != null)
                {
                    editor.OnInspectorGUI();
                }
            }

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        "\U0001F5D1 Delete",
                        EditorStyles.miniButton,
                        GUILayout.Width(80)))
                {
                    DeletePuzzleLevelProvider(card, existingAsset);
                }
            }
        }

        #endregion

        #region Provider Lifecycle Helpers

        private ScriptableObject FindProviderInArray(Type providerType)
        {
            EnsureProviderCache();
            _providerCache.TryGetValue(providerType, out ScriptableObject cached);
            return cached;
        }

        private void EnsureProviderCache()
        {
            ScriptableObject currentManager = _managerInfo?.AttachedAsset;
            if (_cachedManagerAsset == currentManager)
            {
                return;
            }

            _cachedManagerAsset = currentManager;
            _providerCache.Clear();

            if (currentManager == null) return;

            using (SerializedObject so = new SerializedObject(currentManager))
            {
                SerializedProperty prop
                    = so.FindProperty("initialProviders");
                if (prop == null) return;

                for (int i = 0; i < prop.arraySize; i++)
                {
                    ScriptableObject asset
                        = prop.GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                    if (asset != null)
                    {
                        _providerCache[asset.GetType()] = asset;
                    }
                }
            }
        }

        private void EnablePuzzleLevelProvider(
            PuzzleLevelProviderCard card)
        {
            ScriptableObject asset
                = FindOrCreateProviderAsset(card);

            if (asset == null) return;

            using (SerializedObject so = new SerializedObject(
                       _managerInfo.AttachedAsset))
            {
                SerializedProperty prop
                    = so.FindProperty("initialProviders");
                if (prop == null) return;

                so.Update();

                // Add if not already in array
                if (FindProviderInArray(card.ProviderType) == null)
                {
                    int idx = prop.arraySize++;
                    prop.GetArrayElementAtIndex(idx)
                        .objectReferenceValue = asset;
                }

                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(_managerInfo.AttachedAsset);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);

            _cachedManagerAsset = null;
            ClearCachedEditors();
        }

        private void DisablePuzzleLevelProvider(Type providerType)
        {
            using (SerializedObject so = new SerializedObject(
                       _managerInfo.AttachedAsset))
            {
                SerializedProperty prop
                    = so.FindProperty("initialProviders");
                if (prop == null) return;

                so.Update();

                for (int i = 0; i < prop.arraySize; i++)
                {
                    ScriptableObject asset
                        = prop.GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                    if (asset != null
                        && providerType.IsInstanceOfType(asset))
                    {
                        prop.DeleteArrayElementAtIndex(i);
                        // Clean up null gap Unity sometimes leaves
                        if (i < prop.arraySize
                            && prop.GetArrayElementAtIndex(i)
                                .objectReferenceValue == null)
                        {
                            prop.DeleteArrayElementAtIndex(i);
                        }

                        break;
                    }
                }

                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(_managerInfo.AttachedAsset);
            AssetDatabase.SaveAssets();

            _cachedManagerAsset = null;
            ClearCachedEditors();
        }

        private void DeletePuzzleLevelProvider(
            PuzzleLevelProviderCard card,
            ScriptableObject existingAsset)
        {
            string assetPath = AssetDatabase.GetAssetPath(existingAsset);
            string assetName = existingAsset.name;

            if (!EditorUtility.DisplayDialog(
                    "Delete Provider",
                    $"Delete '{assetName}' permanently?",
                    "Delete", "Cancel"))
            {
                return;
            }

            DisablePuzzleLevelProvider(card.ProviderType);

            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
            }

            Debug.Log(
                $"[PuzzleLevelSettings] Deleted provider '{assetName}'.");
        }

        private void ClearCachedEditors()
        {
            foreach (var editor in _cachedEditors.Values)
            {
                if (editor != null)
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                }
            }

            _cachedEditors.Clear();
        }

        private ScriptableObject FindOrCreateProviderAsset(
            PuzzleLevelProviderCard card)
        {
            // Search existing assets on disk
            string[] guids = AssetDatabase.FindAssets(
                "t:" + card.ProviderType.Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset
                    = AssetDatabase.LoadAssetAtPath(
                        path, card.ProviderType) as ScriptableObject;
                if (asset != null) return asset;
            }

            // Create new
            ScriptableObject newAsset
                = ScriptableObject.CreateInstance(card.ProviderType);
            newAsset.name = card.ProviderType.Name;

            string folderPath = ProviderDefaultFolder;
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
            }

            string assetPath2 = AssetDatabase.GenerateUniqueAssetPath(
                $"{folderPath}/{card.ProviderType.Name}.asset");
            AssetDatabase.CreateAsset(newAsset, assetPath2);

            Debug.Log(
                $"[PuzzleLevelSettings] Created provider at "
                + $"'{assetPath2}'.");

            return newAsset;
        }

        private static string[] GetMissingDefines(string[] required)
        {
            if (required == null || required.Length == 0)
            {
                return Array.Empty<string>();
            }

            string currentDefines = PlayerSettings
                .GetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup);
            return required
                .Where(d => !currentDefines.Contains(d))
                .ToArray();
        }

        private static void EnablePuzzleLevelProviderDefineSymbols(
            string[] missingDefines)
        {
            string currentDefines = PlayerSettings
                .GetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup);
            var defines = new HashSet<string>(
                currentDefines.Split(';'));
            foreach (string d in missingDefines)
            {
                defines.Add(d);
            }

            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup,
                string.Join(";", defines));

            Debug.Log(
                "[PuzzleLevelSettings] Enabled scripting defines: "
                + string.Join(", ", missingDefines));
        }

        #endregion

        #endregion

        #region Overrides Tab

        private void DrawOverridesTab()
        {
            bool hasManager = _managerInfo != null
                && _managerInfo.IsAttached;

            if (!hasManager)
            {
                EditorGUILayout.HelpBox(
                    "Create a PuzzleLevelManager in the Manager tab "
                    + "before configuring overrides.",
                    MessageType.Info);
                return;
            }

            PuzzleLevelOverrideConfig currentConfig
                = GetManagerConfigValue<PuzzleLevelOverrideConfig>(
                    "_overrideConfig");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Override Configuration",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                PuzzleLevelOverrideConfig newConfig
                    = (PuzzleLevelOverrideConfig)EditorGUILayout.ObjectField(
                        "Config Asset",
                        currentConfig,
                        typeof(PuzzleLevelOverrideConfig),
                        false);

                if (EditorGUI.EndChangeCheck())
                {
                    SetManagerConfigValue("_overrideConfig", newConfig);
                    currentConfig = newConfig;
                }

                GUILayout.Space(4);

                EditorGUI.BeginDisabledGroup(EditorApplication.isCompiling);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (currentConfig == null)
                    {
                        if (GUILayout.Button(
                                "Create Override Config",
                                GUILayout.Height(30)))
                        {
                            CreateAndBindOverrideConfig();
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(
                                "Reveal in Project",
                                GUILayout.Width(130), GUILayout.Height(24)))
                        {
                            EditorGUIUtility.PingObject(currentConfig);
                        }

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(
                                "Unbind",
                                GUILayout.Width(60), GUILayout.Height(24)))
                        {
                            SetManagerConfigValue(
                                "_overrideConfig", null);
                            currentConfig = null;
                        }
                    }
                }

                EditorGUI.EndDisabledGroup();
            }

            if (currentConfig == null)
            {
                EditorGUILayout.HelpBox(
                    "No override configuration assigned. "
                    + "Create one to pre-define level overrides that "
                    + "are applied at startup.",
                    MessageType.Info);
                return;
            }

            GUILayout.Space(8);

            DrawOverrideEntriesList(currentConfig);

            GUILayout.Space(12);

            DrawRuntimeOverrideStatus();

            GUILayout.Space(8);

            if (GUILayout.Button("Open Override Injector Window",
                    GUILayout.Height(30)))
            {
                PuzzleLevelOverrideWindow.Open();
            }
        }

        private void CreateAndBindOverrideConfig()
        {
            string filePath = EditorUtility.SaveFilePanelInProject(
                "Create Override Config",
                OverrideConfigAssetName,
                "asset",
                "Choose a folder for the override configuration.",
                OverrideConfigDefaultFolder);

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            PuzzleLevelOverrideConfig config
                = ScriptableObject.CreateInstance<
                    PuzzleLevelOverrideConfig>();
            AssetDatabase.CreateAsset(config, filePath);

            SetManagerConfigValue("_overrideConfig", config);

            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            Debug.Log(
                $"[PuzzleLevelSettings] Created override config at "
                + $"'{filePath}'.");
        }

        private static void DrawRuntimeOverrideStatus()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to see active runtime overrides.",
                    MessageType.Info);
                return;
            }

            PuzzleLevelManager manager = PuzzleLevelManager.Instance;
            if (manager == null) return;

            PuzzleLevelOverrideRegistry registry
                = manager.GetOverrideRegistry();
            if (registry == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUIStyle countStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };
                EditorGUILayout.LabelField(
                    $"Runtime Overrides Active: {registry.Count}",
                    countStyle);

                var overriddenIds = registry.GetOverriddenIds();
                if (overriddenIds.Count > 0)
                {
                    GUILayout.Space(4);
                    foreach (string id in overriddenIds)
                    {
                        EditorGUILayout.LabelField(
                            $"  • {id}", EditorStyles.miniLabel);
                    }
                }
            }
        }

        #endregion

        #region Utilities

        private T GetManagerConfigValue<T>(string fieldName)
            where T : ScriptableObject
        {
            using (SerializedObject so = new SerializedObject(
                       _managerInfo.AttachedAsset))
            {
                return so.FindProperty(fieldName)
                    ?.objectReferenceValue as T;
            }
        }

        private void SetManagerConfigValue(
            string fieldName, ScriptableObject value)
        {
            using (SerializedObject so = new SerializedObject(
                       _managerInfo.AttachedAsset))
            {
                SerializedProperty prop
                    = so.FindProperty(fieldName);
                if (prop != null)
                {
                    so.Update();
                    prop.objectReferenceValue = value;
                    so.ApplyModifiedProperties();
                }
            }

            EditorUtility.SetDirty(_managerInfo.AttachedAsset);
        }

        private static AsyncResourceLoadingConfiguration GetOrCreateConfiguration()
        {
            var config = AsyncResourceLoadingConfiguration.Instance;
            if (config != null) return config;

            config = AssetDatabase.LoadAssetAtPath<AsyncResourceLoadingConfiguration>(
                ConfigAssetPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<AsyncResourceLoadingConfiguration>();
            if (!System.IO.Directory.Exists("Assets/Resources"))
            {
                System.IO.Directory.CreateDirectory("Assets/Resources");
            }

            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static void DrawInlineInspector(
            ScriptableObject asset, string header)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(asset);
                try
                {
                    editor.OnInspectorGUI();
                }
                finally
                {
                    if (editor != null)
                    {
                        UnityEngine.Object.DestroyImmediate(editor);
                    }
                }
            }
        }

        private void DrawOverrideEntriesList(
            PuzzleLevelOverrideConfig config)
        {
            if (config == null) return;

            using (SerializedObject so = new SerializedObject(config))
            {
                so.Update();

                SerializedProperty entriesProp
                    = so.FindProperty("_entries");
                if (entriesProp == null) return;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        "OVERRIDE ENTRIES", EditorStyles.miniBoldLabel);

                    bool changed = false;

                    for (int i = 0; i < entriesProp.arraySize; i++)
                    {
                        SerializedProperty element
                            = entriesProp.GetArrayElementAtIndex(i);

                        using (new EditorGUILayout.VerticalScope(
                                   EditorStyles.helpBox))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField(
                                    $"Entry {i}",
                                    EditorStyles.boldLabel,
                                    GUILayout.Width(60));

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("X",
                                        EditorStyles.miniButton,
                                        GUILayout.Width(24)))
                                {
                                    entriesProp.DeleteArrayElementAtIndex(i);
                                    changed = true;
                                    break; // Exit loop after deletion
                                }
                            }

                            DrawChildStringField(
                                element, "LevelId", "Level ID");
                            DrawChildObjectField(
                                element,
                                "OverrideAsset",
                                "Override Asset",
                                typeof(TextAsset));
                            DrawChildEnumField(
                                element, "DataType", "Data Type");
                        }
                    }

                    if (changed)
                    {
                        so.ApplyModifiedProperties();
                        return; // Restart rendering next frame
                    }

                    GUILayout.Space(4);

                    if (GUILayout.Button("Add Override Entry",
                            GUILayout.Height(24)))
                    {
                        entriesProp.arraySize++;
                        so.ApplyModifiedProperties();
                    }
                }

                so.ApplyModifiedProperties();
            }
        }

        // -- Manual IMGUI field helpers (no PropertyField, no Odin caching) --

        private static void DrawConfigIntField(
            SerializedObject so, string name, string label)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null) return;

            int value = EditorGUILayout.IntField(label, prop.intValue);
            if (value != prop.intValue)
            {
                prop.intValue = value;
            }
        }

        private static void DrawConfigFloatField(
            SerializedObject so, string name, string label)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null) return;

            float value = EditorGUILayout.FloatField(label, prop.floatValue);
            if (!Mathf.Approximately(value, prop.floatValue))
            {
                prop.floatValue = value;
            }
        }

        private static void DrawChildStringField(
            SerializedProperty parent, string name, string label)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            if (prop == null) return;

            string value = EditorGUILayout.TextField(
                label, prop.stringValue);
            if (value != prop.stringValue)
            {
                prop.stringValue = value;
            }
        }

        private static void DrawChildObjectField(
            SerializedProperty parent,
            string name,
            string label,
            System.Type objectType)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            if (prop == null) return;

            Object current = prop.objectReferenceValue;
            Object newValue = EditorGUILayout.ObjectField(
                label, current, objectType, false);
            if (newValue != current)
            {
                prop.objectReferenceValue = newValue;
            }
        }

        private static void DrawChildEnumField(
            SerializedProperty parent, string name, string label)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            if (prop == null) return;

            int currentValue = prop.intValue;
            if (!System.Enum.IsDefined(typeof(DataType), currentValue))
            {
                currentValue = 0;
            }

            var newValue = (DataType)EditorGUILayout.EnumPopup(
                label, (DataType)currentValue);
            if ((int)newValue != prop.intValue)
            {
                prop.intValue = (int)newValue;
            }
        }

        #endregion
    }
}
