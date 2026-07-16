using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.DataSync;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync.Editor
{
    internal sealed class DataSyncSettingsProvider : SettingsProvider
    {
        private const string SettingsAssetPath =
            "Assets/Resources/Integration/Managers/DataSyncConfiguration.asset";

        private const string ResourcesFolder = "Assets/Resources";

        private const string ConfigFolder =
            "Assets/Resources/Integration/Managers";

        private const string ProviderAssetFolder =
            "Assets/Resources/Integration";

        private static readonly string[] TabNames =
            { "Director", "Translators", "Adapters", "Preview" };

        private static readonly string[] AdapterSubTabNames =
            { "Manage", "Load Orders", "Save Orders" };

        private SerializedObject _serializedSettings;
        private int _selectedTabIndex;
        private int _adapterSubTabIndex;
        private Vector2 _scrollPosition;
        private Vector2 _manageScrollPosition;

        private Dictionary<ScriptableObject, UnityEditor.Editor>
            _inlineEditorCache;

        private static Type[] _cachedTranslatorTypes;
        private static Type[] _cachedAdapterTypes;
        private static GUIStyle _statusLabelStyle;

        private DataSyncSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new DataSyncSettingsProvider(
                "Project/Tools/Data Sync",
                SettingsScope.Project,
                new[]
                {
                    "data", "sync", "save", "load",
                    "adapter", "translator", "persist",
                    "version", "migration", "conflict"
                }
            );
        }

        #region GUI

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            DataSyncConfiguration settings = GetOrCreateSettings();
            if (settings == null) return;

            EnsureSerializedObject(settings);
            _serializedSettings.Update();

            EditorGUILayout.Space();

            DrawStatusBar(settings);

            EditorGUILayout.Space();
            _selectedTabIndex = GUILayout.Toolbar(
                _selectedTabIndex, TabNames
            );
            EditorGUILayout.Space();

            Rect dividerRect = EditorGUILayout.GetControlRect(
                false, 1f
            );
            EditorGUI.DrawRect(
                dividerRect,
                new Color(0.5f, 0.5f, 0.5f, 0.3f)
            );
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition
            );

            switch (_selectedTabIndex)
            {
                case 0: DrawDirectorTab(); break;
                case 1: DrawTranslatorsTab(); break;
                case 2: DrawAdaptersTab(); break;
                case 3: DrawPreviewTab(); break;
            }

            EditorGUILayout.EndScrollView();
            _serializedSettings.ApplyModifiedProperties();
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            DestroyInlineEditors();
            _cachedTranslatorTypes = null;
            _cachedAdapterTypes = null;
        }

        #endregion

        #region Tab — Director

        private void DrawDirectorTab()
        {
            EditorGUILayout.HelpBox(
                "Configure adapters, translators, and order groups "
                + "in their respective tabs below.",
                MessageType.None
            );
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Config Asset",
                    GUILayout.Width(EditorGUIUtility.labelWidth)
                );
                EditorGUILayout.LabelField(
                    SettingsAssetPath, EditorStyles.miniLabel
                );
            }
            EditorGUILayout.Space();

            // ── Adapter Features ──
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Adapter Features", EditorStyles.miniBoldLabel
                );
                EditorGUILayout.HelpBox(
                    "Enable scripting defines to activate "
                    + "platform-specific adapters. Changes require "
                    + "recompilation.",
                    MessageType.Info
                );

                DrawScriptingDefineToggle(
                    "GOOGLE_SERVICES_SAVE",
                    "Google Service Save Adapter",
                    "Google Play Games Services saved-game API"
                );
            }
            EditorGUILayout.Space();

            // ── General Settings ──
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "General Settings", EditorStyles.miniBoldLabel
                );
                DrawSerializedProperty(
                    "_defaultAdapterId", "Default Adapter Id"
                );
            }
        }

        private static void DrawScriptingDefineToggle(
            string define,
            string label,
            string tooltip)
        {
            BuildTargetGroup buildTarget =
                EditorUserBuildSettings.selectedBuildTargetGroup;
            string currentDefines =
                PlayerSettings.GetScriptingDefineSymbolsForGroup(
                    buildTarget
                );
            bool isEnabled = currentDefines.Contains(define);

            EditorGUI.BeginChangeCheck();
            bool newEnabled = EditorGUILayout.Toggle(
                new GUIContent(label, tooltip), isEnabled
            );

            if (EditorGUI.EndChangeCheck())
            {
                if (newEnabled)
                {
                    if (!currentDefines.Contains(define))
                    {
                        currentDefines = string.IsNullOrEmpty(
                            currentDefines
                        )
                            ? define
                            : currentDefines + ";" + define;
                    }
                }
                else
                {
                    currentDefines = string.Join(
                        ";",
                        currentDefines.Split(';')
                            .Where(d => d.Trim() != define)
                    );
                }

                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    buildTarget, currentDefines
                );
            }
        }

        #endregion

        #region Tab — Translators (single-select)

        private void DrawTranslatorsTab()
        {
            EditorGUILayout.HelpBox(
                "Translators handle encoding/decoding. "
                + "Only ONE translator can be active at a time. "
                + "Enabling a new one disables the previous.",
                MessageType.None
            );
            EditorGUILayout.Space();

            Type[] translatorTypes =
                GetCachedProviderTypes(typeof(ISaveTranslator));

            if (translatorTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing "
                    + "ISaveTranslator found.",
                    MessageType.Info
                );
                return;
            }

            SerializedProperty translatorsProp =
                _serializedSettings.FindProperty("_translators");

            foreach (Type providerType in translatorTypes)
            {
                DrawSingleSelectProviderCard(
                    providerType, translatorsProp
                );
            }
        }

        /// <summary>
        /// Draws a provider card where only one can be active.
        /// Enabling this one disables all others.
        /// </summary>
        private void DrawSingleSelectProviderCard(
            Type providerType,
            SerializedProperty arrayProp)
        {
            if (arrayProp == null || !arrayProp.isArray) return;

            ScriptableObject existingAsset =
                FindProviderInArray(arrayProp, providerType);
            bool isEnabled = existingAsset != null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Radio-style indicator
                    GUIStyle indicatorStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        normal = { textColor = isEnabled ? Color.green : Color.gray }
                    };
                    EditorGUILayout.LabelField(
                        isEnabled ? "\u25C9" : "\u25CB",
                        indicatorStyle,
                        GUILayout.Width(20)
                    );

                    EditorGUILayout.LabelField(
                        ObjectNames.NicifyVariableName(providerType.Name),
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    if (isEnabled)
                    {
                        // Show "Active" badge
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                        GUILayout.Label(
                            "ACTIVE",
                            EditorStyles.miniButton,
                            GUILayout.Width(60)
                        );
                        GUI.backgroundColor = Color.white;

                        if (DrawSmallDeleteButton())
                        {
                            if (EditorUtility.DisplayDialog(
                                    "Delete Translator",
                                    $"Delete '{existingAsset.name}' "
                                    + "from disk?",
                                    "Delete", "Cancel"))
                            {
                                string assetPath =
                                    AssetDatabase.GetAssetPath(existingAsset);
                                RemoveFromArray(arrayProp, existingAsset);
                                EditorUtility.SetDirty(
                                    _serializedSettings.targetObject
                                );
                                AssetDatabase.SaveAssets();
                                if (!string.IsNullOrEmpty(assetPath))
                                {
                                    AssetDatabase.DeleteAsset(assetPath);
                                    AssetDatabase.SaveAssets();
                                }
                            }
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(
                                "Enable", GUILayout.Width(70)))
                        {
                            // Single-select: clear all others first
                            while (arrayProp.arraySize > 0)
                                arrayProp.DeleteArrayElementAtIndex(0);

                            EnableProvider(arrayProp, providerType);
                            return;
                        }
                    }
                }

                if (isEnabled && existingAsset != null)
                {
                    GUILayout.Space(4);
                    DrawInlineInspectorFor(existingAsset);
                }
            }
        }

        #endregion

        #region Tab — Adapters (sub-tabs)

        private void DrawAdaptersTab()
        {
            _adapterSubTabIndex = GUILayout.Toolbar(
                _adapterSubTabIndex, AdapterSubTabNames
            );
            EditorGUILayout.Space();

            switch (_adapterSubTabIndex)
            {
                case 0: DrawManageAdaptersTab(); break;
                case 1: DrawGroupOrderTab("_loadOrder", "Load"); break;
                case 2: DrawGroupOrderTab("_saveOrder", "Save"); break;
            }
        }

        #endregion

        #region Sub-tab — Manage Adapters

        private void DrawManageAdaptersTab()
        {
            EditorGUILayout.HelpBox(
                "Create, enable, and delete adapter assets here. "
                + "Use Load Orders / Save Orders tabs to configure "
                + "which adapters are used and in what priority.",
                MessageType.None
            );
            EditorGUILayout.Space();

            Type[] adapterTypes =
                GetCachedProviderTypes(typeof(ISaveAdapter));

            if (adapterTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableObject types implementing "
                    + "ISaveAdapter found.",
                    MessageType.Info
                );
                return;
            }

            // Collect adapters used in any group
            HashSet<ScriptableObject> usedInGroups =
                CollectAllGroupAdapters();

            _manageScrollPosition = EditorGUILayout.BeginScrollView(
                _manageScrollPosition
            );

            foreach (Type providerType in adapterTypes)
            {
                ScriptableObject existingAsset =
                    FindExistingAssetOnDisk(providerType);
                bool isUsed = existingAsset != null
                              && usedInGroups.Contains(existingAsset);

                DrawManageAdapterCard(providerType, existingAsset, isUsed);
            }

            EditorGUILayout.EndScrollView();
        }

        private HashSet<ScriptableObject> CollectAllGroupAdapters()
        {
            var used = new HashSet<ScriptableObject>();

            foreach (string fieldName in new[] { "_loadOrder", "_saveOrder" })
            {
                SerializedProperty orderProp =
                    _serializedSettings.FindProperty(fieldName);
                if (orderProp == null || !orderProp.isArray) continue;

                for (int g = 0; g < orderProp.arraySize; g++)
                {
                    SerializedProperty groupProp =
                        orderProp.GetArrayElementAtIndex(g);
                    SerializedProperty adaptersProp =
                        groupProp.FindPropertyRelative("_adapters");
                    if (adaptersProp == null || !adaptersProp.isArray)
                        continue;

                    for (int a = 0; a < adaptersProp.arraySize; a++)
                    {
                        ScriptableObject so =
                            adaptersProp.GetArrayElementAtIndex(a)
                                .objectReferenceValue as ScriptableObject;
                        if (so != null) used.Add(so);
                    }
                }
            }

            return used;
        }

        private void DrawManageAdapterCard(
            Type providerType,
            ScriptableObject existingAsset,
            bool isUsedInGroup)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        ObjectNames.NicifyVariableName(providerType.Name),
                        EditorStyles.boldLabel
                    );

                    if (isUsedInGroup)
                    {
                        GUI.backgroundColor = new Color(0.3f, 0.5f, 0.7f);
                        GUILayout.Label(
                            "IN USE",
                            EditorStyles.miniButton,
                            GUILayout.Width(60)
                        );
                        GUI.backgroundColor = Color.white;
                    }

                    // Adapter availability badge
                    if (existingAsset is ISaveAdapter adapter)
                    {
                        bool available = adapter.IsAvailable;
                        Color badgeColor = available
                            ? new Color(0.25f, 0.6f, 0.3f)
                            : new Color(0.7f, 0.35f, 0.3f);
                        string badgeText = available ? "OK" : "OFF";
                        GUI.backgroundColor = badgeColor;
                        GUILayout.Label(
                            badgeText,
                            EditorStyles.miniButton,
                            GUILayout.Width(35)
                        );
                        GUI.backgroundColor = Color.white;
                    }

                    GUILayout.FlexibleSpace();

                    if (existingAsset != null)
                    {
                        if (GUILayout.Button(
                                "Ping", EditorStyles.miniButton,
                                GUILayout.Width(40)))
                        {
                            EditorGUIUtility.PingObject(existingAsset);
                        }

                        if (DrawSmallDeleteButton())
                        {
                            if (EditorUtility.DisplayDialog(
                                    "Delete Adapter",
                                    $"Delete '{existingAsset.name}' "
                                    + "from disk?\n\n"
                                    + "This will also remove it from "
                                    + "any groups.",
                                    "Delete", "Cancel"))
                            {
                                // Remove from all groups first
                                RemoveAdapterFromAllGroups(existingAsset);

                                string assetPath =
                                    AssetDatabase.GetAssetPath(existingAsset);
                                if (!string.IsNullOrEmpty(assetPath))
                                {
                                    AssetDatabase.DeleteAsset(assetPath);
                                    AssetDatabase.SaveAssets();
                                }

                                EditorUtility.SetDirty(
                                    _serializedSettings.targetObject
                                );
                                AssetDatabase.SaveAssets();
                            }
                            return;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(
                                "Create", EditorStyles.miniButton,
                                GUILayout.Width(55)))
                        {
                            ScriptableObject asset =
                                FindOrCreateAdapterAsset(providerType);
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }

                if (existingAsset != null)
                {
                    GUILayout.Space(4);
                    DrawInlineInspectorFor(existingAsset);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Asset not yet created. Click Create.",
                        EditorStyles.miniLabel
                    );
                }
            }
        }

        private void RemoveAdapterFromAllGroups(ScriptableObject adapter)
        {
            foreach (string fieldName in new[]
                         { "_loadOrder", "_saveOrder" })
            {
                SerializedProperty orderProp =
                    _serializedSettings.FindProperty(fieldName);
                if (orderProp == null || !orderProp.isArray) continue;

                for (int g = orderProp.arraySize - 1; g >= 0; g--)
                {
                    SerializedProperty groupProp =
                        orderProp.GetArrayElementAtIndex(g);
                    SerializedProperty adaptersProp =
                        groupProp.FindPropertyRelative("_adapters");
                    if (adaptersProp == null || !adaptersProp.isArray)
                        continue;

                    for (int a = adaptersProp.arraySize - 1;
                         a >= 0;
                         a--)
                    {
                        if (adaptersProp.GetArrayElementAtIndex(a)
                                .objectReferenceValue == adapter)
                        {
                            adaptersProp.DeleteArrayElementAtIndex(a);
                        }
                    }

                    CleanNullArrayEntries(adaptersProp);
                }
            }
        }

        #endregion

        #region Group Order Tab (shared by Load/Save)

        private void DrawGroupOrderTab(
            string fieldName,
            string label)
        {
            SerializedProperty orderProp =
                _serializedSettings.FindProperty(fieldName);

            if (orderProp == null)
            {
                EditorGUILayout.HelpBox(
                    $"Field '{fieldName}' not found.",
                    MessageType.Error
                );
                return;
            }

            string helpText = label == "Save"
                ? "Each group writes to the first available adapter. "
                  + "Groups are tried top-to-bottom."
                : "Each group tries adapters until data is found. "
                  + "Groups cascade top-to-bottom.";

            EditorGUILayout.HelpBox(helpText, MessageType.None);
            EditorGUILayout.Space();

            // Draw groups
            for (int g = 0; g < orderProp.arraySize; g++)
            {
                SerializedProperty groupProp =
                    orderProp.GetArrayElementAtIndex(g);
                SerializedProperty adaptersProp =
                    groupProp.FindPropertyRelative("_adapters");

                DrawGroupCard(orderProp, g, adaptersProp, label);
            }

            if (orderProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No {label.ToLower()} groups configured. "
                    + "Add a group below.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        $"+ Add {label} Group",
                        GUILayout.Height(26),
                        GUILayout.Width(160)))
                {
                    AddGroup(orderProp);
                }
                GUILayout.FlexibleSpace();
            }
        }

        #endregion

        #region Group Card

        private void DrawGroupCard(
            SerializedProperty orderProp,
            int groupIndex,
            SerializedProperty adaptersProp,
            string orderLabel)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ── Group header ──
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect badgeRect = GUILayoutUtility.GetRect(
                        22, 18,
                        GUILayout.Width(22),
                        GUILayout.Height(18)
                    );
                    EditorGUI.DrawRect(
                        badgeRect,
                        new Color(0.25f, 0.45f, 0.65f, 0.8f)
                    );
                    GUI.Label(
                        badgeRect,
                        (groupIndex + 1).ToString(),
                        new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = Color.white }
                        }
                    );

                    EditorGUILayout.LabelField(
                        $"Group {groupIndex + 1}",
                        EditorStyles.boldLabel
                    );

                    GUILayout.FlexibleSpace();

                    // ▲▼ reorder group
                    using (new EditorGUI.DisabledScope(groupIndex == 0))
                    {
                        if (GUILayout.Button(
                                "\u25B2",
                                EditorStyles.miniButtonLeft,
                                GUILayout.Width(22),
                                GUILayout.Height(18)))
                        {
                            orderProp.MoveArrayElement(
                                groupIndex, groupIndex - 1
                            );
                        }
                    }
                    using (new EditorGUI.DisabledScope(
                               groupIndex >= orderProp.arraySize - 1))
                    {
                        if (GUILayout.Button(
                                "\u25BC",
                                EditorStyles.miniButtonRight,
                                GUILayout.Width(22),
                                GUILayout.Height(18)))
                        {
                            orderProp.MoveArrayElement(
                                groupIndex, groupIndex + 1
                            );
                        }
                    }

                    // Delete group
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button(
                            "\u2717",
                            EditorStyles.miniButton,
                            GUILayout.Width(22),
                            GUILayout.Height(18)))
                    {
                        orderProp.DeleteArrayElementAtIndex(groupIndex);
                        GUI.backgroundColor = Color.white;
                        return;
                    }
                    GUI.backgroundColor = Color.white;

                    // Edit adapters
                    if (GUILayout.Button(
                            "\u270E",
                            EditorStyles.miniButton,
                            GUILayout.Width(22),
                            GUILayout.Height(18)))
                    {
                        PopupWindow.Show(
                            GUILayoutUtility
                                .GetLastRect(),
                            CreateAdapterCheckboxPopup(
                                adaptersProp,
                                orderProp,
                                groupIndex
                            )
                        );
                    }
                }

                // ── Separator ──
                Rect sep = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(
                    sep, new Color(0.5f, 0.5f, 0.5f, 0.2f)
                );
                GUILayout.Space(2);

                // ── Adapters within group ──
                if (adaptersProp != null && adaptersProp.isArray)
                {
                    if (adaptersProp.arraySize == 0)
                    {
                        EditorGUILayout.LabelField(
                            "(no adapters)",
                            EditorStyles.miniLabel
                        );
                    }

                    for (int a = 0;
                         a < adaptersProp.arraySize;
                         a++)
                    {
                        SerializedProperty element =
                            adaptersProp.GetArrayElementAtIndex(a);
                        ScriptableObject adapter =
                            element.objectReferenceValue
                                as ScriptableObject;

                        DrawGroupAdapterRow(
                            adaptersProp, a, adapter
                        );
                    }
                }
            }
        }

        #endregion

        #region Group Adapter Row

        private void DrawGroupAdapterRow(
            SerializedProperty adaptersProp,
            int adapterIndex,
            ScriptableObject adapter)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);

                string displayName = adapter != null
                    ? ObjectNames.NicifyVariableName(
                        adapter.GetType().Name
                    )
                    : "<missing>";

                EditorGUILayout.LabelField(displayName);

                GUILayout.FlexibleSpace();

                // ▲▼ reorder within group
                using (new EditorGUI.DisabledScope(adapterIndex == 0))
                {
                    if (GUILayout.Button(
                            "\u25B2",
                            EditorStyles.miniButtonLeft,
                            GUILayout.Width(20),
                            GUILayout.Height(16)))
                    {
                        adaptersProp.MoveArrayElement(
                            adapterIndex, adapterIndex - 1
                        );
                    }
                }
                using (new EditorGUI.DisabledScope(
                           adapterIndex
                           >= adaptersProp.arraySize - 1))
                {
                    if (GUILayout.Button(
                            "\u25BC",
                            EditorStyles.miniButtonRight,
                            GUILayout.Width(20),
                            GUILayout.Height(16)))
                    {
                        adaptersProp.MoveArrayElement(
                            adapterIndex, adapterIndex + 1
                        );
                    }
                }

                // ✕ remove from group
                GUI.backgroundColor = new Color(1f, 0.5f, 0.4f);
                if (GUILayout.Button(
                        "\u2717",
                        EditorStyles.miniButton,
                        GUILayout.Width(20),
                        GUILayout.Height(16)))
                {
                    adaptersProp.DeleteArrayElementAtIndex(
                        adapterIndex
                    );
                    CleanNullArrayEntries(adaptersProp);
                    GUI.backgroundColor = Color.white;
                    return;
                }
                GUI.backgroundColor = Color.white;
            }
        }

        #endregion

        #region Add Adapter Dropdown

        private AdapterCheckboxPopup CreateAdapterCheckboxPopup(
            SerializedProperty adaptersProp,
            SerializedProperty orderProp,
            int currentGroupIndex)
        {
            Type[] allTypes =
                GetCachedProviderTypes(typeof(ISaveAdapter));

            // Types already in THIS group
            var inThisGroup = new HashSet<Type>();
            for (int i = 0; i < adaptersProp.arraySize; i++)
            {
                ScriptableObject so =
                    adaptersProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue as ScriptableObject;
                if (so != null) inThisGroup.Add(so.GetType());
            }

            // Types in OTHER groups (within the same order)
            var inOtherGroups = new HashSet<Type>();
            for (int g = 0; g < orderProp.arraySize; g++)
            {
                if (g == currentGroupIndex) continue;

                SerializedProperty otherGroup =
                    orderProp.GetArrayElementAtIndex(g);
                SerializedProperty otherAdapters =
                    otherGroup.FindPropertyRelative("_adapters");
                if (otherAdapters == null || !otherAdapters.isArray)
                    continue;

                for (int a = 0; a < otherAdapters.arraySize; a++)
                {
                    ScriptableObject so =
                        otherAdapters.GetArrayElementAtIndex(a)
                            .objectReferenceValue as ScriptableObject;
                    if (so != null) inOtherGroups.Add(so.GetType());
                }
            }

            return new AdapterCheckboxPopup(
                allTypes,
                inThisGroup,
                inOtherGroups,
                (type, add) =>
                {
                    if (add)
                    {
                        ScriptableObject asset =
                            FindOrCreateAdapterAsset(type);
                        AddToArray(adaptersProp, asset);
                    }
                    else
                    {
                        RemoveTypeFromArray(
                            adaptersProp, type
                        );
                    }

                    _serializedSettings.ApplyModifiedProperties();
                    EditorUtility.SetDirty(
                        _serializedSettings.targetObject
                    );
                    AssetDatabase.SaveAssets();
                }
            );
        }

        private static void RemoveTypeFromArray(
            SerializedProperty arrayProp,
            Type providerType)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                ScriptableObject so =
                    arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue as ScriptableObject;
                if (so != null
                    && providerType.IsInstanceOfType(so))
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            CleanNullArrayEntries(arrayProp);
        }

        /// <summary>
        /// Popup with checkboxes for all adapter types.
        /// Changes are batched — applied only on Save.
        /// Checked = in this group (editable). Grayed = in another group.
        /// </summary>
        private sealed class AdapterCheckboxPopup
            : PopupWindowContent
        {
            private readonly Type[] _allTypes;
            private readonly HashSet<Type> _inOtherGroups;
            private readonly Action<Type, bool> _onToggle;

            /// <summary>Snapshot at open time — used to compute
            /// add/remove diffs when the user presses Save.</summary>
            private readonly HashSet<Type> _originalSelection;

            /// <summary>Local working copy modified by checkboxes.</summary>
            private readonly HashSet<Type> _localSelection;

            private Vector2 _scroll;

            public AdapterCheckboxPopup(
                Type[] allTypes,
                HashSet<Type> initialSelection,
                HashSet<Type> inOtherGroups,
                Action<Type, bool> onToggle)
            {
                _allTypes = allTypes;
                _inOtherGroups = inOtherGroups;
                _onToggle = onToggle;
                _originalSelection =
                    new HashSet<Type>(initialSelection);
                _localSelection =
                    new HashSet<Type>(initialSelection);
            }

            public override Vector2 GetWindowSize()
            {
                float h = Mathf.Min(
                    _allTypes.Length * 26f + 12f + 40f,
                    420f
                );
                return new Vector2(290f, Mathf.Max(h, 80f));
            }

            public override void OnGUI(Rect rect)
            {
                if (_allTypes.Length == 0)
                {
                    EditorGUILayout.LabelField(
                        "No adapters found.",
                        EditorStyles.centeredGreyMiniLabel
                    );
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(
                    _scroll,
                    GUILayout.Height(rect.height - 40f)
                );

                foreach (Type t in _allTypes)
                {
                    string displayName =
                        ObjectNames.NicifyVariableName(t.Name);
                    bool isChecked = _localSelection.Contains(t);
                    bool isInOtherGroup =
                        !isChecked && _inOtherGroups.Contains(t);

                    using (new EditorGUI.DisabledScope(isInOtherGroup))
                    {
                        Rect rowRect =
                            EditorGUILayout.GetControlRect(
                                false, 22f
                            );

                        Rect toggleRect = new Rect(
                            rowRect.x + 4, rowRect.y + 3,
                            16, 16
                        );
                        bool newChecked = GUI.Toggle(
                            toggleRect, isChecked, ""
                        );

                        Rect labelRect = new Rect(
                            rowRect.x + 24, rowRect.y,
                            rowRect.width - 30, rowRect.height
                        );
                        GUI.Label(
                            labelRect,
                            displayName
                        );

                        if (newChecked != isChecked)
                        {
                            if (newChecked)
                                _localSelection.Add(t);
                            else
                                _localSelection.Remove(t);
                        }
                    }

                    if (isInOtherGroup
                        && Event.current.type == EventType.Repaint)
                    {
                        Rect last = GUILayoutUtility.GetLastRect();
                        EditorGUI.DrawRect(
                            last,
                            new Color(0.35f, 0.35f, 0.35f, 0.3f)
                        );
                    }
                }

                EditorGUILayout.EndScrollView();

                // ── Save / Cancel buttons ──
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(
                            "Cancel", GUILayout.Width(70)))
                    {
                        editorWindow.Close();
                    }

                    if (GUILayout.Button(
                            "Save", GUILayout.Width(70)))
                    {
                        CommitChanges();
                        editorWindow.Close();
                    }
                }
            }

            private void CommitChanges()
            {
                // Types added since open
                foreach (Type t in _localSelection)
                {
                    if (!_originalSelection.Contains(t))
                        _onToggle?.Invoke(t, true);
                }

                // Types removed since open
                foreach (Type t in _originalSelection)
                {
                    if (!_localSelection.Contains(t))
                        _onToggle?.Invoke(t, false);
                }
            }
        }

        #endregion

        #region Group Operations

        private static void AddGroup(SerializedProperty orderProp)
        {
            int newIndex = orderProp.arraySize++;
            SerializedProperty newGroup =
                orderProp.GetArrayElementAtIndex(newIndex);
            SerializedProperty adapters =
                newGroup.FindPropertyRelative("_adapters");
            adapters.ClearArray();
        }

        private static void AddToArray(
            SerializedProperty arrayProp,
            ScriptableObject asset)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue == asset)
                    return;
            }

            int newIndex = arrayProp.arraySize++;
            arrayProp.GetArrayElementAtIndex(newIndex)
                .objectReferenceValue = asset;
            CleanNullArrayEntries(arrayProp);
        }

        #endregion

        #region Adapter Asset Helpers

        private ScriptableObject FindOrCreateAdapterAsset(Type providerType)
        {
            ScriptableObject asset =
                FindExistingAssetOnDisk(providerType);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance(providerType);
                asset.name = providerType.Name;

                if (!Directory.Exists(ProviderAssetFolder))
                    Directory.CreateDirectory(ProviderAssetFolder);

                string assetPath =
                    AssetDatabase.GenerateUniqueAssetPath(
                        Path.Combine(
                            ProviderAssetFolder,
                            providerType.Name + ".asset"
                        )
                    );
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            return asset;
        }

        private static ScriptableObject FindExistingAssetOnDisk(
            Type providerType)
        {
            string[] guids =
                AssetDatabase.FindAssets("t:" + providerType.Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset =
                    AssetDatabase.LoadAssetAtPath(
                        path, providerType
                    ) as ScriptableObject;
                if (asset != null) return asset;
            }

            return null;
        }

        #endregion

        #region Inline Inspector (cached per target)

        private void DrawInlineInspectorFor(ScriptableObject target)
        {
            if (target == null) return;

            _inlineEditorCache ??=
                new Dictionary<ScriptableObject, UnityEditor.Editor>();

            if (!_inlineEditorCache.TryGetValue(
                    target,
                    out UnityEditor.Editor editor))
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                _inlineEditorCache[target] = editor;
            }

            editor.OnInspectorGUI();
        }

        private void DestroyInlineEditors()
        {
            if (_inlineEditorCache == null) return;

            foreach (UnityEditor.Editor editor
                     in _inlineEditorCache.Values)
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);
            }

            _inlineEditorCache.Clear();
        }

        #endregion

        #region Provider — Find / Add / Remove

        private static Type[] GetCachedProviderTypes(Type interfaceType)
        {
            if (interfaceType == typeof(ISaveTranslator))
            {
                _cachedTranslatorTypes ??=
                    ScanProviderTypes(typeof(ISaveTranslator));
                return _cachedTranslatorTypes;
            }

            if (interfaceType == typeof(ISaveAdapter))
            {
                _cachedAdapterTypes ??=
                    ScanProviderTypes(typeof(ISaveAdapter));
                return _cachedAdapterTypes;
            }

            return ScanProviderTypes(interfaceType);
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

        private static ScriptableObject FindProviderInArray(
            SerializedProperty arrayProp,
            Type providerType)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                ScriptableObject asset =
                    arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue as ScriptableObject;
                if (asset != null
                    && providerType.IsInstanceOfType(asset))
                    return asset;
            }

            return null;
        }

        private void EnableProvider(
            SerializedProperty arrayProp,
            Type providerType)
        {
            ScriptableObject asset =
                FindExistingAssetOnDisk(providerType);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance(providerType);
                asset.name = providerType.Name;

                if (!Directory.Exists(ProviderAssetFolder))
                    Directory.CreateDirectory(ProviderAssetFolder);

                string assetPath =
                    AssetDatabase.GenerateUniqueAssetPath(
                        Path.Combine(
                            ProviderAssetFolder,
                            providerType.Name + ".asset"
                        )
                    );
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            AddToArray(arrayProp, asset);
            EditorUtility.SetDirty(_serializedSettings.targetObject);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
        }

        private static void RemoveFromArray(
            SerializedProperty arrayProp,
            ScriptableObject asset)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                if (arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue == asset)
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            CleanNullArrayEntries(arrayProp);
        }

        private static void CleanNullArrayEntries(
            SerializedProperty arrayProp)
        {
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)
            {
                if (arrayProp.GetArrayElementAtIndex(i)
                        .objectReferenceValue == null)
                    arrayProp.DeleteArrayElementAtIndex(i);
            }
        }

        private static bool DrawSmallDeleteButton()
        {
            GUIContent content = EditorGUIUtility.IconContent(
                "TreeEditor.Trash"
            );
            if (content == null || content.image == null)
                content = new GUIContent("\u2717", "Delete");

            content.tooltip = "Delete the provider asset from disk.";
            return GUILayout.Button(
                content,
                EditorStyles.miniButton,
                GUILayout.Width(24),
                GUILayout.Height(18)
            );
        }

        #endregion

        #region Tab — Preview

        private void DrawPreviewTab()
        {
            DataSyncConfiguration config =
                _serializedSettings.targetObject
                    as DataSyncConfiguration;

            if (config == null) return;

            EditorGUILayout.HelpBox(
                "Current configuration summary. "
                + "Enter Play Mode to see live state.",
                MessageType.None
            );
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Configuration Summary",
                    EditorStyles.miniBoldLabel
                );

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField(
                        "Translator Count",
                        config.Translators?.Length ?? 0
                    );
                    EditorGUILayout.IntField(
                        "Save Groups",
                        config.SaveOrder?.Length ?? 0
                    );
                    EditorGUILayout.IntField(
                        "Load Groups",
                        config.LoadOrder?.Length ?? 0
                    );
                    int totalSaveAdapters =
                        config.SaveOrder?.Sum(g => g.Count) ?? 0;
                    int totalLoadAdapters =
                        config.LoadOrder?.Sum(g => g.Count) ?? 0;
                    EditorGUILayout.IntField(
                        "Adapters (save)", totalSaveAdapters
                    );
                    EditorGUILayout.IntField(
                        "Adapters (load)", totalLoadAdapters
                    );
                }
            }
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar(DataSyncConfiguration config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int translatorCount = config.Translators?.Length ?? 0;
                int saveGroups = config.SaveOrder?.Length ?? 0;
                int loadGroups = config.LoadOrder?.Length ?? 0;
                bool valid =
                    translatorCount > 0 && saveGroups > 0
                                        && loadGroups > 0;

                string statusText = valid ? "Ready" : "Incomplete";
                Color statusColor =
                    valid ? Color.green : Color.yellow;

                _statusLabelStyle ??=
                    new GUIStyle(EditorStyles.miniLabel);
                _statusLabelStyle.normal.textColor = statusColor;

                EditorGUILayout.LabelField(
                    string.Format(
                        "Status: {0}  |  {1} translators, "
                        + "{2} save groups, {3} load groups",
                        statusText, translatorCount,
                        saveGroups, loadGroups
                    ),
                    _statusLabelStyle
                );
            }
        }

        #endregion

        #region Asset Helpers

        private static DataSyncConfiguration GetOrCreateSettings()
        {
            DataSyncConfiguration settings = AssetDatabase
                .LoadAssetAtPath<DataSyncConfiguration>(
                    SettingsAssetPath
                );

            if (settings != null) return settings;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "No Data Sync Configuration asset found. "
                + "Click the button below to create one.",
                MessageType.Info
            );

            if (GUILayout.Button(
                    "Create Data Sync Configuration",
                    GUILayout.Height(30)))
            {
                CreateSettingsAsset();
            }

            return null;
        }

        private static void CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourcesFolder))
                Directory.CreateDirectory(ResourcesFolder);

            if (!Directory.Exists(ConfigFolder))
                Directory.CreateDirectory(ConfigFolder);

            DataSyncConfiguration settings =
                ScriptableObject.CreateInstance<DataSyncConfiguration>();

            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[DataSync] Created configuration asset at '"
                + SettingsAssetPath + "'."
            );
        }

        #endregion

        #region Utility

        private void EnsureSerializedObject(
            DataSyncConfiguration settings)
        {
            if (_serializedSettings != null
                && _serializedSettings.targetObject == settings)
                return;

            DestroyInlineEditors();
            _serializedSettings = new SerializedObject(settings);
        }

        private void DrawSerializedProperty(
            string propertyName,
            string label)
        {
            SerializedProperty prop =
                _serializedSettings.FindProperty(propertyName);
            if (prop == null)
            {
                EditorGUILayout.LabelField(
                    label,
                    $"<missing: {propertyName}>"
                );
                return;
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        #endregion
    }
}
