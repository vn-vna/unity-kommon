using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;
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

        private const string TabPrefKey =
            "PuzzleLevelSettingsProvider_TabIndex";

        private static readonly string[] TabNames =
            { "Manager", "Providers", "Overrides" };

        private AsyncResourceLoadingConfiguration _config;
        private AsyncResourceLoaderSettingsProvider.ConcreteManagerInfo _managerInfo;
        private PuzzleLevelManager _managerCandidate;
        private int _managerAssetCount = -1;
        private int _tabIndex;
        private Vector2 _scrollPosition;

        // Provider cache — avoids per-frame SerializedObject + AssetDatabase lookups
        private ScriptableObject _cachedManagerAsset;
        private int _providerSignature;
        private readonly Dictionary<Type, ScriptableObject> _providerCache
            = new Dictionary<Type, ScriptableObject>();
        private readonly Dictionary<Type, ScriptableObject> _providerCandidates
            = new Dictionary<Type, ScriptableObject>();

        // Cached editors — avoids per-frame Editor.CreateEditor (Odin caching issue)
        private readonly Dictionary<ScriptableObject, UnityEditor.Editor>
            _cachedEditors = new Dictionary<ScriptableObject, UnityEditor.Editor>();

#if !UNITY_ADDRESSABLES
        private static AddRequest _addressablesInstallRequest;
#endif

        private PuzzleLevelSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _tabIndex = EditorPrefs.GetInt(TabPrefKey, 0);
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PuzzleLevelSettingsProvider(
                "Project/Frameworks/Puzzle Levels",
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

                int newTab = GUILayout.Toolbar(_tabIndex, TabNames);
                if (newTab != _tabIndex)
                {
                    _tabIndex = newTab;
                    EditorPrefs.SetInt(TabPrefKey, _tabIndex);
                }
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
                QuickLog.Error<PuzzleLevelSettingsProvider>(
                    "Failed to draw Puzzle Level settings: {0}",
                    ex);
            }
        }

        public override void OnActivate(
            string searchContext,
            VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            Undo.undoRedoPerformed += HandleProjectStateChanged;
            EditorApplication.projectChanged += HandleProjectStateChanged;
            InvalidateCaches();
        }

        public override void OnDeactivate()
        {
            Undo.undoRedoPerformed -= HandleProjectStateChanged;
            EditorApplication.projectChanged -= HandleProjectStateChanged;
            InvalidateCaches();
            base.OnDeactivate();
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

                int managerAssetCount = GetManagerAssetCount();
                if (managerAssetCount > 1)
                {
                    EditorGUILayout.HelpBox(
                        $"Found {managerAssetCount} PuzzleLevelManager assets. "
                        + "Keep one canonical asset at "
                        + "Assets/Resources/PuzzleLevelManager.asset to avoid "
                        + "split-brain singleton resolution.",
                        MessageType.Error);
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
                    "Attach an existing canonical manager or create one at "
                    + "Assets/Resources/PuzzleLevelManager.asset.",
                    MessageType.Info);

                GUILayout.Space(8);

                _managerCandidate = (PuzzleLevelManager)EditorGUILayout.ObjectField(
                    "Existing Manager",
                    _managerCandidate,
                    typeof(PuzzleLevelManager),
                    false);

                using (new EditorGUI.DisabledScope(
                           _managerCandidate == null
                           || EditorApplication.isCompiling))
                {
                    if (GUILayout.Button(
                            "Attach Existing Manager",
                            GUILayout.Height(28)))
                    {
                        AttachManagerToConfig(_managerCandidate);
                        RefreshManagerInfo();
                        return;
                    }
                }

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
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            PuzzleLevelManager canonicalManager
                = AssetDatabase.LoadAssetAtPath<PuzzleLevelManager>(
                    ManagerAssetPath);
            if (canonicalManager != null)
            {
                AttachManagerToConfig(canonicalManager);
                RefreshManagerInfo();
                Selection.activeObject = canonicalManager;
                EditorGUIUtility.PingObject(canonicalManager);
                return;
            }

            string[] existingManagerGuids = AssetDatabase.FindAssets(
                $"t:{nameof(PuzzleLevelManager)}");
            if (existingManagerGuids.Length > 0)
            {
                string firstPath = AssetDatabase.GUIDToAssetPath(
                    existingManagerGuids[0]);
                _managerCandidate = AssetDatabase.LoadAssetAtPath<
                    PuzzleLevelManager>(firstPath);
                EditorUtility.DisplayDialog(
                    "Existing Manager Found",
                    "A PuzzleLevelManager already exists. Review and attach "
                    + "it explicitly to avoid a split-brain singleton.",
                    "OK");
                if (_managerCandidate != null)
                {
                    Selection.activeObject = _managerCandidate;
                    EditorGUIUtility.PingObject(_managerCandidate);
                }

                return;
            }

            PuzzleLevelManager manager
                = ScriptableObject.CreateInstance<PuzzleLevelManager>();

            AssetDatabase.CreateAsset(manager, ManagerAssetPath);
            Undo.RegisterCreatedObjectUndo(
                manager,
                "Create Puzzle Level Manager");

            AttachManagerToConfig(manager);
            AssetDatabase.SaveAssets();
            RefreshManagerInfo();

            Selection.activeObject = manager;
            EditorGUIUtility.PingObject(manager);

            QuickLog.Info<PuzzleLevelSettingsProvider>(
                "Created canonical PuzzleLevelManager at '{0}'.",
                ManagerAssetPath);
        }

        private void AttachManagerToConfig(PuzzleLevelManager manager)
        {
            if (_config == null || manager == null) return;

            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty listProp
                = serializedConfig.FindProperty("managerAssets");
            if (listProp == null) return;

            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue
                    == manager)
                {
                    return;
                }
            }

            Undo.RecordObject(_config, "Attach Puzzle Level Manager");
            int newIndex = listProp.arraySize++;
            listProp.GetArrayElementAtIndex(newIndex)
                .objectReferenceValue = manager;
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            InvalidateCaches();
        }

        private void RefreshManagerInfo()
        {
            _managerInfo = AsyncResourceLoaderSettingsProvider.FindManagerInfo(
                typeof(PuzzleLevelManager),
                _config);
            InvalidateCaches();
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

            public PuzzleLevelProviderCard(
                string displayName,
                Type providerType)
            {
                DisplayName = displayName;
                ProviderType = providerType;
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
            new("Addressable",      typeof(PuzzleLevelAddressableProvider)),
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

#if !UNITY_ADDRESSABLES
                DrawAddressablesSetupCard();
#endif
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
                    else
                    {
                        GUILayout.Label(
                            " DISABLED ",
                            EditorStyles.miniLabel,
                            GUILayout.Width(70));
                    }
                }

                if (isEnabled)
                {
                    GUILayout.Space(4);
                    DrawPuzzleLevelProviderBody(card, existingAsset);
                }
                else
                {
                    DrawDisabledProviderBody(card);
                }
            }
        }

        private void DrawDisabledProviderBody(PuzzleLevelProviderCard card)
        {
            _providerCandidates.TryGetValue(
                card.ProviderType,
                out ScriptableObject candidate);
            ScriptableObject selected = EditorGUILayout.ObjectField(
                "Existing Asset",
                candidate,
                card.ProviderType,
                false) as ScriptableObject;
            _providerCandidates[card.ProviderType] = selected;

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                using (new EditorGUI.DisabledScope(selected == null))
                {
                    if (GUILayout.Button("Attach Existing"))
                    {
                        EnablePuzzleLevelProvider(card, selected);
                        return;
                    }
                }

                if (GUILayout.Button("Create New"))
                {
                    ScriptableObject created = CreateProviderAsset(card);
                    if (created != null)
                    {
                        EnablePuzzleLevelProvider(card, created);
                    }
                }
            }
        }

#if !UNITY_ADDRESSABLES
        private static void DrawAddressablesSetupCard()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Addressable",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Install com.unity.addressables to enable the optional "
                    + "Addressable puzzle-level provider. The assembly version "
                    + "define is configured automatically.",
                    MessageType.Info);

                using (new EditorGUI.DisabledScope(
                           EditorApplication.isCompiling
                           || _addressablesInstallRequest != null))
                {
                    if (GUILayout.Button("Install Addressables"))
                    {
                        _addressablesInstallRequest = Client.Add(
                            "com.unity.addressables");
                        EditorApplication.update
                            += HandleAddressablesInstallProgress;
                    }
                }
            }
        }

        private static void HandleAddressablesInstallProgress()
        {
            if (_addressablesInstallRequest == null
                || !_addressablesInstallRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= HandleAddressablesInstallProgress;
            if (_addressablesInstallRequest.Status == StatusCode.Success)
            {
                QuickLog.Info<PuzzleLevelSettingsProvider>(
                    "Installed Addressables package '{0}'.",
                    _addressablesInstallRequest.Result.packageId);
            }
            else
            {
                QuickLog.Error<PuzzleLevelSettingsProvider>(
                    "Addressables installation failed: {0}",
                    _addressablesInstallRequest.Error?.message
                    ?? "Unknown package-manager error");
            }

            _addressablesInstallRequest = null;
        }
#endif

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
            if (currentManager == null)
            {
                _cachedManagerAsset = null;
                _providerSignature = 0;
                _providerCache.Clear();
                return;
            }

            using (SerializedObject so = new SerializedObject(currentManager))
            {
                SerializedProperty prop
                    = so.FindProperty("initialProviders");
                if (prop == null) return;

                int signature = 17;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    Object reference
                        = prop.GetArrayElementAtIndex(i).objectReferenceValue;
                    signature = unchecked(
                        signature * 31
                        + (reference != null ? reference.GetInstanceID() : 0));
                }

                if (_cachedManagerAsset == currentManager
                    && _providerSignature == signature)
                {
                    return;
                }

                _cachedManagerAsset = currentManager;
                _providerSignature = signature;
                _providerCache.Clear();

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
            PuzzleLevelProviderCard card,
            ScriptableObject asset)
        {
            if (asset == null
                || !card.ProviderType.IsInstanceOfType(asset))
            {
                return;
            }

            using (SerializedObject so = new SerializedObject(
                       _managerInfo.AttachedAsset))
            {
                SerializedProperty prop
                    = so.FindProperty("initialProviders");
                if (prop == null) return;

                so.Update();

                if (FindProviderInArray(card.ProviderType) == null)
                {
                    Undo.RecordObject(
                        _managerInfo.AttachedAsset,
                        $"Enable {card.DisplayName} Provider");
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
                Undo.RecordObject(
                    _managerInfo.AttachedAsset,
                    "Disable Puzzle Level Provider");

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
            List<PuzzleLevelManager> users = FindProviderUsers(existingAsset);

            if (users.Count > 1)
            {
                EditorUtility.DisplayDialog(
                    "Shared Provider Cannot Be Deleted",
                    $"'{assetName}' is referenced by {users.Count} managers. "
                    + "Disable it here, then remove all remaining references "
                    + "before deleting the asset.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Move Provider to Trash",
                    $"Disable '{assetName}' and move its asset to the OS trash?",
                    "Move to Trash", "Cancel"))
            {
                return;
            }

            DisablePuzzleLevelProvider(card.ProviderType);

            if (!string.IsNullOrEmpty(assetPath))
            {
                if (!AssetDatabase.MoveAssetToTrash(assetPath))
                {
                    EnablePuzzleLevelProvider(card, existingAsset);
                    EditorUtility.DisplayDialog(
                        "Delete Failed",
                        $"Could not move '{assetName}' to the trash.",
                        "OK");
                    return;
                }

                AssetDatabase.SaveAssets();
            }

            QuickLog.Info<PuzzleLevelSettingsProvider>(
                "Moved provider '{0}' to the trash.",
                assetName);
        }

        private static List<PuzzleLevelManager> FindProviderUsers(
            ScriptableObject provider)
        {
            List<PuzzleLevelManager> users = new List<PuzzleLevelManager>();
            string[] managerGuids = AssetDatabase.FindAssets(
                $"t:{nameof(PuzzleLevelManager)}");
            foreach (string managerGuid in managerGuids)
            {
                string managerPath = AssetDatabase.GUIDToAssetPath(managerGuid);
                PuzzleLevelManager manager
                    = AssetDatabase.LoadAssetAtPath<PuzzleLevelManager>(
                        managerPath);
                if (manager == null)
                {
                    continue;
                }

                using SerializedObject serializedManager
                    = new SerializedObject(manager);
                SerializedProperty providers
                    = serializedManager.FindProperty("initialProviders");
                if (providers == null)
                {
                    continue;
                }

                for (int i = 0; i < providers.arraySize; i++)
                {
                    if (providers.GetArrayElementAtIndex(i).objectReferenceValue
                        != provider)
                    {
                        continue;
                    }

                    users.Add(manager);
                    break;
                }
            }

            return users;
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

        private void HandleProjectStateChanged()
        {
            InvalidateCaches();
            Repaint();
        }

        private void InvalidateCaches()
        {
            _managerAssetCount = -1;
            _cachedManagerAsset = null;
            _providerSignature = 0;
            _providerCache.Clear();
            _providerCandidates.Clear();
            ClearCachedEditors();
        }

        private int GetManagerAssetCount()
        {
            if (_managerAssetCount < 0)
            {
                _managerAssetCount = AssetDatabase.FindAssets(
                    $"t:{nameof(PuzzleLevelManager)}").Length;
            }

            return _managerAssetCount;
        }

        private ScriptableObject CreateProviderAsset(
            PuzzleLevelProviderCard card)
        {
            ScriptableObject newAsset
                = ScriptableObject.CreateInstance(card.ProviderType);
            newAsset.name = card.ProviderType.Name;

            string folderPath = ProviderDefaultFolder;
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string assetPath2 = AssetDatabase.GenerateUniqueAssetPath(
                $"{folderPath}/{card.ProviderType.Name}.asset");
            AssetDatabase.CreateAsset(newAsset, assetPath2);
            Undo.RegisterCreatedObjectUndo(
                newAsset,
                $"Create {card.DisplayName} Provider");

            QuickLog.Info<PuzzleLevelSettingsProvider>(
                "Created provider at '{0}'.",
                assetPath2);

            return newAsset;
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
            Undo.RegisterCreatedObjectUndo(
                config,
                "Create Puzzle Level Override Config");

            SetManagerConfigValue("_overrideConfig", config);

            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            QuickLog.Info<PuzzleLevelSettingsProvider>(
                "Created override config at '{0}'.",
                filePath);
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
                    Undo.RecordObject(
                        _managerInfo.AttachedAsset,
                        "Change Puzzle Level Manager Configuration");
                    prop.objectReferenceValue = value;
                    so.ApplyModifiedProperties();
                }
            }

            EditorUtility.SetDirty(_managerInfo.AttachedAsset);
            AssetDatabase.SaveAssets();
            InvalidateCaches();
        }

        private static AsyncResourceLoadingConfiguration GetOrCreateConfiguration()
        {
            var config = AsyncResourceLoadingConfiguration.Instance;
            if (config != null) return config;

            config = AssetDatabase.LoadAssetAtPath<AsyncResourceLoadingConfiguration>(
                ConfigAssetPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<AsyncResourceLoadingConfiguration>();
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            Undo.RegisterCreatedObjectUndo(
                config,
                "Create Async Resource Loader Configuration");
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
                        Undo.RecordObject(config, "Add Puzzle Level Override");
                        int newIndex = entriesProp.arraySize;
                        entriesProp.InsertArrayElementAtIndex(newIndex);
                        SerializedProperty newEntry
                            = entriesProp.GetArrayElementAtIndex(newIndex);
                        newEntry.FindPropertyRelative("LevelId").stringValue
                            = string.Empty;
                        newEntry.FindPropertyRelative("OverrideAsset")
                            .objectReferenceValue = null;
                        newEntry.FindPropertyRelative("DataType").intValue
                            = (int)DataType.Text;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(config);
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
