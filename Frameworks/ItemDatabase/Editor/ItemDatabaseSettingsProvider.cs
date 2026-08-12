using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.ItemDatabase;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    /// <summary>
    /// Project Settings > Frameworks > Item Database.
    /// Card-based management of item definitions, discoverable tags,
    /// the middleware pipeline, and a read-only configuration preview.
    /// </summary>
    internal sealed class ItemDatabaseSettingsProvider : SettingsProvider
    {
        #region Constants

        private const string ConfigAssetPath =
            "Assets/Resources/Integration/Managers/ItemDatabaseConfiguration.asset";

        private const string ConfigFolder =
            "Assets/Resources/Integration/Managers";

        private const string TabPrefKey =
            "ItemDatabaseSettingsProvider_SelectedTab";

        private const string OwnerFilterPrefKey =
            "ItemDatabaseSettingsProvider_OwnerFilter";

        private static readonly string[] TabNames =
            { "Definitions", "Tags", "Middlewares", "Preview" };

        private static readonly string[] OwnerFilterOptions =
            { "All", "Unowned", "Managed by IngameCurrency" };

        #endregion

        #region Private Fields

        private ItemDatabaseConfiguration _config;
        private int _selectedTabIndex;
        private int _ownerFilterIndex;
        private Vector2 _scrollPosition;
        private Vector2 _liveScrollPosition;
        private string _searchText = string.Empty;
        private string _tagSearchText = string.Empty;
        private string _middlewareSearchText = string.Empty;

        private readonly Dictionary<int, UnityEditor.Editor> _cachedEditors
            = new Dictionary<int, UnityEditor.Editor>();

        private static Type[] _cachedTagTypes;
        private static Type[] _cachedMiddlewareTypes;

        #endregion

        #region Constructor

        private ItemDatabaseSettingsProvider(
            string path,
            SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords)
        {
            _selectedTabIndex = Mathf.Clamp(
                EditorPrefs.GetInt(TabPrefKey, 0),
                0, TabNames.Length - 1);
            _ownerFilterIndex = EditorPrefs.GetInt(OwnerFilterPrefKey, 0);
        }

        #endregion

        #region SettingsProvider Registration

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new ItemDatabaseSettingsProvider(
                "Project/Frameworks/Item Database",
                SettingsScope.Project,
                new[]
                {
                    "item", "inventory", "tag", "definition",
                    "stackable", "currency", "armor", "weapon",
                    "middleware", "sync"
                }
            );
        }

        #endregion

        #region GUI

        public override void OnGUI(string searchContext)
        {
            try
            {
                base.OnGUI(searchContext);

                _config = GetOrCreateSettings();
                if (_config == null)
                {
                    EditorGUILayout.HelpBox(
                        "Failed to create or load ItemDatabaseConfiguration.",
                        MessageType.Error);
                    return;
                }

                DrawManagerCardHeader();

                EditorGUILayout.Space();
                int newTab = GUILayout.Toolbar(_selectedTabIndex, TabNames);
                if (newTab != _selectedTabIndex)
                {
                    _selectedTabIndex = newTab;
                    EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
                    DestroyInlineEditors();
                }
                EditorGUILayout.Space();

                Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(dividerRect,
                    new Color(0.5f, 0.5f, 0.5f, 0.3f));
                EditorGUILayout.Space();

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                switch (_selectedTabIndex)
                {
                    case 0: DrawDefinitionsTab(); break;
                    case 1: DrawTagsTab(); break;
                    case 2: DrawMiddlewaresTab(); break;
                    case 3: DrawPreviewTab(); break;
                }

                EditorGUILayout.EndScrollView();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ItemDatabaseSettings] {ex}");
            }
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();
            DestroyInlineEditors();
            EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
            EditorPrefs.SetInt(OwnerFilterPrefKey, _ownerFilterIndex);
        }

        #endregion

        #region Manager Card Header

        private void DrawManagerCardHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Item Database Configuration", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    bool hasConfig = _config != null;
                    DrawBadge(hasConfig ? "ACTIVE" : "MISSING",
                        hasConfig
                            ? new Color(0.2f, 0.7f, 0.2f)
                            : new Color(0.8f, 0.3f, 0.2f));

                    if (hasConfig && GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(_config);
                    }
                }

                if (_config != null)
                {
                    EditorGUILayout.LabelField(
                        $"Definitions: {_config.ItemDefinitions.Length}"
                        + $"  |  Tags: {_config.TagDefinitions.Length}"
                        + $"  |  Middlewares: {_config.MiddlewareTypeNames.Length}",
                        EditorStyles.miniLabel);
                }
            }
        }

        #endregion

        #region Tab — Definitions

        private void DrawDefinitionsTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _searchText = EditorGUILayout.TextField(_searchText,
                    EditorStyles.toolbarSearchField);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+ New Definition", GUILayout.Width(140)))
                {
                    CreateNewDefinition();
                }
            }
            EditorGUILayout.Space();

            var definitions = _config.ItemDefinitions ?? Array.Empty<ItemDefinition>();
            foreach (var def in definitions)
            {
                if (def == null) continue;
                if (MatchesSearch(def, _searchText)) DrawDefinitionCard(def);
            }

            if (definitions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No item definitions yet. Click '+ New Definition' to create one.",
                    MessageType.Info);
            }
        }

        private void DrawDefinitionCard(ItemDefinition def)
        {
            bool isOwned = !string.IsNullOrEmpty(ItemDatabase.GetDefinitionOwner(def.ItemId));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool expanded = IsFolded(def);
                    bool foldout = EditorGUILayout.Foldout(expanded,
                        string.IsNullOrEmpty(def.ItemId) ? "(unnamed)" : def.ItemId, true);
                    if (foldout != expanded) SetFolded(def, foldout);

                    if (isOwned) DrawBadge("Managed", new Color(0.55f, 0.4f, 0.75f));

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                        EditorGUIUtility.PingObject(def);

                    GUI.enabled = !isOwned;
                    if (DrawSmallDeleteButton())
                    {
                        DeleteDefinition(def);
                        GUI.enabled = true;
                        return;
                    }
                    GUI.enabled = true;
                }

                if (IsFolded(def))
                {
                    // The custom ItemDefinitionEditor renders identity fields
                    // + the full tag management section.
                    DrawInlineInspectorFor(def);
                }
            }
        }

        private void MarkConfigDirty()
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ConfigAssetPath);
        }

        private void CreateNewDefinition()
        {
            int index = _config.ItemDefinitions?.Length ?? 0;
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.name = "New Item Definition";
            def.ItemId = $"item_{index}";

            var metadata = ScriptableObject.CreateInstance<ItemMetadata>();
            metadata.name = $"{def.ItemId}_meta";

            AssetDatabase.AddObjectToAsset(metadata, _config);
            AssetDatabase.AddObjectToAsset(def, _config);
            def.Metadata = metadata;

            var list = new List<ItemDefinition>(_config.ItemDefinitions ?? Array.Empty<ItemDefinition>())
            { def };
            _config.ItemDefinitions = list.ToArray();

            MarkConfigDirty();
        }

        private void DeleteDefinition(ItemDefinition def)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Delete Definition",
                $"Delete item definition '{def.ItemId}'?",
                "Delete", "Cancel");
            if (!confirm) return;

            var list = new List<ItemDefinition>(_config.ItemDefinitions ?? Array.Empty<ItemDefinition>());
            list.Remove(def);
            _config.ItemDefinitions = list.ToArray();

            // Destroy per-definition tag instances (sub-assets of config) too,
            // but keep default templates alive.
            ItemDefinitionTagUtils.DestroyDefinitionTags(_config, def);
            ItemDefinitionTagUtils.DestroySubAsset(def.Metadata);
            ItemDefinitionTagUtils.DestroySubAsset(def);

            MarkConfigDirty();
        }

        #endregion

        #region Tab — Tags

        private void DrawTagsTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _tagSearchText = EditorGUILayout.TextField(_tagSearchText,
                    EditorStyles.toolbarSearchField);

                if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                {
                    _cachedTagTypes = null;
                }

                _ownerFilterIndex = EditorGUILayout.Popup(_ownerFilterIndex,
                    OwnerFilterOptions, GUILayout.Width(180));
            }
            EditorGUILayout.Space();

            Type[] tagTypes = ScanTagTypes();

            foreach (var type in tagTypes)
            {
                if (type == null) continue;
                if (!string.IsNullOrEmpty(_tagSearchText)
                    && type.Name.IndexOf(_tagSearchText,
                        StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!MatchesOwnerFilter(type)) continue;

                DrawTagInfoCard(type);
            }
        }

        /// <summary>
        /// Info + defaults card. The default template (stored in
        /// config.TagDefinitions) is the seed applied to every newly
        /// created per-definition tag scriptable of this type.
        /// </summary>
        private void DrawTagInfoCard(Type type)
        {
            bool hasData = TagDataRegistry.GetDataType(type) != null;
            string owner = ItemDatabase.GetTagOwner(type);
            var defaults = FindDefaultTemplate(type);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(type.Name, EditorStyles.boldLabel);

                    if (hasData)
                        DrawBadge("has data", new Color(0.7f, 0.55f, 0.2f));
                    else
                        DrawBadge("marker, no data", new Color(0.55f, 0.4f, 0.75f));

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(owner))
                    {
                        DrawBadge($"Managed by {owner}", new Color(0.55f, 0.4f, 0.75f));
                    }
                }

                var dataType = TagDataRegistry.GetDataType(type);
                EditorGUILayout.LabelField(
                    dataType != null
                        ? $"[TagData] -> {dataType.Name}"
                        : "(no runtime data — marker tag)",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space();
                DrawTagDefaultsEditor(type, defaults, hasData);
            }
        }

        private void DrawTagDefaultsEditor(Type type, TagDefinition defaults, bool hasData)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Default Values",
                    EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.FlexibleSpace();

                if (defaults != null)
                {
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        ClearDefaultTemplate(defaults);
                        return;
                    }
                }
                else if (GUILayout.Button("Create Defaults", EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    CreateDefaultTemplate(type);
                }
            }

            if (defaults != null)
            {
                if (hasData)
                {
                    DrawInlineInspectorFor(defaults);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Marker tag — no configurable values.",
                        EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField(
                    "No defaults set — new tags use the type's built-in defaults.",
                    EditorStyles.miniLabel);
            }
        }

        private TagDefinition FindDefaultTemplate(Type type)
            => ItemDefinitionTagUtils.FindDefaultTemplate(_config, type);

        private void CreateDefaultTemplate(Type type)
        {
            if (FindDefaultTemplate(type) != null) return;

            var tag = (TagDefinition)ScriptableObject.CreateInstance(type);
            tag.name = $"{type.Name}_defaults";

            AssetDatabase.AddObjectToAsset(tag, _config);
            var list = new List<TagDefinition>(_config.TagDefinitions ?? Array.Empty<TagDefinition>())
            { tag };
            _config.TagDefinitions = list.ToArray();

            MarkConfigDirty();
        }

        private void ClearDefaultTemplate(TagDefinition tag)
        {
            var list = new List<TagDefinition>(_config.TagDefinitions ?? Array.Empty<TagDefinition>());
            list.Remove(tag);
            _config.TagDefinitions = list.ToArray();

            ItemDefinitionTagUtils.DestroySubAsset(tag);
            MarkConfigDirty();
        }

        private bool MatchesOwnerFilter(Type type)
        {
            bool isIngameCurrency = ItemDatabase.GetTagOwner(type) == "IngameCurrency";
            switch (_ownerFilterIndex)
            {
                case 0: return true;
                case 1: return !isIngameCurrency;
                case 2: return isIngameCurrency;
                default: return true;
            }
        }

        #endregion

        #region Tab — Middlewares

        private void DrawMiddlewaresTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _middlewareSearchText = EditorGUILayout.TextField(_middlewareSearchText,
                    EditorStyles.toolbarSearchField);

                if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                {
                    _cachedMiddlewareTypes = null;
                }
            }
            EditorGUILayout.Space();

            Type[] middlewareTypes = ScanMiddlewareTypes();
            var configured = new List<string>(_config.MiddlewareTypeNames ?? Array.Empty<string>());

            for (int i = 0; i < middlewareTypes.Length; i++)
            {
                var type = middlewareTypes[i];
                if (type == null) continue;
                if (!string.IsNullOrEmpty(_middlewareSearchText)
                    && type.Name.IndexOf(_middlewareSearchText,
                        StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawMiddlewareCard(type, i, configured);
            }
        }

        private void DrawMiddlewareCard(Type type, int index, List<string> configured)
        {
            string typeName = type.AssemblyQualifiedName;
            bool isEnabled = configured.Contains(typeName);
            var attr = type.GetCustomAttribute<ItemDatabaseMiddlewareAttribute>();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(type.Name, EditorStyles.boldLabel);

                    DrawBadge($"Priority: {attr?.DefaultPriority ?? 500}",
                        new Color(0.7f, 0.55f, 0.2f));

                    if (isEnabled) DrawBadge("ENABLED", new Color(0.2f, 0.7f, 0.2f));

                    GUILayout.FlexibleSpace();

                    if (isEnabled)
                    {
                        if (GUILayout.Button("Disable", GUILayout.Width(70)))
                        {
                            configured.Remove(typeName);
                            SaveMiddlewareList(configured);
                            return;
                        }

                        if (index > 0 && GUILayout.Button("^", GUILayout.Width(28)))
                        {
                            MoveMiddleware(configured, index, -1);
                            return;
                        }
                        if (index < _cachedMiddlewareTypes.Length - 1
                            && GUILayout.Button("v", GUILayout.Width(28)))
                        {
                            MoveMiddleware(configured, index, 1);
                            return;
                        }
                    }
                    else if (GUILayout.Button("Enable", GUILayout.Width(70)))
                    {
                        configured.Add(typeName);
                        SaveMiddlewareList(configured);
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(attr?.Description))
                {
                    EditorGUILayout.LabelField(attr.Description,
                        EditorStyles.wordWrappedMiniLabel);
                }

                DrawMiddlewareHooks(type);
            }
        }

        private void MoveMiddleware(List<string> configured, int index, int delta)
        {
            int newIndex = index + delta;
            if (newIndex < 0 || newIndex >= _cachedMiddlewareTypes.Length) return;

            var types = _cachedMiddlewareTypes;
            var tmp = types[index];
            types[index] = types[newIndex];
            types[newIndex] = tmp;

            // Rebuild configured order to match the new arrangement.
            var reordered = new List<string>();
            foreach (var t in types)
            {
                string name = t.AssemblyQualifiedName;
                if (configured.Contains(name)) reordered.Add(name);
            }
            SaveMiddlewareList(reordered);
        }

        private void SaveMiddlewareList(List<string> configured)
        {
            _config.MiddlewareTypeNames = configured.ToArray();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
        }

        private void DrawMiddlewareHooks(Type type)
        {
            var interfaces = new List<string>();
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.Name.StartsWith("IItemDatabase")) interfaces.Add(iface.Name);
            }

            if (interfaces.Count == 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawBadge("NO HOOKS", new Color(0.5f, 0.5f, 0.5f));
                    EditorGUILayout.LabelField(
                        "Manual middleware — call its methods directly (e.g. ExpireNow()).",
                        EditorStyles.miniLabel);
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawBadge("HOOKS", new Color(0.35f, 0.5f, 0.7f));

                foreach (var iface in interfaces)
                {
                    string label = iface
                        .Replace("IItemDatabase", "")
                        .Replace("Middleware", "");
                    bool isBefore = label.StartsWith("Before");
                    DrawBadge(label, isBefore
                        ? new Color(0.4f, 0.55f, 0.75f)
                        : new Color(0.45f, 0.65f, 0.45f));
                }
            }

            EditorGUILayout.LabelField(
                "Implements: " + string.Join(", ", interfaces),
                EditorStyles.miniLabel);
        }

        #endregion

        #region Tab — Preview

        private void DrawPreviewTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Configuration Summary",
                    EditorStyles.boldLabel);
                EditorGUILayout.Space();

                int tagTypeCount = ScanTagTypes().Length;
                int middlewareTypeCount = ScanMiddlewareTypes().Length;

                DrawPreviewRow("Item Definitions",
                    $"{_config.ItemDefinitions?.Length ?? 0}");
                DrawPreviewRow("Tag Types (code)", $"{tagTypeCount}");
                DrawPreviewRow("Tag Assets", $"{_config.TagDefinitions?.Length ?? 0}");
                DrawPreviewRow("Middlewares",
                    $"{_config.MiddlewareTypeNames?.Length ?? 0} / {middlewareTypeCount}");
                DrawPreviewRow("Save Key", "item_db");
                DrawPreviewRow("Config Asset", "ItemDatabaseConfiguration.asset");

                EditorGUILayout.Space();
                DrawPreviewRow("Status",
                    _config != null
                        ? "Ready"
                        : "No Config",
                    _config != null
                        ? new Color(0.2f, 0.7f, 0.2f)
                        : new Color(0.8f, 0.3f, 0.2f));
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                DrawLiveDatabase();
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect the live in-memory database.",
                    MessageType.Info);
            }
        }

        /// <summary>
        /// Read-only view of the runtime database. Only enabled while in
        /// Play Mode, when the ItemDatabase director has booted.
        /// </summary>
        private void DrawLiveDatabase()
        {
            bool enabled = ItemDatabase.IsEnabled;
            bool directorReady = enabled
                && ItemDatabaseDirector.Instance != null
                && ItemDatabaseDirector.ReadyTask.IsCompleted;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Live Database (Play Mode)",
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    DrawBadge(enabled ? "ACTIVE" : "DISABLED",
                        enabled
                            ? new Color(0.2f, 0.7f, 0.2f)
                            : new Color(0.8f, 0.3f, 0.2f));

                    if (directorReady)
                    {
                        if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
                        {
                            // no-op: drawing re-reads the store every repaint
                        }
                    }
                }

                EditorGUILayout.Space();

                if (!enabled || !directorReady)
                {
                    EditorGUILayout.HelpBox(
                        enabled
                            ? "ItemDatabase director is still booting — try again shortly."
                            : "ItemDatabase is disabled (no configuration asset found).",
                        MessageType.Warning);
                    return;
                }

                int itemCount = ItemDatabase.Count;
                DrawPreviewRow("Items in memory", $"{itemCount}");

                if (itemCount == 0)
                {
                    EditorGUILayout.LabelField(
                        "(inventory is empty)", EditorStyles.miniLabel);
                    return;
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Inventory", EditorStyles.miniBoldLabel);

                _liveScrollPosition = EditorGUILayout.BeginScrollView(
                    _liveScrollPosition, GUILayout.MaxHeight(300));

                foreach (var item in ItemDatabase.AllItems)
                {
                    DrawLiveItemRow(item);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawLiveItemRow(InventoryItem item)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(item.itemId, EditorStyles.boldLabel,
                        GUILayout.Width(120));
                    EditorGUILayout.LabelField(ShortKey(item.key), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();

                    var stack = ItemDatabase.GetTag<StackableData>(item.key);
                    var exp = ItemDatabase.GetTag<ExpirableData>(item.key);

                    if (stack != null)
                        DrawBadge($"x{stack.count}", new Color(0.7f, 0.55f, 0.2f));

                    if (exp != null)
                    {
                        bool expired = exp.IsExpired;
                        DrawBadge(expired ? "EXPIRED" : "expires",
                            expired
                                ? new Color(0.8f, 0.3f, 0.2f)
                                : new Color(0.35f, 0.6f, 0.8f));
                    }
                }

                if (!string.IsNullOrEmpty(item.customName))
                {
                    EditorGUILayout.LabelField($"Name: {item.customName}",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.LabelField(
                    $"Created: {item.createdAt:yyyy-MM-dd HH:mm:ss} UTC",
                    EditorStyles.miniLabel);
            }
        }

        private string ShortKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            return key.Length <= 28 ? key : key.Substring(0, 28) + "…";
        }

        private void DrawPreviewRow(string label, string value)
        {
            DrawPreviewRow(label, value, Color.white);
        }

        private void DrawPreviewRow(string label, string value, Color valueColor)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.label, GUILayout.Width(180));
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = valueColor } };
                EditorGUILayout.LabelField(value, style);
            }
        }

        #endregion

        #region Shared Editor Helpers

        private void DrawBadge(string text, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold
            };
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label($" {text} ", style, GUILayout.Width(18 + text.Length * 6.5f));
            GUI.backgroundColor = prev;
        }

        private bool DrawSmallDeleteButton()
        {
            var content = EditorGUIUtility.IconContent("TreeEditor.Trash");
            return GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(26));
        }

        private void DrawInlineInspectorFor(ScriptableObject target)
        {
            if (target == null) return;

            int id = target.GetInstanceID();
            if (!_cachedEditors.TryGetValue(id, out var editor) || editor == null)
            {
                _cachedEditors[id] = UnityEditor.Editor.CreateEditor(target);
                editor = _cachedEditors[id];
            }

            if (editor == null) return;
            editor.OnInspectorGUI();
        }

        private bool IsFolded(Object target)
        {
            return SessionState.GetBool($"ItemDb_Fold_{target.GetInstanceID()}", false);
        }

        private void SetFolded(Object target, bool value)
        {
            SessionState.SetBool($"ItemDb_Fold_{target.GetInstanceID()}", value);
        }

        private bool MatchesSearch(ItemDefinition def, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            return (def.ItemId?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (def.Description?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private void DestroyInlineEditors()
        {
            foreach (var editor in _cachedEditors.Values)
            {
                if (editor != null) Object.DestroyImmediate(editor);
            }
            _cachedEditors.Clear();
        }

        #endregion

        #region Config Asset Management

        private ItemDatabaseConfiguration GetOrCreateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemDatabaseConfiguration>(ConfigAssetPath);
            if (existing != null)
            {
                ItemDatabaseConfiguration.Instance = existing;
                return existing;
            }

            var created = ItemDatabaseConfiguration.CreateOrMoveToDesignatedPath();
            if (created != null)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return created;
        }

        #endregion

        #region Reflection Scans

        internal static Type[] ScanTagTypes()
        {
            if (_cachedTagTypes != null) return _cachedTagTypes;

            var results = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (typeof(TagDefinition).IsAssignableFrom(type))
                        results.Add(type);
                }
            }

            _cachedTagTypes = results.OrderBy(t => t.Name).ToArray();
            return _cachedTagTypes;
        }

        private static Type[] ScanMiddlewareTypes()
        {
            if (_cachedMiddlewareTypes != null) return _cachedMiddlewareTypes;

            var results = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (type.GetCustomAttribute<ItemDatabaseMiddlewareAttribute>() == null) continue;
                    bool implementsAny = type.GetInterfaces()
                        .Any(i => i.Name.StartsWith("IItemDatabase"));
                    if (!implementsAny) continue;
                    results.Add(type);
                }
            }

            _cachedMiddlewareTypes = results.OrderBy(t => t.Name).ToArray();
            return _cachedMiddlewareTypes;
        }

        #endregion
    }
}
