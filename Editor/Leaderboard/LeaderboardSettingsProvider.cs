using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Leaderboard;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard.Editor
{
    internal sealed class LeaderboardSettingsProvider : SettingsProvider
    {
        #region Constants

        private const string SettingsAssetPath =
            "Assets/Resources/Integration/Managers/LeaderboardConfiguration.asset";

        private const string ProviderAssetFolder =
            "Assets/Resources/Integration";

        private const string TabPrefKey =
            "LeaderboardSettingsProvider_SelectedTab";

        private static readonly string[] TabNames =
            { "Manage", "Leaderboards" };

        #endregion

        #region Private Fields

        private SerializedObject _serializedSettings;
        private int _selectedTabIndex;
        private Vector2 _scrollPosition;
        private Vector2 _providerScrollPosition;
        private Vector2 _leaderboardScrollPosition;

        private Dictionary<ScriptableObject, UnityEditor.Editor>
            _inlineEditorCache;

        private static Type[] _cachedProviderTypes;

        #endregion

        #region Constructor

        private LeaderboardSettingsProvider(
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
            return new LeaderboardSettingsProvider(
                "Project/Tools/Leaderboards",
                SettingsScope.Project,
                new[]
                {
                    "leaderboard", "score", "ranking",
                    "highscores", "ladder"
                }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            LeaderboardConfiguration settings = GetOrCreateSettings();
            if (settings == null) return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            EditorGUILayout.Space();

            DrawStatusBar(settings);

            EditorGUILayout.Space();
            int newTab = GUILayout.Toolbar(
                _selectedTabIndex, TabNames);
            if (newTab != _selectedTabIndex)
            {
                _selectedTabIndex = newTab;
                EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            }
            EditorGUILayout.Space();

            Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition);

            switch (_selectedTabIndex)
            {
                case 0: DrawManageTab(); break;
                case 1: DrawLeaderboardsTab(); break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            DestroyInlineEditors();
            _cachedProviderTypes = null;
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar(LeaderboardConfiguration config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int providerCount = config.ProviderAsset != null ? 1 : 0;
                int leaderboardCount = config.Leaderboards.Length;
                bool valid = providerCount > 0 && leaderboardCount > 0;

                Color statusColor = valid ? Color.green : Color.yellow;
                string statusText = valid ? "Ready" : "Incomplete";

                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = statusColor }
                };

                EditorGUILayout.LabelField(
                    $"Status: {statusText}  |  "
                    + $"{providerCount} provider, "
                    + $"{leaderboardCount} leaderboards",
                    style
                );
            }
        }

        #endregion

        #region Tab — Manage

        private void DrawManageTab()
        {
            EditorGUILayout.HelpBox(
                "Select the leaderboard provider. Only one provider "
                + "can be active at a time.",
                MessageType.None);
            EditorGUILayout.Space();

            SerializedProperty providerProp =
                _serializedSettings.FindProperty("_provider");

            if (providerProp == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_provider' field.",
                    MessageType.Error);
                return;
            }

            Type[] providerTypes =
                GetCachedProviderTypes(typeof(ILeaderboardProvider));

            if (providerTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing "
                    + "ILeaderboardProvider found.",
                    MessageType.Info);
                return;
            }

            ScriptableObject currentProvider =
                providerProp.objectReferenceValue as ScriptableObject;
            Type currentType = currentProvider?.GetType();

            _providerScrollPosition =
                EditorGUILayout.BeginScrollView(_providerScrollPosition);

            foreach (Type providerType in providerTypes)
            {
                bool isActive = currentType != null
                    && providerType.IsAssignableFrom(currentType);

                DrawProviderCard(
                    providerType,
                    isActive,
                    currentProvider,
                    providerProp);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawProviderCard(
            Type providerType,
            bool isActive,
            ScriptableObject currentProvider,
            SerializedProperty prop)
        {
            string displayName =
                ObjectNames.NicifyVariableName(providerType.Name);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        displayName, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (isActive && currentProvider != null)
                    {
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                        GUILayout.Label(
                            "ACTIVE", EditorStyles.miniButton,
                            GUILayout.Width(60));
                        GUI.backgroundColor = Color.white;

                        if (DrawDeleteButton())
                        {
                            DeleteAssetAndClearField(currentProvider, prop);
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Enable", GUILayout.Width(70)))
                        {
                            ScriptableObject asset =
                                FindOrCreateProviderAsset(providerType);
                            prop.objectReferenceValue = asset;
                            _serializedSettings.ApplyModifiedProperties();
                            EditorUtility.SetDirty(
                                _serializedSettings.targetObject);
                            AssetDatabase.SaveAssets();
                            DestroyInlineEditors();
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (isActive && currentProvider != null)
                {
                    GUILayout.Space(4);
                    DrawInlineInspectorFor(currentProvider);
                }
            }
        }

        #endregion

        #region Tab — Leaderboards

        private void DrawLeaderboardsTab()
        {
            EditorGUILayout.HelpBox(
                "Define your leaderboards. Each entry's ID must match "
                + "the ID registered on the provider's backend.",
                MessageType.None);
            EditorGUILayout.Space();

            SerializedProperty leaderboardsProp =
                _serializedSettings.FindProperty("_leaderboards");

            if (leaderboardsProp == null || !leaderboardsProp.isArray)
            {
                EditorGUILayout.HelpBox(
                    "Could not find '_leaderboards' field.",
                    MessageType.Error);
                return;
            }

            if (leaderboardsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No leaderboards configured. Add one below.",
                    MessageType.Warning);
            }

            _leaderboardScrollPosition =
                EditorGUILayout.BeginScrollView(_leaderboardScrollPosition);

            for (int i = 0; i < leaderboardsProp.arraySize; i++)
            {
                DrawLeaderboardCard(leaderboardsProp, i);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        "+ Add Leaderboard",
                        GUILayout.Height(26),
                        GUILayout.Width(160)))
                {
                    AddLeaderboardSubAsset(leaderboardsProp);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawLeaderboardCard(
            SerializedProperty arrayProp,
            int index)
        {
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(index);
            var def = element.objectReferenceValue
                as LeaderboardDefinition;
            if (def == null) return;

            SerializedObject defSo = new SerializedObject(def);
            defSo.Update();

            string title = def.DisplayName;
            if (string.IsNullOrEmpty(title))
            {
                title = "Leaderboard " + (index + 1);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Numbered badge
                    Rect badgeRect = GUILayoutUtility.GetRect(
                        22, 18,
                        GUILayout.Width(22),
                        GUILayout.Height(18));
                    EditorGUI.DrawRect(
                        badgeRect,
                        new Color(0.25f, 0.45f, 0.65f, 0.8f));
                    GUI.Label(
                        badgeRect,
                        (index + 1).ToString(),
                        new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = Color.white }
                        });

                    EditorGUILayout.LabelField(
                        title, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    // Reorder
                    using (new EditorGUI.DisabledScope(index == 0))
                    {
                        if (GUILayout.Button(
                                "\u25B2", EditorStyles.miniButtonLeft,
                                GUILayout.Width(22), GUILayout.Height(18)))
                        {
                            arrayProp.MoveArrayElement(index, index - 1);
                        }
                    }

                    using (new EditorGUI.DisabledScope(
                               index >= arrayProp.arraySize - 1))
                    {
                        if (GUILayout.Button(
                                "\u25BC", EditorStyles.miniButtonRight,
                                GUILayout.Width(22), GUILayout.Height(18)))
                        {
                            arrayProp.MoveArrayElement(index, index + 1);
                        }
                    }

                    // Edit (rename)
                    if (GUILayout.Button(
                            "\u270E", EditorStyles.miniButton,
                            GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        SerializedProperty editElement = arrayProp.GetArrayElementAtIndex(index);
                        var editDef = editElement.objectReferenceValue as LeaderboardDefinition;
                        if (editDef != null)
                        {
                            Rect btnRect = GUILayoutUtility.GetLastRect();
                            btnRect.position = GUIUtility.GUIToScreenPoint(btnRect.position);
                            PopupWindow.Show(btnRect, new RenameSubAssetPopup(editDef, _serializedSettings));
                        }
                    }

                    // Delete
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button(
                            "\u2717", EditorStyles.miniButton,
                            GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        RemoveLeaderboardSubAsset(arrayProp, index);
                        GUI.backgroundColor = Color.white;
                        return;
                    }
                    GUI.backgroundColor = Color.white;
                }

                // Separator
                Rect sep = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(
                    sep, new Color(0.5f, 0.5f, 0.5f, 0.2f));
                GUILayout.Space(2);

                // Fields — draw from the sub-asset's SerializedObject
                DrawPropFrom(defSo, "_id", "ID");
                DrawPropFrom(defSo, "_displayName", "Display Name");
                DrawPropFrom(defSo, "_sortOrder", "Sort Order");
                DrawPropFrom(defSo, "_resetCadence", "Reset");

                defSo.ApplyModifiedProperties();
            }
        }

        private static void DrawPropFrom(
            SerializedObject so,
            string fieldName,
            string label)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        #endregion

        #region Sub-Asset Helpers

        private void AddLeaderboardSubAsset(
            SerializedProperty arrayProp)
        {
            var config =
                _serializedSettings.targetObject as LeaderboardConfiguration;
            if (config == null) return;

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath)) return;

            var def = ScriptableObject.CreateInstance<LeaderboardDefinition>();
            def.name = "Leaderboard_" + (arrayProp.arraySize + 1);

            AssetDatabase.AddObjectToAsset(def, assetPath);
            AssetDatabase.SaveAssets();

            arrayProp.arraySize++;
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
            element.objectReferenceValue = def;

            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void RemoveLeaderboardSubAsset(
            SerializedProperty arrayProp,
            int index)
        {
            SerializedProperty element =
                arrayProp.GetArrayElementAtIndex(index);
            var def = element.objectReferenceValue
                as LeaderboardDefinition;

            arrayProp.DeleteArrayElementAtIndex(index);
            CleanNullArrayEntries(arrayProp);
            _serializedSettings.ApplyModifiedProperties();

            if (def != null)
            {
                AssetDatabase.RemoveObjectFromAsset(def);
                UnityEngine.Object.DestroyImmediate(def, true);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.SetDirty(
                _serializedSettings.targetObject);
        }

        private static void CleanNullArrayEntries(
            SerializedProperty arrayProp)
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

        #region Provider Helpers

        private static Type[] GetCachedProviderTypes(Type interfaceType)
        {
            _cachedProviderTypes ??=
                ScanProviderTypes(interfaceType);
            return _cachedProviderTypes;
        }

        private static Type[] ScanProviderTypes(Type interfaceType)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t =>
                    t.IsClass && !t.IsAbstract &&
                    typeof(ScriptableObject).IsAssignableFrom(t) &&
                    interfaceType.IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToArray();
        }

        private ScriptableObject FindOrCreateProviderAsset(Type providerType)
        {
            string[] guids =
                AssetDatabase.FindAssets("t:" + providerType.Name);
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

        private void DeleteAssetAndClearField(
            ScriptableObject asset,
            SerializedProperty prop)
        {
            if (asset == null) return;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                $"Delete '{asset.name}' at '{assetPath}'?\n\n"
                + "This cannot be undone.",
                "Delete", "Cancel");

            if (!confirmed) return;

            prop.objectReferenceValue = null;
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_serializedSettings.targetObject);

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            DestroyInlineEditors();
        }

        #endregion

        #region Asset Helpers

        private static LeaderboardConfiguration GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LeaderboardConfiguration>(
                SettingsAssetPath);
            if (settings != null) return settings;

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No Leaderboard Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info);

            if (GUILayout.Button(
                    "Create Leaderboard Configuration",
                    GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            string folder = Path.GetDirectoryName(SettingsAssetPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var settings =
                ScriptableObject.CreateInstance<LeaderboardConfiguration>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[Leaderboard] Created configuration at '"
                + SettingsAssetPath + "'.");
        }

        private void EnsureSerializedObject(
            LeaderboardConfiguration settings)
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

        #region Drawing Utilities

        private static bool DrawDeleteButton()
        {
            var content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            if (content == null || content.image == null)
            {
                content = new GUIContent("\u2717", "Delete");
            }

            content.tooltip = "Delete the provider asset from disk.";
            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(24),
                GUILayout.Height(18));
        }

        #endregion

        #region Nested Types

        private sealed class RenameSubAssetPopup : PopupWindowContent
        {
            private readonly ScriptableObject _target;
            private readonly SerializedObject _parentSo;
            private string _newName;

            public RenameSubAssetPopup(
                ScriptableObject target,
                SerializedObject parentSo)
            {
                _target = target;
                _parentSo = parentSo;
                _newName = target.name;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(260f, 60f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Rename Asset", EditorStyles.boldLabel);
                GUILayout.Space(4);

                _newName = EditorGUILayout.TextField("Name", _newName);

                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                    {
                        editorWindow.Close();
                    }

                    if (GUILayout.Button("Save", GUILayout.Width(70)))
                    {
                        ApplyRename();
                        editorWindow.Close();
                    }
                }
            }

            private void ApplyRename()
            {
                if (_target == null) return;

                string trimmed = _newName?.Trim();
                if (string.IsNullOrEmpty(trimmed)) return;

                _target.name = trimmed;
                EditorUtility.SetDirty(_target);

                if (_parentSo != null)
                {
                    _parentSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_parentSo.targetObject);
                }

                AssetDatabase.SaveAssets();
            }
        }

        #endregion
    }
}
