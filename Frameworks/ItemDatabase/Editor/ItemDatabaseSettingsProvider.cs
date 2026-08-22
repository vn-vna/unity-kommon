using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.ItemDatabase;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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
            { "Definitions", "Tags", "Middlewares", "Validation", "Live Preview" };

        private static readonly string[] DefinitionSortOptions =
            { "ID (A-Z)", "ID (Z-A)", "Most Issues", "Tag Count" };

        #endregion

        #region Private Fields

        private ItemDatabaseConfiguration _config;
        private int _selectedTabIndex;
        private int _ownerFilterIndex;
        private Vector2 _scrollPosition;
        private Vector2 _liveScrollPosition;
        private string _searchText = string.Empty;
        private string _newDefinitionId = string.Empty;
        private string _tagSearchText = string.Empty;
        private string _middlewareSearchText = string.Empty;
        private string _liveSearchText = string.Empty;
        private int _definitionSortIndex;
        private bool _showOnlyValidationErrors;
        private IReadOnlyList<ItemDatabaseDiagnostic> _diagnostics
            = Array.Empty<ItemDatabaseDiagnostic>();
        private bool _diagnosticsDirty = true;
        private string _lastGuiError;
        private string[] _configurationConflictPaths = Array.Empty<string>();

        private readonly Dictionary<int, UnityEditor.Editor> _cachedEditors
            = new Dictionary<int, UnityEditor.Editor>();

        private static Type[] _cachedTagTypes;
        private static Type[] _cachedMiddlewareTypes;
        private static readonly Dictionary<Type, int> MiddlewarePriorityCache
            = new Dictionary<Type, int>();

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

                _config = LoadSettings();
                if (_config == null)
                {
                    DrawManagerCardHeader();
                    EditorGUILayout.Space();
                    DrawMissingConfiguration();
                    return;
                }

                RefreshDiagnosticsIfNeeded();

                DrawManagerCardHeader();
                DrawConfigurationConflicts();

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

                using (var scrollView = new EditorGUILayout.ScrollViewScope(
                           _scrollPosition))
                {
                    _scrollPosition = scrollView.scrollPosition;
                    switch (_selectedTabIndex)
                    {
                        case 0: DrawDefinitionsTab(); break;
                        case 1: DrawTagsTab(); break;
                        case 2: DrawMiddlewaresTab(); break;
                        case 3: DrawValidationTab(); break;
                        case 4: DrawPreviewTab(); break;
                    }
                }
                _lastGuiError = null;
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_lastGuiError != ex.ToString())
                {
                    _lastGuiError = ex.ToString();
                    QuickLog.Error<ItemDatabaseSettingsProvider>(
                        "Failed to draw Item Database settings: {0}",
                        ex
                    );
                }

                EditorGUILayout.HelpBox(
                    $"Item Database settings could not be rendered: {ex.Message}",
                    MessageType.Error
                );
            }
        }

        public override void OnActivate(
            string searchContext,
            VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            Undo.undoRedoPerformed += HandleProjectStateChanged;
            EditorApplication.projectChanged += HandleProjectStateChanged;
            HandleProjectStateChanged();
        }

        public override void OnDeactivate()
        {
            Undo.undoRedoPerformed -= HandleProjectStateChanged;
            EditorApplication.projectChanged -= HandleProjectStateChanged;
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

                    if (hasConfig
                        && GUILayout.Button("Validate", GUILayout.Width(68)))
                    {
                        _selectedTabIndex = 3;
                        EditorPrefs.SetInt(TabPrefKey, _selectedTabIndex);
                    }

                    using (new EditorGUI.DisabledScope(
                               !hasConfig || !IsConfigurationAssetDirty()))
                    {
                        if (GUILayout.Button("Save", GUILayout.Width(52)))
                        {
                            SaveConfigurationAsset();
                        }
                    }
                }

                if (_config != null)
                {
                    EditorGUILayout.LabelField(
                        $"Definitions: {_config.ItemDefinitions.Length}"
                        + $"  |  Tags: {_config.TagDefinitions.Length}"
                        + $"  |  Middlewares: {_config.MiddlewareTypeNames.Length}"
                        + $"  |  Validation: {CountBlockingErrors()} errors, "
                        + $"{CountDiagnostics(ItemDatabaseDiagnosticSeverity.Warning)} warnings",
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
                _definitionSortIndex = EditorGUILayout.Popup(
                    _definitionSortIndex,
                    DefinitionSortOptions,
                    GUILayout.Width(110)
                );

            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("New Item ID", GUILayout.Width(82));
                _newDefinitionId = EditorGUILayout.TextField(_newDefinitionId);
                if (GUILayout.Button("Generate", GUILayout.Width(72)))
                {
                    _newDefinitionId = GenerateUniqueItemId();
                }

                bool canCreate = CanEditConfiguration
                    && IsNewDefinitionIdValid(_newDefinitionId);
                using (new EditorGUI.DisabledScope(!canCreate))
                {
                    if (GUILayout.Button("Create", GUILayout.Width(72)))
                    {
                        CreateNewDefinition(_newDefinitionId);
                    }
                }
            }
            EditorGUILayout.Space();

            ItemDefinition[] definitions = SortDefinitions(
                _config.ItemDefinitions
                    .Where(definition => definition != null
                        && MatchesSearch(definition, _searchText))
                    .ToArray()
            );
            foreach (ItemDefinition def in definitions)
            {
                DrawDefinitionCard(def);
            }

            if (_config.ItemDefinitions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No item definitions yet. Click '+ New Definition' to create one.",
                    MessageType.Info);
            }
            else if (definitions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No definitions match the current search.",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"Showing {definitions.Length} / {_config.ItemDefinitions.Length}",
                    EditorStyles.miniLabel
                );
            }
        }

        private void DrawDefinitionCard(ItemDefinition def)
        {
            string owner = ItemDatabase.GetDefinitionOwner(def);
            bool isOwned = !string.IsNullOrEmpty(owner);
            bool isExternal = !ItemDefinitionTagUtils.IsOwnedSubAsset(
                _config,
                def
            );
            int issueCount = CountDiagnosticsFor(def);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool expanded = IsFolded(def);
                    bool foldout = EditorGUILayout.Foldout(expanded,
                        string.IsNullOrEmpty(def.ItemId) ? "(unnamed)" : def.ItemId, true);
                    if (foldout != expanded) SetFolded(def, foldout);

                    if (isOwned)
                    {
                        DrawBadge(
                            $"Managed by {owner}",
                            new Color(0.55f, 0.4f, 0.75f)
                        );
                    }

                    if (issueCount > 0)
                    {
                        DrawBadge(
                            $"{issueCount} issues",
                            new Color(0.8f, 0.45f, 0.2f)
                        );
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                        EditorGUIUtility.PingObject(def);

                    using (new EditorGUI.DisabledScope(
                               (isOwned && !isExternal)
                               || !CanEditConfiguration))
                    {
                        if (DrawSmallDeleteButton())
                        {
                            DeleteDefinition(def);
                            return;
                        }
                    }
                }

                if (IsFolded(def))
                {
                    if (isExternal)
                    {
                        EditorGUILayout.HelpBox(
                            "This definition belongs to another asset and is "
                            + "read-only here. Unlink it to repair the configuration.",
                            MessageType.Warning
                        );
                    }
                    else
                    {
                        DrawInlineInspectorFor(def);
                    }
                }
            }
        }

        private void MarkConfigDirty()
        {
            ItemDefinitionTagUtils.MarkDirty(_config);
            _diagnosticsDirty = true;
        }

        private void RefreshDiagnosticsIfNeeded()
        {
            if (!_diagnosticsDirty) return;

            _diagnostics = _config != null
                ? ItemDatabaseConfigurationValidator.Validate(_config)
                : Array.Empty<ItemDatabaseDiagnostic>();
            _diagnosticsDirty = false;
        }

        private bool IsConfigurationAssetDirty()
        {
            return GetConfigurationObjects().Any(EditorUtility.IsDirty);
        }

        private void SaveConfigurationAsset()
        {
            foreach (Object target in GetConfigurationObjects())
            {
                AssetDatabase.SaveAssetIfDirty(target);
            }

            _diagnosticsDirty = true;
        }

        private IEnumerable<Object> GetConfigurationObjects()
        {
            var targets = new HashSet<Object>();
            if (_config == null) return targets;

            targets.Add(_config);
            foreach (TagDefinition tag in _config.TagDefinitions)
            {
                if (ItemDefinitionTagUtils.IsOwnedSubAsset(_config, tag))
                {
                    targets.Add(tag);
                }
            }

            foreach (ItemDefinition definition in _config.ItemDefinitions)
            {
                if (!ItemDefinitionTagUtils.IsOwnedSubAsset(
                        _config,
                        definition
                    ))
                {
                    continue;
                }

                targets.Add(definition);
                if (ItemDefinitionTagUtils.IsOwnedSubAsset(
                        _config,
                        definition.Metadata
                    ))
                {
                    targets.Add(definition.Metadata);
                }

                foreach (TagDefinition tag in definition.Tags)
                {
                    if (ItemDefinitionTagUtils.IsOwnedSubAsset(_config, tag))
                    {
                        targets.Add(tag);
                    }
                }
            }

            return targets;
        }

        private void CreateNewDefinition(string requestedItemId)
        {
            if (!CanEditConfiguration) return;

            string itemId = requestedItemId?.Trim();
            if (!IsNewDefinitionIdValid(itemId)) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Item Definition");

            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            var metadata = ScriptableObject.CreateInstance<ItemMetadata>();
            AssetDatabase.AddObjectToAsset(metadata, _config);
            AssetDatabase.AddObjectToAsset(def, _config);

            def.name = $"ItemDefinition_{itemId}";
            def.ItemId = itemId;
            metadata.name = $"{def.ItemId}_meta";
            def.Metadata = metadata;
            Undo.RegisterCreatedObjectUndo(metadata, "Create Item Metadata");
            Undo.RegisterCreatedObjectUndo(def, "Create Item Definition");
            Undo.RegisterCompleteObjectUndo(_config, "Create Item Definition");

            var list = new List<ItemDefinition>(_config.ItemDefinitions ?? Array.Empty<ItemDefinition>())
            { def };
            _config.ItemDefinitions = list.ToArray();

            MarkConfigDirty();
            ItemDefinitionTagUtils.MarkDirty(def, metadata);
            Undo.CollapseUndoOperations(undoGroup);
            SetFolded(def, true);
            Selection.activeObject = def;
            _newDefinitionId = string.Empty;
        }

        private void DeleteDefinition(ItemDefinition def)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Unlink Definition",
                $"Remove item definition '{def.ItemId}' from the active "
                + "configuration?\n\nIts sub-assets are retained to protect "
                + "incoming references and may appear as orphan warnings.",
                "Unlink", "Cancel");
            if (!confirm) return;

            ItemDefinitionTagUtils.DeleteDefinition(_config, def);
            DestroyInlineEditors();
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

                string[] ownerFilterOptions = BuildOwnerFilterOptions();
                _ownerFilterIndex = Mathf.Clamp(
                    _ownerFilterIndex,
                    0,
                    ownerFilterOptions.Length - 1
                );
                _ownerFilterIndex = EditorGUILayout.Popup(_ownerFilterIndex,
                    ownerFilterOptions, GUILayout.Width(190));
            }
            EditorGUILayout.Space();

            Type[] tagTypes = ScanTagTypes();

            foreach (var type in tagTypes)
            {
                if (type == null) continue;
                Type dataType = TagDataRegistry.GetDataType(type);
                if (!string.IsNullOrEmpty(_tagSearchText)
                    && type.FullName.IndexOf(
                        _tagSearchText,
                        StringComparison.OrdinalIgnoreCase) < 0
                    && (dataType == null
                        || dataType.FullName.IndexOf(
                            _tagSearchText,
                            StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (!MatchesOwnerFilter(type, BuildOwnerFilterOptions())) continue;

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
                DrawTagDefaultsEditor(
                    type,
                    defaults,
                    hasData,
                    !string.IsNullOrEmpty(owner),
                    defaults != null
                        && !ItemDefinitionTagUtils.IsOwnedSubAsset(
                            _config,
                            defaults
                        )
                );
            }
        }

        private void DrawTagDefaultsEditor(
            Type type,
            TagDefinition defaults,
            bool hasData,
            bool isOwned,
            bool isExternal)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Default Values",
                    EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.FlexibleSpace();

                if (defaults != null)
                {
                    using (new EditorGUI.DisabledScope(
                               !CanEditConfiguration
                               || (isOwned && !isExternal)))
                    {
                        if (GUILayout.Button(
                                new GUIContent(
                                    "Clear",
                                    "Remove this default safely. Existing references must be cloned first."
                                ),
                                EditorStyles.miniButton,
                                GUILayout.Width(50)))
                        {
                            ClearDefaultTemplate(defaults);
                            return;
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(
                               !CanEditConfiguration || isOwned))
                    {
                        if (GUILayout.Button(
                                "Create Defaults",
                                EditorStyles.miniButton,
                                GUILayout.Width(110)))
                        {
                            CreateDefaultTemplate(type);
                        }
                    }
                }
            }

            if (defaults != null)
            {
                if (isExternal)
                {
                    EditorGUILayout.HelpBox(
                        "This default belongs to another asset. It is read-only; "
                        + "clear it to unlink the reference.",
                        MessageType.Warning
                    );
                }

                if (HasEditableSerializedFields(defaults))
                {
                    using (new EditorGUI.DisabledScope(isOwned || isExternal))
                    {
                        DrawInlineInspectorFor(defaults);
                    }
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
            ItemDefinitionTagUtils.CreateDefaultTemplate(_config, type);
            MarkConfigDirty();
        }

        private void ClearDefaultTemplate(TagDefinition tag)
        {
            ItemDefinition[] references = ItemDefinitionTagUtils
                .GetDefinitionReferences(_config, tag);
            string owner = ItemDatabase.GetTagOwner(tag.GetType());
            if (references.Length > 0 && !string.IsNullOrEmpty(owner))
            {
                EditorUtility.DisplayDialog(
                    "Managed Default Is In Use",
                    $"'{tag.GetType().Name}' is managed by '{owner}' and is "
                    + $"referenced by {references.Length} definitions. The "
                    + "owning module must repair these references.",
                    "OK"
                );
                Selection.activeObject = references[0];
                EditorGUIUtility.PingObject(references[0]);
                return;
            }

            bool materializeReferences = false;
            if (references.Length > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Default Is In Use",
                    $"'{tag.GetType().Name}' is referenced by {references.Length} "
                    + "definitions. Clearing it directly would create missing references.",
                    $"Clone {references.Length} References & Clear",
                    "Cancel",
                    "Ping First Reference"
                );
                if (choice == 1) return;
                if (choice == 2)
                {
                    Selection.activeObject = references[0];
                    EditorGUIUtility.PingObject(references[0]);
                    return;
                }

                materializeReferences = true;
            }
            else if (!EditorUtility.DisplayDialog(
                         "Clear Tag Defaults",
                         $"Remove the '{tag.GetType().Name}' default template?",
                         "Clear",
                         "Cancel"))
            {
                return;
            }

            ItemDefinitionTagUtils.ClearDefaultTemplate(
                _config,
                tag,
                materializeReferences
            );
            MarkConfigDirty();
        }

        private bool MatchesOwnerFilter(Type type, string[] options)
        {
            if (_ownerFilterIndex <= 0) return true;

            string owner = ItemDatabase.GetTagOwner(type);
            if (_ownerFilterIndex == 1) return string.IsNullOrEmpty(owner);

            string selected = options[_ownerFilterIndex];
            const string prefix = "Managed by ";
            return selected.StartsWith(prefix, StringComparison.Ordinal)
                && owner == selected.Substring(prefix.Length);
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

            string[] unresolved = configured
                .Where(typeName => Type.GetType(typeName) == null)
                .ToArray();
            if (unresolved.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{unresolved.Length} configured middleware type(s) cannot be resolved. "
                    + "Remove stale entries before Play Mode or build.",
                    MessageType.Error
                );
                foreach (string typeName in unresolved)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.SelectableLabel(
                            typeName,
                            EditorStyles.miniLabel,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)
                        );
                        using (new EditorGUI.DisabledScope(!CanEditConfiguration))
                        {
                            if (GUILayout.Button("Remove", GUILayout.Width(64)))
                            {
                                configured.Remove(typeName);
                                SaveMiddlewareList(configured);
                                return;
                            }
                        }
                    }
                }

                EditorGUILayout.Space();
            }

            middlewareTypes = middlewareTypes
                .OrderByDescending(type => configured.Contains(type.AssemblyQualifiedName))
                .ThenBy(GetMiddlewarePriority)
                .ThenBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            foreach (Type type in middlewareTypes)
            {
                if (type == null) continue;
                ItemDatabaseMiddlewareAttribute attribute = type
                    .GetCustomAttribute<ItemDatabaseMiddlewareAttribute>();
                if (!string.IsNullOrEmpty(_middlewareSearchText)
                    && type.FullName.IndexOf(
                        _middlewareSearchText,
                        StringComparison.OrdinalIgnoreCase) < 0
                    && (attribute?.Description?.IndexOf(
                            _middlewareSearchText,
                            StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                    continue;

                DrawMiddlewareCard(type, configured);
            }
        }

        private void DrawMiddlewareCard(Type type, List<string> configured)
        {
            string typeName = type.AssemblyQualifiedName;
            bool isEnabled = configured.Contains(typeName);
            var attr = type.GetCustomAttribute<ItemDatabaseMiddlewareAttribute>();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(type.Name, EditorStyles.boldLabel);

                    DrawBadge($"Priority: {GetMiddlewarePriority(type)}",
                        new Color(0.7f, 0.55f, 0.2f));

                    if (isEnabled) DrawBadge("ENABLED", new Color(0.2f, 0.7f, 0.2f));

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(!CanEditConfiguration))
                    {
                        if (isEnabled)
                        {
                            if (GUILayout.Button("Disable", GUILayout.Width(70)))
                            {
                                configured.Remove(typeName);
                                SaveMiddlewareList(configured);
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
                }

                if (!string.IsNullOrEmpty(attr?.Description))
                {
                    EditorGUILayout.LabelField(attr.Description,
                        EditorStyles.wordWrappedMiniLabel);
                }

                DrawMiddlewareHooks(type);
            }
        }

        private void SaveMiddlewareList(List<string> configured)
        {
            Undo.RegisterCompleteObjectUndo(_config, "Configure Item Database Middleware");
            _config.MiddlewareTypeNames = configured.ToArray();
            MarkConfigDirty();
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

        #region Tab — Validation

        private void DrawValidationTab()
        {
            int errorCount = CountBlockingErrors();
            int warningCount = CountDiagnostics(ItemDatabaseDiagnosticSeverity.Warning);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Configuration Validation",
                        EditorStyles.boldLabel
                    );
                    GUILayout.FlexibleSpace();
                    DrawBadge(
                        $"{errorCount} errors",
                        errorCount > 0
                            ? new Color(0.8f, 0.3f, 0.2f)
                            : new Color(0.2f, 0.7f, 0.2f)
                    );
                    DrawBadge(
                        $"{warningCount} warnings",
                        warningCount > 0
                            ? new Color(0.8f, 0.55f, 0.2f)
                            : new Color(0.2f, 0.7f, 0.2f)
                    );
                }

                EditorGUILayout.LabelField(
                    errorCount > 0
                        ? "Build is blocked by validation errors; runtime also "
                            + "rejects invalid schema invariants."
                        : "No blocking configuration errors were found.",
                    EditorStyles.wordWrappedMiniLabel
                );
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _showOnlyValidationErrors = GUILayout.Toggle(
                    _showOnlyValidationErrors,
                    "Errors only",
                    EditorStyles.miniButton,
                    GUILayout.Width(90)
                );

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!CanEditConfiguration))
                {
                    if (GUILayout.Button("Clone Shared Defaults", GUILayout.Width(150)))
                    {
                        MaterializeSharedDefaultReferences();
                    }

                    if (GUILayout.Button("Clone Shared Metadata", GUILayout.Width(155)))
                    {
                        MaterializeSharedMetadata();
                    }

                    if (GUILayout.Button("Normalize Names", GUILayout.Width(120)))
                    {
                        NormalizeSubAssetNames();
                    }
                }
            }

            EditorGUILayout.Space();
            IEnumerable<ItemDatabaseDiagnostic> visibleDiagnostics = _diagnostics;
            if (_showOnlyValidationErrors)
            {
                visibleDiagnostics = visibleDiagnostics.Where(diagnostic =>
                    diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Error);
            }

            ItemDatabaseDiagnostic[] diagnostics = visibleDiagnostics.ToArray();
            if (diagnostics.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No diagnostics match the current filter.",
                    MessageType.Info
                );
                return;
            }

            foreach (ItemDatabaseDiagnostic diagnostic in diagnostics)
            {
                DrawDiagnostic(diagnostic);
            }
        }

        private void DrawDiagnostic(ItemDatabaseDiagnostic diagnostic)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color color;
                    switch (diagnostic.Severity)
                    {
                        case ItemDatabaseDiagnosticSeverity.Error:
                            color = new Color(0.8f, 0.3f, 0.2f);
                            break;
                        case ItemDatabaseDiagnosticSeverity.Warning:
                            color = new Color(0.8f, 0.55f, 0.2f);
                            break;
                        default:
                            color = new Color(0.35f, 0.6f, 0.8f);
                            break;
                    }

                    DrawBadge(diagnostic.Severity.ToString().ToUpperInvariant(), color);
                    EditorGUILayout.LabelField(
                        diagnostic.Code,
                        EditorStyles.miniBoldLabel
                    );
                    GUILayout.FlexibleSpace();
                    if (diagnostic.Context != null
                        && GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                    {
                        Selection.activeObject = diagnostic.Context;
                        EditorGUIUtility.PingObject(diagnostic.Context);
                    }
                }

                EditorGUILayout.LabelField(
                    diagnostic.Message,
                    EditorStyles.wordWrappedLabel
                );
            }
        }

        private void MaterializeSharedDefaultReferences()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Materialize Item Database Defaults");
            int metadataCopies;
            int materialized = 0;
            try
            {
                metadataCopies = CloneSharedMetadataReferences();
                foreach (TagDefinition template in _config.TagDefinitions.ToArray())
                {
                    if (template == null
                        || !string.IsNullOrEmpty(
                            ItemDatabase.GetTagOwner(template.GetType())))
                    {
                        continue;
                    }

                    foreach (ItemDefinition definition in ItemDefinitionTagUtils
                                 .GetDefinitionReferences(_config, template))
                    {
                        if (!ItemDefinitionTagUtils.IsOwnedSubAsset(
                                _config,
                                definition
                            )
                            || !string.IsNullOrEmpty(
                                ItemDatabase.GetDefinitionOwner(definition)))
                        {
                            continue;
                        }

                        ItemDefinitionTagUtils.CloneTagIntoDefinition(
                            _config,
                            definition,
                            template
                        );
                        materialized++;
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            MarkConfigDirty();
            EditorUtility.DisplayDialog(
                "Shared Defaults Materialized",
                $"Created {materialized} per-definition tag copies. "
                + $"Also isolated {metadataCopies} shared metadata references. "
                + "Review validation, then save the configuration.",
                "OK"
            );
        }

        private void MaterializeSharedMetadata()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Materialize Item Database Metadata");
            int materialized;
            try
            {
                materialized = CloneSharedMetadataReferences();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            MarkConfigDirty();
            EditorUtility.DisplayDialog(
                "Shared Metadata Materialized",
                $"Created {materialized} isolated metadata copies. "
                + "Review validation, then save the configuration.",
                "OK"
            );
        }

        private int CloneSharedMetadataReferences()
        {
            int materialized = 0;
            foreach (ItemDefinition definition in _config.ItemDefinitions)
            {
                if (definition == null || definition.Metadata == null) continue;
                if (!ItemDefinitionTagUtils.IsOwnedSubAsset(
                        _config,
                        definition
                    )
                    || !string.IsNullOrEmpty(
                        ItemDatabase.GetDefinitionOwner(definition)))
                {
                    continue;
                }

                ItemMetadata source = definition.Metadata;
                ItemMetadata result = ItemDefinitionTagUtils
                    .CloneSharedMetadataForDefinition(_config, definition);
                if (result != source) materialized++;
            }

            return materialized;
        }

        private void NormalizeSubAssetNames()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Normalize Item Database Names");

            foreach (ItemDefinition definition in _config.ItemDefinitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.ItemId))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(
                        ItemDatabase.GetDefinitionOwner(definition)))
                {
                    continue;
                }

                if (!ItemDefinitionTagUtils.IsOwnedSubAsset(
                        _config,
                        definition
                    ))
                {
                    continue;
                }

                Undo.RegisterCompleteObjectUndo(definition, "Normalize Item Database Names");
                definition.name = $"ItemDefinition_{definition.ItemId}";
                ItemDefinitionTagUtils.MarkDirty(definition);

                if (definition.Metadata == null
                    || !ItemDefinitionTagUtils.IsOwnedSubAsset(
                        _config,
                        definition.Metadata
                    ))
                {
                    continue;
                }
                Undo.RegisterCompleteObjectUndo(
                    definition.Metadata,
                    "Normalize Item Database Names"
                );
                definition.Metadata.name = $"{definition.ItemId}_meta";
                ItemDefinitionTagUtils.MarkDirty(definition.Metadata);

                foreach (TagDefinition tag in definition.Tags)
                {
                    if (tag == null
                        || ItemDefinitionTagUtils.IsSharedTemplate(_config, tag)
                        || !ItemDefinitionTagUtils.IsOwnedSubAsset(_config, tag))
                    {
                        continue;
                    }

                    Undo.RegisterCompleteObjectUndo(
                        tag,
                        "Normalize Item Database Names"
                    );
                    tag.name = $"{tag.GetType().Name}_{definition.ItemId}";
                    ItemDefinitionTagUtils.MarkDirty(tag);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            MarkConfigDirty();
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
                DrawPreviewRow(
                    "Validation",
                    $"{CountBlockingErrors()} errors, "
                    + $"{CountDiagnostics(ItemDatabaseDiagnosticSeverity.Warning)} warnings"
                );

                EditorGUILayout.Space();
                DrawPreviewRow("Status",
                    Application.isPlaying
                        ? ItemDatabase.State.ToString()
                        : "Configured (enter Play Mode for runtime status)",
                    CountBlockingErrors() == 0
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
            ItemDatabaseLifecycleState state = ItemDatabase.State;
            bool directorReady = enabled
                && state == ItemDatabaseLifecycleState.Ready
                && ItemDatabaseDirector.Instance != null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Live Database (Play Mode)",
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    DrawBadge(enabled ? state.ToString().ToUpperInvariant() : "DISABLED",
                        directorReady
                            ? new Color(0.2f, 0.7f, 0.2f)
                            : new Color(0.8f, 0.3f, 0.2f));
                }

                EditorGUILayout.Space();

                if (!enabled || !directorReady)
                {
                    EditorGUILayout.HelpBox(
                        enabled
                            ? state == ItemDatabaseLifecycleState.Failed
                                ? $"Initialization failed: {ItemDatabase.InitializationException?.Message}"
                                : $"Item Database runtime state is {state}."
                            : "ItemDatabase is disabled (no configuration asset found).",
                        state == ItemDatabaseLifecycleState.Failed
                            ? MessageType.Error
                            : MessageType.Warning);
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
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Inventory",
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(70)
                    );
                    _liveSearchText = EditorGUILayout.TextField(
                        _liveSearchText,
                        EditorStyles.toolbarSearchField
                    );
                }

                _liveScrollPosition = EditorGUILayout.BeginScrollView(
                    _liveScrollPosition, GUILayout.MaxHeight(300));

                InventoryItem[] visibleItems = ItemDatabase.AllItems
                    .Where(item => MatchesLiveSearch(item, _liveSearchText))
                    .OrderBy(item => item.itemId, StringComparer.Ordinal)
                    .ThenBy(item => item.key, StringComparer.Ordinal)
                    .ToArray();
                foreach (InventoryItem item in visibleItems)
                {
                    DrawLiveItemRow(item);
                }

                if (visibleItems.Length == 0)
                {
                    EditorGUILayout.LabelField(
                        "No live items match the search.",
                        EditorStyles.miniLabel
                    );
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

                    if (GUILayout.Button(
                            new GUIContent("Copy Key", "Copy the complete item key."),
                            EditorStyles.miniButton,
                            GUILayout.Width(62)))
                    {
                        EditorGUIUtility.systemCopyBuffer = item.key;
                    }

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
            content.tooltip = "Unlink this definition; retained sub-assets protect incoming references.";
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
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(!CanEditConfiguration))
            {
                editor.OnInspectorGUI();
            }

            if (EditorGUI.EndChangeCheck())
            {
                ItemDefinitionTagUtils.MarkDirty(_config, target);
                _diagnosticsDirty = true;
            }
        }

        private bool IsFolded(Object target)
        {
            return SessionState.GetBool(GetFoldoutKey(target), false);
        }

        private void SetFolded(Object target, bool value)
        {
            SessionState.SetBool(GetFoldoutKey(target), value);
        }

        private bool MatchesSearch(ItemDefinition def, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            if ((def.ItemId?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (def.Description?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (def.name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
            {
                return true;
            }

            string owner = ItemDatabase.GetDefinitionOwner(def);
            if ((owner?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
            {
                return true;
            }

            foreach (TagDefinition tag in def.Tags)
            {
                if (tag != null
                    && tag.GetType().Name.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return _diagnostics.Any(diagnostic =>
                IsDiagnosticForDefinition(diagnostic, def)
                && (diagnostic.Code.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || diagnostic.Message.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0));
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

        private ItemDatabaseConfiguration LoadSettings()
        {
            _configurationConflictPaths = ItemDatabaseBuildValidator
                .FindAlternativeConfigurationPaths();
            var existing = AssetDatabase.LoadAssetAtPath<ItemDatabaseConfiguration>(ConfigAssetPath);
            if (existing != null)
            {
                ItemDatabaseConfiguration.SetCanonical(existing);
                return existing;
            }

            ItemDatabaseConfiguration.SetCanonical(null);
            return null;
        }

        private void DrawMissingConfiguration()
        {
            EditorGUILayout.HelpBox(
                "Item Database is disabled. Opening this page does not create, move, or enable assets.",
                MessageType.Info
            );

            string[] candidatePaths = _configurationConflictPaths;

            using (new EditorGUI.DisabledScope(
                       !CanEditConfiguration || candidatePaths.Length > 0))
            {
                if (GUILayout.Button(
                        "Create Item Database Configuration",
                        GUILayout.Height(28)))
                {
                    CreateConfiguration();
                    GUIUtility.ExitGUI();
                }
            }

            if (candidatePaths.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Resolve the existing configuration before creating another. "
                + "Package-owned candidates cannot be adopted and must be "
                + "removed or relocated in their package.",
                MessageType.Error
            );
            EditorGUILayout.LabelField(
                "Existing Candidates",
                EditorStyles.boldLabel
            );
            foreach (string path in candidatePaths)
            {
                ItemDatabaseConfiguration candidate = AssetDatabase
                    .LoadAssetAtPath<ItemDatabaseConfiguration>(path);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.SelectableLabel(
                        path,
                        EditorStyles.miniLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight)
                    );
                    if (GUILayout.Button("Ping", GUILayout.Width(48)))
                    {
                        EditorGUIUtility.PingObject(candidate);
                    }

                    using (new EditorGUI.DisabledScope(!CanEditConfiguration))
                    {
                        if (GUILayout.Button("Adopt", GUILayout.Width(54)))
                        {
                            AdoptConfiguration(path);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawConfigurationConflicts()
        {
            string[] alternativePaths = _configurationConflictPaths;
            if (alternativePaths.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Multiple Item Database configurations were found. Builds are "
                + "blocked until every non-canonical configuration is removed "
                + "or relocated.",
                MessageType.Error
            );
            foreach (string path in alternativePaths)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.SelectableLabel(
                        path,
                        EditorStyles.miniLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight)
                    );
                    if (GUILayout.Button("Ping", GUILayout.Width(48)))
                    {
                        EditorGUIUtility.PingObject(
                            AssetDatabase.LoadMainAssetAtPath(path)
                        );
                    }
                }
            }
        }

        #endregion

        #region Reflection Scans

        internal static Type[] ScanTagTypes()
        {
            if (_cachedTagTypes != null) return _cachedTagTypes;

            _cachedTagTypes = TypeCache.GetTypesDerivedFrom<TagDefinition>()
                .Where(type => !type.IsAbstract
                    && !type.IsInterface
                    && ItemDatabaseConfigurationValidator.IsAvailableInPlayer(type))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return _cachedTagTypes;
        }

        private static Type[] ScanMiddlewareTypes()
        {
            if (_cachedMiddlewareTypes != null) return _cachedMiddlewareTypes;

            _cachedMiddlewareTypes = TypeCache
                .GetTypesWithAttribute<ItemDatabaseMiddlewareAttribute>()
                .Where(type => !type.IsAbstract
                    && !type.IsInterface
                    && ItemDatabaseConfigurationValidator.IsAvailableInPlayer(type)
                    && InventoryMiddlewarePipeline.ImplementsSupportedHook(type))
                .OrderBy(GetMiddlewarePriority)
                .ThenBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return _cachedMiddlewareTypes;
        }

        private bool CanEditConfiguration
        {
            get
            {
                if (Application.isPlaying
                    || EditorApplication.isCompiling
                    || EditorApplication.isUpdating)
                {
                    return false;
                }

                if (_config == null) return true;
                string path = AssetDatabase.GetAssetPath(_config);
                return !string.IsNullOrEmpty(path)
                    && AssetDatabase.IsOpenForEdit(
                        path,
                        StatusQueryOptions.UseCachedIfPossible
                    );
            }
        }

        private void HandleProjectStateChanged()
        {
            DestroyInlineEditors();
            _config = LoadSettings();
            _diagnosticsDirty = true;
            _cachedTagTypes = null;
            _cachedMiddlewareTypes = null;
            MiddlewarePriorityCache.Clear();
        }

        private int CountDiagnostics(ItemDatabaseDiagnosticSeverity severity)
        {
            return _diagnostics.Count(diagnostic => diagnostic.Severity == severity);
        }

        private int CountBlockingErrors()
        {
            return CountDiagnostics(ItemDatabaseDiagnosticSeverity.Error)
                + _configurationConflictPaths.Length;
        }

        private int CountDiagnosticsFor(ItemDefinition definition)
        {
            return _diagnostics.Count(diagnostic =>
                diagnostic.Severity != ItemDatabaseDiagnosticSeverity.Info
                && IsDiagnosticForDefinition(diagnostic, definition));
        }

        private static bool IsDiagnosticForDefinition(
            ItemDatabaseDiagnostic diagnostic,
            ItemDefinition definition)
        {
            if (diagnostic == null || definition == null) return false;
            if (diagnostic.Context == definition
                || diagnostic.Context == definition.Metadata)
            {
                return true;
            }

            return diagnostic.Context is TagDefinition tag
                && definition.Tags.Contains(tag);
        }

        private ItemDefinition[] SortDefinitions(ItemDefinition[] definitions)
        {
            IEnumerable<ItemDefinition> sorted = definitions;
            switch (_definitionSortIndex)
            {
                case 1:
                    sorted = sorted.OrderByDescending(
                        definition => definition.ItemId,
                        StringComparer.OrdinalIgnoreCase
                    );
                    break;
                case 2:
                    sorted = sorted
                        .OrderByDescending(CountDiagnosticsFor)
                        .ThenBy(
                            definition => definition.ItemId,
                            StringComparer.OrdinalIgnoreCase
                        );
                    break;
                case 3:
                    sorted = sorted
                        .OrderByDescending(definition => definition.Tags.Length)
                        .ThenBy(
                            definition => definition.ItemId,
                            StringComparer.OrdinalIgnoreCase
                        );
                    break;
                default:
                    sorted = sorted.OrderBy(
                        definition => definition.ItemId,
                        StringComparer.OrdinalIgnoreCase
                    );
                    break;
            }

            return sorted.ToArray();
        }

        private string GenerateUniqueItemId()
        {
            var existingIds = new HashSet<string>(
                _config.ItemDefinitions
                    .Where(definition => definition != null)
                    .Select(definition => definition.ItemId),
                StringComparer.Ordinal
            );
            string candidate;
            do
            {
                candidate = $"item_{Guid.NewGuid():N}";
            }
            while (existingIds.Contains(candidate));

            return candidate;
        }

        private bool IsNewDefinitionIdValid(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;

            string normalized = itemId.Trim();
            return !_config.ItemDefinitions.Any(definition =>
                definition != null
                && string.Equals(
                    definition.ItemId,
                    normalized,
                    StringComparison.Ordinal
                ));
        }

        private static string[] BuildOwnerFilterOptions()
        {
            string[] owners = ScanTagTypes()
                .Select(ItemDatabase.GetTagOwner)
                .Where(owner => !string.IsNullOrEmpty(owner))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(owner => owner, StringComparer.Ordinal)
                .Select(owner => $"Managed by {owner}")
                .ToArray();
            return new[] { "All", "Unowned" }.Concat(owners).ToArray();
        }

        private static int GetMiddlewarePriority(Type type)
        {
            if (type == null) return int.MaxValue;
            if (MiddlewarePriorityCache.TryGetValue(type, out int cachedPriority))
            {
                return cachedPriority;
            }

            int priority = type
                .GetCustomAttribute<ItemDatabaseMiddlewareAttribute>()
                ?.DefaultPriority ?? 500;

            MiddlewarePriorityCache[type] = priority;
            return priority;
        }

        private static bool HasEditableSerializedFields(Object target)
        {
            if (target == null) return false;
            var serializedTarget = new SerializedObject(target);
            SerializedProperty iterator = serializedTarget.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath != "m_Script") return true;
            }

            return false;
        }

        private static bool MatchesLiveSearch(InventoryItem item, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return (item.itemId?.IndexOf(
                       search,
                       StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (item.key?.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (item.customName?.IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private static string GetFoldoutKey(Object target)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            return $"ItemDb_Fold_{id}";
        }

        private void CreateConfiguration()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath) != null) return;

            EnsureAssetFolder(ConfigFolder);
            var config = ScriptableObject.CreateInstance<ItemDatabaseConfiguration>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            Undo.RegisterCreatedObjectUndo(
                config,
                "Create Item Database Configuration"
            );
            ItemDatabaseConfiguration.SetCanonical(config);
            AssetDatabase.SaveAssetIfDirty(config);
            _config = config;
        }

        private void AdoptConfiguration(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)
                || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog(
                    "Adoption Not Allowed",
                    "Only project-owned configurations under Assets can be adopted.",
                    "OK"
                );
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Adopt Item Database Configuration",
                    $"Move '{sourcePath}' to the canonical Resources path?\n\n"
                    + ConfigAssetPath
                    + "\n\nAsset moves cannot be undone through the Unity Undo stack.",
                    "Move",
                    "Cancel"))
            {
                return;
            }

            EnsureAssetFolder(ConfigFolder);
            string error = AssetDatabase.MoveAsset(sourcePath, ConfigAssetPath);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("Move Failed", error, "OK");
                return;
            }

            _config = LoadSettings();
            if (_config != null) AssetDatabase.SaveAssetIfDirty(_config);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        #endregion
    }
}
