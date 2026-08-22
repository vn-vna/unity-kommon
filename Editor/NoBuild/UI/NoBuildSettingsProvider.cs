// ═══════════════════════════════════════════════════════════
// ── NoBuildSettingsProvider ───────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Editor;
using Com.Hapiga.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    public sealed class NoBuildSettingsProvider : SettingsProvider
    {
        public const string Path = "Project/Tools/No Build";
        private const float LabelW = EditorGuiLayout.DefaultLabelWidth;
        private static readonly string[] Tabs =
            { "Scene Sets", "Defines", "Build Profiles", "Devices", "Flags" };
        private static readonly string[] StateFilters =
            { "All", "Active", "Inactive" };
        private static readonly string[] PlatformFilters =
            { "All", "Android", "Other Platforms" };
        private static readonly string[] FlagFilters =
            { "All", "Template", "Custom" };

        private SerializedObject _so;
        private SerializedProperty _sceneSets, _defineSets, _buildProfiles, _flagDefs;
        private SerializedProperty _activeScene, _activeDefine;
        private int _tab;
        private int _selSet = -1, _selDef = -1, _selBuild = -1, _selFlag = -1;
        private Vector2 _pvScroll;
        private string _searchQuery = string.Empty;
        private int _filterIndex;
        private int _expandedEntryIndex = -1;

        // ── Device Tab State ───────────────────────
        private List<AdbDeviceInfo> _cachedDevices;
        private double _lastDeviceRefreshTime;
        private const double DeviceRefreshInterval = 2.0;
        private string _installApkPath = "";
        private string _installAabPath = "";
        private List<WirelessDeviceInfo> _wirelessDevices;
        private string _pairingCode = "";

        private NoBuildSettingsProvider(string p, SettingsScope sc,
            IEnumerable<string> kw = null) : base(p, sc, kw)
        {
            activateHandler = (_, _2) => Refresh();
            deactivateHandler = () => { _so?.Dispose(); _so = null; };
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new NoBuildSettingsProvider(Path,
            SettingsScope.Project,
            new[] { "nobuild", "scene", "switch", "define", "build", "profile", "combo" });

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);
            if (_so?.targetObject == null) { Refresh(); if (_so?.targetObject == null) return; }
            _so.Update();
            // Auto-select first entry when tab has no selection
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_tab == 0 && (_selSet < 0 || _selSet >= s.sceneSets.Count) && s.sceneSets.Count > 0)
                _selSet = 0;
            if (_tab == 1 && (_selDef < 0 || _selDef >= s.scriptDefinitionSets.Count) && s.scriptDefinitionSets.Count > 0)
                _selDef = 0;
            if (_tab == 2 && (_selBuild < 0 || _selBuild >= s.buildProfiles.Count) && s.buildProfiles.Count > 0)
                _selBuild = 0;
            if (_tab == 4 && (_selFlag < 0 || _selFlag >= s.flagDefinitions.Count) && s.flagDefinitions.Count > 0)
                _selFlag = 0;

            DrawProviderHeader(s);
            int selectedTab = EditorGuiLayout.DrawTabBar(_tab, Tabs);
            if (selectedTab != _tab)
            {
                _tab = selectedTab;
                _filterIndex = 0;
                _expandedEntryIndex = -1;
                _searchQuery = string.Empty;
            }

            GUILayout.Space(4);

            if (_tab == 3)
            {
                DrawDevicePanel();
            }
            else
            {
                DrawCollectionBrowser();
            }

            _so.ApplyModifiedProperties();
        }

        private static void DrawProviderHeader(NoBuildSettings settings)
        {
            string sceneSet = settings.ActiveSceneSet?.setName ?? "No active scene set";
            string defineSet = settings.ActiveScriptDefinitionSet?.setName ?? "No active define set";
            EditorGuiLayout.DrawHeaderBox(
                "No Build",
                $"Scenes: {sceneSet}  •  Defines: {defineSet}"
            );
        }

        private void DrawCollectionBrowser()
        {
            DrawCollectionToolbar();
            _pvScroll = EditorGUILayout.BeginScrollView(_pvScroll);
            DrawCollectionList();
            EditorGUILayout.EndScrollView();
        }

        private void DrawCollectionToolbar()
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(LabelW));
            _searchQuery = EditorGUILayout.TextField(
                _searchQuery,
                EditorStyles.toolbarSearchField,
                GUILayout.ExpandWidth(true)
            );
            if (!string.IsNullOrEmpty(_searchQuery)
                && GUILayout.Button("Clear", GUILayout.Width(48)))
            {
                _searchQuery = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Filter",
                GUILayout.Width(LabelW)
            );
            _filterIndex = GUILayout.Toolbar(
                _filterIndex,
                GetFilterLabels(),
                GUILayout.ExpandWidth(true)
            );
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(GetAddButtonLabel(), GUILayout.Height(22)))
            {
                AddNew();
            }

            EditorGUI.BeginDisabledGroup(!HasSelection());
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RunBlue;
            if (GUILayout.Button(
                    "Clone Selected",
                    GUILayout.Height(22)
                ))
            {
                CloneSelected();
            }

            GUI.backgroundColor = previousBackground;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawCollectionList()
        {
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            EditorGUILayout.LabelField(
                GetCollectionLabel(),
                EditorGuiStyles.SectionHeader
            );
            GUILayout.Space(2);
            bool hasVisibleEntries = false;

            switch (_tab)
            {
                case 0:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.sceneSets.Select(x => x.setName).ToList(),
                        s.activeSceneSetIndex,
                        ref _selSet
                    );
                    break;
                case 1:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.scriptDefinitionSets.Select(x => x.setName).ToList(),
                        s.activeScriptDefinitionSetIndex,
                        ref _selDef
                    );
                    break;
                case 2:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.buildProfiles.Select(x => x.profileName).ToList(),
                        -1,
                        ref _selBuild,
                        s.buildProfiles.Select(
                            x => (BuildTarget?)x.buildConfiguration.platform
                        ).ToList()
                    );
                    break;
                case 4:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.flagDefinitions.Select((x, i) =>
                            FlagDisplayName(x, i, s)
                        ).ToList(),
                        -1,
                        ref _selFlag
                    );
                    break;
            }

            if (!hasVisibleEntries)
            {
                EditorGuiLayout.DrawEmptyState(
                    string.IsNullOrEmpty(_searchQuery)
                        ? $"No {GetCollectionLabel().ToLowerInvariant()} created."
                        : "No matching entries.",
                    string.IsNullOrEmpty(_searchQuery)
                        ? "Use the create button above to add one."
                        : "Clear or broaden the search and filter."
                );
            }

        }

        private bool HasSelection()
        {
            return _tab switch
            {
                0 => _selSet >= 0 && _selSet < _sceneSets.arraySize,
                1 => _selDef >= 0 && _selDef < _defineSets.arraySize,
                2 => _selBuild >= 0 && _selBuild < _buildProfiles.arraySize,
                4 => _selFlag >= 0 && _selFlag < _flagDefs.arraySize,
                _ => false
            };
        }

        private string GetAddButtonLabel()
        {
            return _tab switch
            {
                0 => "+ Scene Set",
                1 => "+ Define Set",
                2 => "+ Build Profile",
                4 => "+ Flag Definition",
                _ => string.Empty
            };
        }

        private bool DrawCollectionEntries(
            List<string> names, int activeIdx, ref int selIdx,
            List<BuildTarget?> platforms = null)
        {
            bool hasVisibleEntries = false;
            for (int i = 0; i < names.Count; i++)
            {
                if (!MatchesCollectionFilter(names[i], i, activeIdx))
                {
                    continue;
                }

                hasVisibleEntries = true;
                bool isSel = selIdx == i;
                bool isAct = activeIdx == i;
                bool isExpanded = _expandedEntryIndex == i;
                if (DrawCollectionCard(
                        names[i],
                        isSel,
                        isAct,
                        isExpanded,
                        platforms != null && i < platforms.Count
                            ? platforms[i]
                            : null,
                        ref selIdx,
                        i
                    ))
                {
                    break;
                }
            }

            return hasVisibleEntries;
        }

        private bool DrawCollectionCard(
            string entryName,
            bool isSelected,
            bool isActive,
            bool isExpanded,
            BuildTarget? platform,
            ref int selectedIndex,
            int entryIndex)
        {
            int entryCountBefore = GetCurrentCollectionCount();
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.BeginHorizontal();

            if (platform.HasValue)
            {
                GUIContent icon = PlatformIconUtility.GetPlatformIcon(
                    platform.Value
                );
                GUILayout.Label(
                    icon,
                    GUILayout.Width(22),
                    GUILayout.Height(22)
                );

                GUILayout.Label(
                    PlatformIconUtility.GetPlatformDisplayName(
                        platform.Value
                    ),
                    EditorStyles.miniLabel,
                    GUILayout.Width(92)
                );
            }

            GUIStyle titleStyle = isSelected
                ? EditorGuiStyles.HoverBoldLabel
                : EditorGuiStyles.HoverLabel;
            if (GUILayout.Button(
                    new GUIContent(entryName, entryName),
                    titleStyle,
                    GUILayout.ExpandWidth(true)
                ))
            {
                selectedIndex = entryIndex;
                _expandedEntryIndex = entryIndex;
            }

            if (GUILayout.Button(
                    isExpanded ? "Collapse" : "Expand",
                    GUILayout.Width(70)
                ))
            {
                selectedIndex = entryIndex;
                _expandedEntryIndex = isExpanded ? -1 : entryIndex;
            }

            EditorGUILayout.EndHorizontal();

            if (isExpanded)
            {
                GUILayout.Space(4);
                DrawPreview();
            }

            EditorGUILayout.EndVertical();
            Rect cardRect = GUILayoutUtility.GetLastRect();
            if (isActive)
            {
                DrawCardOutline(cardRect, EditorGuiColors.ActiveGreen);
            }

            GUILayout.Space(4);
            bool itemDeleted = GetCurrentCollectionCount() < entryCountBefore;
            if (itemDeleted)
            {
                _expandedEntryIndex = -1;
            }

            return itemDeleted;
        }

        private static void DrawCardOutline(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            const float Thickness = 2f;
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, Thickness),
                color
            );
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - Thickness, rect.width, Thickness),
                color
            );
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, Thickness, rect.height),
                color
            );
            EditorGUI.DrawRect(
                new Rect(rect.xMax - Thickness, rect.y, Thickness, rect.height),
                color
            );
        }

        private int GetCurrentCollectionCount()
        {
            NoBuildSettings settings = (NoBuildSettings)_so.targetObject;
            return _tab switch
            {
                0 => settings.sceneSets.Count,
                1 => settings.scriptDefinitionSets.Count,
                2 => settings.buildProfiles.Count,
                4 => settings.flagDefinitions.Count,
                _ => 0
            };
        }

        private string GetCollectionLabel()
        {
            return _tab switch
            {
                0 => "Scene Sets",
                1 => "Define Sets",
                2 => "Build Profiles",
                4 => "Flag Definitions",
                _ => string.Empty
            };
        }

        private string[] GetFilterLabels()
        {
            return _tab switch
            {
                0 => StateFilters,
                1 => StateFilters,
                2 => PlatformFilters,
                4 => FlagFilters,
                _ => Array.Empty<string>()
            };
        }

        private bool MatchesCollectionFilter(
            string entryName,
            int entryIndex,
            int activeIndex)
        {
            if (!string.IsNullOrWhiteSpace(_searchQuery)
                && entryName.IndexOf(
                    _searchQuery,
                    StringComparison.OrdinalIgnoreCase
                ) < 0)
            {
                return false;
            }

            return _tab switch
            {
                0 => MatchesStateFilter(entryIndex, activeIndex),
                1 => MatchesStateFilter(entryIndex, activeIndex),
                2 => MatchesPlatformFilter(entryIndex),
                4 => MatchesFlagFilter(entryIndex),
                _ => true
            };
        }

        private bool MatchesStateFilter(int entryIndex, int activeIndex)
        {
            return _filterIndex switch
            {
                1 => entryIndex == activeIndex,
                2 => entryIndex != activeIndex,
                _ => true
            };
        }

        private bool MatchesPlatformFilter(int entryIndex)
        {
            NoBuildSettings settings = (NoBuildSettings)_so.targetObject;
            BuildTarget platform = settings.buildProfiles[entryIndex]
                .buildConfiguration.platform;
            return _filterIndex switch
            {
                1 => platform == BuildTarget.Android,
                2 => platform != BuildTarget.Android,
                _ => true
            };
        }

        private bool MatchesFlagFilter(int entryIndex)
        {
            NoBuildSettings settings = (NoBuildSettings)_so.targetObject;
            FlagDefinitionType type = settings.flagDefinitions[entryIndex].type;
            return _filterIndex switch
            {
                1 => type == FlagDefinitionType.Template,
                2 => type == FlagDefinitionType.Custom,
                _ => true
            };
        }

        private void AddNew()
        {
            switch (_tab)
            {
                case 0:
                    _sceneSets.arraySize++; _so.ApplyModifiedProperties();
                    _selSet = _sceneSets.arraySize - 1;
                    _sceneSets.GetArrayElementAtIndex(_selSet)
                        .FindPropertyRelative("setName").stringValue = "Scene Set " + _sceneSets.arraySize;
                    break;
                case 1:
                    _defineSets.arraySize++; _so.ApplyModifiedProperties();
                    _selDef = _defineSets.arraySize - 1;
                    _defineSets.GetArrayElementAtIndex(_selDef)
                        .FindPropertyRelative("setName").stringValue = "Define Set " + _defineSets.arraySize;
                    break;
                case 2:
                    _buildProfiles.arraySize++; _so.ApplyModifiedProperties();
                    _selBuild = _buildProfiles.arraySize - 1;
                    _buildProfiles.GetArrayElementAtIndex(_selBuild)
                        .FindPropertyRelative("profileName").stringValue = "Build Profile " + _buildProfiles.arraySize;
                    break;
                case 4:
                    _flagDefs.arraySize++; _so.ApplyModifiedProperties();
                    _selFlag = _flagDefs.arraySize - 1;
                    _flagDefs.GetArrayElementAtIndex(_selFlag)
                        .FindPropertyRelative("name").stringValue = "Flag " + _flagDefs.arraySize;
                    _flagDefs.GetArrayElementAtIndex(_selFlag)
                        .FindPropertyRelative("id").stringValue = "flag-" + _flagDefs.arraySize;
                    break;
            }
        }

        private void CloneSelected()
        {
            NoBuildSettings settings =
                (NoBuildSettings)_so.targetObject;
            if (settings == null) return;

            switch (_tab)
            {
                case 0: CloneSceneSet(settings); break;
                case 1: CloneDefineSet(settings); break;
                case 2: CloneBuildProfile(settings); break;
                case 4: CloneFlagDefinition(settings); break;
            }

            _so.Update();
            EditorUtility.SetDirty(settings);
        }

        private void CloneSceneSet(NoBuildSettings settings)
        {
            if (_selSet < 0 || _selSet >= settings.sceneSets.Count)
                return;

            var source = settings.sceneSets[_selSet];
            var clone = new SceneSet
            {
                setName = source.setName + " (Clone)",
                scenes = source.scenes != null
                    ? source.scenes.ConvertAll(s => s.Clone())
                    : new List<SceneSlot>(),
                buildOrderOverride = source.buildOrderOverride != null
                    ? source.buildOrderOverride.ConvertAll(s => s.Clone())
                    : new List<SceneSlot>(),
                combinations = source.combinations != null
                    ? source.combinations.ConvertAll(
                        c => new SceneCombination
                        {
                            name = c.name,
                            enabled = c.enabled,
                            sceneReferences = c.sceneReferences != null
                                ? new List<SceneReference>(
                                    c.sceneReferences)
                                : new List<SceneReference>()
                        })
                    : new List<SceneCombination>()
            };

            settings.sceneSets.Insert(_selSet + 1, clone);
            _selSet++;
        }

        private void CloneDefineSet(NoBuildSettings settings)
        {
            if (_selDef < 0
                || _selDef >= settings.scriptDefinitionSets.Count)
                return;

            var source =
                settings.scriptDefinitionSets[_selDef];
            var clone = new ScriptDefinitionSet
            {
                setName = source.setName + " (Clone)",
                slots = source.slots != null
                    ? source.slots.ConvertAll(s =>
                        new ScriptDefinitionSlot
                        {
                            defineSymbol = s.defineSymbol,
                            enabled = s.enabled
                        })
                    : new List<ScriptDefinitionSlot>()
            };

            settings.scriptDefinitionSets.Insert(
                _selDef + 1, clone);
            _selDef++;
        }

        private void CloneBuildProfile(NoBuildSettings settings)
        {
            if (_selBuild < 0
                || _selBuild >= settings.buildProfiles.Count)
                return;

            var source = settings.buildProfiles[_selBuild];

            // Deep-clone BuildConfiguration via JSON
            var sourceCfg = source.buildConfiguration;
            var cloneCfg = new BuildConfiguration();
            JsonUtility.FromJsonOverwrite(
                JsonUtility.ToJson(sourceCfg), cloneCfg);

            // Clone BuildNameTemplates
            var cloneNameTpl = new BuildNameTemplate
            {
                template = source.buildNameTemplate?.template
                ?? ""
            };
            var cloneFolder = new BuildNameTemplate
            {
                template = source.buildFolder?.template
                ?? ""
            };

            var clone = new BuildProfile
            {
                profileName = source.profileName + " (Clone)",
                sceneSetIndex = source.sceneSetIndex,
                scriptDefinitionSetIndex =
                    source.scriptDefinitionSetIndex,
                buildConfiguration = cloneCfg,
                buildNameTemplate = cloneNameTpl,
                buildFolder = cloneFolder
            };

            settings.buildProfiles.Insert(
                _selBuild + 1, clone);
            _selBuild++;
            _selFlag++;
        }

        private void CloneFlagDefinition(NoBuildSettings settings)
        {
            if (_selFlag < 0
                || _selFlag >= settings.flagDefinitions.Count)
                return;

            var source = settings.flagDefinitions[_selFlag];
            var clone = new FlagDefinition
            {
                id = source.id,
                name = source.name + " (Clone)",
                type = source.type,
                scriptDefinitionSetIndex =
                    source.scriptDefinitionSetIndex,
                scriptDefinitionSlotIndex =
                    source.scriptDefinitionSlotIndex,
                customDefineSymbol =
                    source.customDefineSymbol,
                trueFlag = source.trueFlag,
                falseFlag = source.falseFlag
            };

            settings.flagDefinitions.Insert(
                _selFlag + 1, clone);
            _selFlag++;
        }

        private static string FlagDisplayName(
            FlagDefinition flag, int index,
            NoBuildSettings settings)
        {
            string label = string.IsNullOrWhiteSpace(
                flag.name) ? "New Flag" : flag.name;
            if (!string.IsNullOrWhiteSpace(flag.id))
                label += $" ({flag.id})";
            return $"{index + 1}. {label}";
        }

        // ══════════════════════════════════════════════════
        // ── Preview Panel
        // ══════════════════════════════════════════════════

        private void DrawPreview()
        {
            switch (_tab) { case 0: DrawSceneSetPreview(); break; case 1: DrawDefinePreview(); break; case 2: DrawBuildPreview(); break; case 4: DrawFlagPreview(); break; }
        }

        // ──────────────────────────────────────────────
        // Scene Set Preview
        // ──────────────────────────────────────────────

        private void DrawSceneSetPreview()
        {
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_selSet < 0 || _selSet >= _sceneSets.arraySize)
            { EditorGUILayout.LabelField("Select a scene set above.", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true)); return; }

            var sp = _sceneSets.GetArrayElementAtIndex(_selSet);
            var nameP = sp.FindPropertyRelative("setName");
            var scenesP = sp.FindPropertyRelative("scenes");
            var combosP = sp.FindPropertyRelative("combinations");
            bool active = s.activeSceneSetIndex == _selSet;

            EditorGuiLayout.DrawSectionHeader(
                "Scene Set Configuration",
                active ? "Currently active." : "Select this set to switch all configured scenes."
            );
            EditorGUILayout.BeginHorizontal();
            Color oldC = GUI.contentColor;
            if (active) GUI.contentColor = EditorGuiColors.ActiveGreen;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            if (!active)
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.BuildGreen;
                if (GUILayout.Button("Set Active", GUILayout.Width(80)))
                {
                    s.activeSceneSetIndex = _selSet;
                    EditorUtility.SetDirty(s);
                }

                GUI.backgroundColor = previousBackground;
            }

            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                {
                    _sceneSets.DeleteArrayElementAtIndex(_selSet); _so.ApplyModifiedProperties();
                    if (s.activeSceneSetIndex == _selSet) s.activeSceneSetIndex = -1;
                    if (s.activeSceneSetIndex > _selSet) s.activeSceneSetIndex--;
                    _selSet = Mathf.Min(_selSet, _sceneSets.arraySize - 1); return;
                }
            }
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Name
            nameP.stringValue = LblTxt("Name", nameP.stringValue);
            GUILayout.Space(6);

            // ── Scenes (reorderable with drag handles) ──
            EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);
            DrawSceneReorderableList(scenesP);

            if (GUILayout.Button("+ Add Scene", GUILayout.Height(22),
                    GUILayout.Width(100)))
            {
                scenesP.arraySize++;
                scenesP.GetArrayElementAtIndex(scenesP.arraySize - 1)
                    .FindPropertyRelative("enabled").boolValue = true;
            }
            GUILayout.Space(8);

            DrawShortcutCombinations(combosP, scenesP);
        }

        private void DrawShortcutCombinations(
            SerializedProperty combinations,
            SerializedProperty parentScenes)
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField(
                "Shortcut Combinations",
                EditorGuiStyles.SectionHeader
            );
            EditorGUILayout.HelpBox(
                "Toolbar shortcuts [1]–[9] open these scene combinations when this set is active.",
                MessageType.None
            );

            if (combinations.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No shortcut combinations configured.",
                    MessageType.Info
                );
            }

            for (int index = 0; index < combinations.arraySize; index++)
            {
                if (DrawShortcutCombinationCard(
                        index,
                        combinations,
                        parentScenes
                    ))
                {
                    break;
                }
            }

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.BuildGreen;
            if (GUILayout.Button(
                    "+ Add Shortcut Combination",
                    GUILayout.Height(24)
                ))
            {
                combinations.arraySize++;
                SerializedProperty combination = combinations
                    .GetArrayElementAtIndex(combinations.arraySize - 1);
                combination.FindPropertyRelative("name").stringValue =
                    "Shortcut " + combinations.arraySize;
                combination.FindPropertyRelative("enabled").boolValue = true;
            }

            GUI.backgroundColor = previousBackground;
            EditorGUILayout.EndVertical();
        }

        private bool DrawShortcutCombinationCard(
            int index,
            SerializedProperty combinations,
            SerializedProperty parentScenes)
        {
            SerializedProperty combination = combinations
                .GetArrayElementAtIndex(index);
            SerializedProperty name = combination.FindPropertyRelative("name");
            SerializedProperty enabled = combination.FindPropertyRelative("enabled");
            SerializedProperty references = combination.FindPropertyRelative(
                "sceneReferences"
            );

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.BeginHorizontal();
            Color previousBadgeColor = GUI.color;
            GUI.color = enabled.boolValue
                ? EditorGuiColors.ActiveGreen
                : EditorGuiColors.InactiveGray;
            GUILayout.Label(
                $"{index + 1}",
                EditorGuiStyles.Badge,
                GUILayout.Width(24),
                GUILayout.Height(20)
            );
            GUI.color = previousBadgeColor;

            EditorGUILayout.LabelField(
                $"Shortcut {index + 1}",
                EditorStyles.boldLabel
            );
            GUILayout.FlexibleSpace();
            enabled.boolValue = EditorGUILayout.ToggleLeft(
                "Enabled",
                enabled.boolValue,
                GUILayout.Width(72)
            );

            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            bool shouldDelete = GUILayout.Button(
                "Delete",
                GUILayout.Width(58)
            );
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();

            if (shouldDelete)
            {
                combinations.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndVertical();
                return true;
            }

            EditorGuiLayout.LabeledTextField("Name", name);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Scenes", EditorStyles.miniBoldLabel);
            DrawComboSceneReferences(references, parentScenes);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
            return false;
        }

        private void DrawSceneReorderableList(SerializedProperty scenesP)
        {
            if (scenesP.arraySize == 0)
            {
                EditorGUILayout.LabelField("  (no scenes)",
                    EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < scenesP.arraySize; i++)
            {
                var slot = scenesP.GetArrayElementAtIndex(i);
                var en = slot.FindPropertyRelative("enabled");
                var sc = slot.FindPropertyRelative("scene");

                EditorGUILayout.BeginHorizontal();

                // Toggle + object field
                en.boolValue = EditorGUILayout.Toggle(
                    en.boolValue, GUILayout.Width(16));
                sc.objectReferenceValue =
                    EditorGUI.ObjectField(
                        GUILayoutUtility.GetRect(
                            GUIContent.none, GUI.skin.textField,
                            GUILayout.ExpandWidth(true)),
                        sc.objectReferenceValue,
                        typeof(SceneAsset), false);

                // Up button
                GUI.enabled = i > 0;
                if (GUILayout.Button("\u25B2",
                        GUILayout.Width(24), GUILayout.Height(18)))
                {
                    scenesP.MoveArrayElement(i, i - 1);
                    _so.ApplyModifiedProperties();
                }
                GUI.enabled = true;

                // Down button
                GUI.enabled = i < scenesP.arraySize - 1;
                if (GUILayout.Button("\u25BC",
                        GUILayout.Width(24), GUILayout.Height(18)))
                {
                    scenesP.MoveArrayElement(i, i + 1);
                    _so.ApplyModifiedProperties();
                }
                GUI.enabled = true;

                // Remove button
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("\u00D7",
                        GUILayout.Width(22), GUILayout.Height(18)))
                {
                    scenesP.DeleteArrayElementAtIndex(i);
                    _so.ApplyModifiedProperties();
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawComboSceneReferences(
            SerializedProperty sceneRefsP,
            SerializedProperty parentScenesP)
        {
            // Build display names from parent scene set
            List<string> sceneNames = new();
            for (int i = 0; i < parentScenesP.arraySize; i++)
            {
                var slot = parentScenesP.GetArrayElementAtIndex(i);
                var dName = slot.FindPropertyRelative("displayName");
                var scObj = slot.FindPropertyRelative("scene");
                string name = dName.stringValue;
                if (string.IsNullOrEmpty(name)
                    && scObj.objectReferenceValue != null)
                    name = ((SceneAsset)scObj.objectReferenceValue)
                        .name;
                if (string.IsNullOrEmpty(name))
                    name = $"Scene {i}";
                sceneNames.Add(name);
            }

            if (sceneNames.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add scenes to this scene set before creating a shortcut combination.",
                    MessageType.Info
                );
                return;
            }

            string[] nameArray = sceneNames.ToArray();

            for (int j = 0; j < sceneRefsP.arraySize; j++)
            {
                var refP = sceneRefsP.GetArrayElementAtIndex(j);
                var indexP = refP.FindPropertyRelative("sceneIndex");
                var enP = refP.FindPropertyRelative("enabled");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"Scene {j + 1}",
                    GUILayout.Width(LabelW)
                );

                enP.boolValue = EditorGUILayout.Toggle(
                    enP.boolValue, GUILayout.Width(16));

                indexP.intValue = Mathf.Clamp(
                    indexP.intValue,
                    0,
                    sceneNames.Count - 1
                );

                indexP.intValue = EditorGUILayout.Popup(
                    indexP.intValue, nameArray);

                Color previousRemoveBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.RemoveRed;
                if (GUILayout.Button("Remove", GUILayout.Width(62)))
                {
                    sceneRefsP.DeleteArrayElementAtIndex(j);
                    GUI.backgroundColor = previousRemoveBackground;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                GUI.backgroundColor = previousRemoveBackground;
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(
                    "+ Add Referenced Scene",
                    GUILayout.Height(20)
                ))
            {
                sceneRefsP.arraySize++;
                var newRef = sceneRefsP.GetArrayElementAtIndex(
                    sceneRefsP.arraySize - 1);
                newRef.FindPropertyRelative("enabled").boolValue = true;
                newRef.FindPropertyRelative("sceneIndex").intValue = 0;
            }
        }

        // ──────────────────────────────────────────────
        // Define Preview
        // ──────────────────────────────────────────────

        private void DrawDefinePreview()
        {
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_selDef < 0 || _selDef >= _defineSets.arraySize)
            { EditorGUILayout.LabelField("Select a define set.", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true)); return; }

            var sp = _defineSets.GetArrayElementAtIndex(_selDef);
            var nameP = sp.FindPropertyRelative("setName");
            var slotsP = sp.FindPropertyRelative("slots");
            bool active = s.activeScriptDefinitionSetIndex == _selDef;

            EditorGuiLayout.DrawSectionHeader(
                "Script Define Set Configuration",
                active ? "Currently applied." : "Apply this set to update scripting defines."
            );
            EditorGUILayout.BeginHorizontal();
            Color oldC = GUI.contentColor;
            if (active) GUI.contentColor = EditorGuiColors.ActiveGreen;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            if (!active)
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.BuildGreen;
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    s.activeScriptDefinitionSetIndex = _selDef;
                    EditorUtility.SetDirty(s);
                    _so.ApplyModifiedProperties();
                    ScriptDefinitionSwitcher.ApplySet(s.scriptDefinitionSets[_selDef]);
                }

                GUI.backgroundColor = previousBackground;
            }
            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                {
                    _defineSets.DeleteArrayElementAtIndex(_selDef); _so.ApplyModifiedProperties();
                    if (s.activeScriptDefinitionSetIndex == _selDef) s.activeScriptDefinitionSetIndex = -1;
                    if (s.activeScriptDefinitionSetIndex > _selDef) s.activeScriptDefinitionSetIndex--;
                    _selDef = Mathf.Min(_selDef, _defineSets.arraySize - 1); return;
                }
            }
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            nameP.stringValue = LblTxt("Name", nameP.stringValue);
            GUILayout.Space(6);

            EditorGUILayout.LabelField("Symbols", EditorStyles.boldLabel);
            if (slotsP.arraySize == 0)
                EditorGUILayout.LabelField("  (no symbols)", EditorStyles.miniLabel);
            for (int i = 0; i < slotsP.arraySize; i++)
            {
                var slot = slotsP.GetArrayElementAtIndex(i);
                var def = slot.FindPropertyRelative("defineSymbol");
                var en = slot.FindPropertyRelative("enabled");
                EditorGUILayout.BeginHorizontal();
                en.boolValue = EditorGUILayout.Toggle(en.boolValue, GUILayout.Width(16));
                def.stringValue = EditorGUILayout.TextField(def.stringValue);
                if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                { slotsP.DeleteArrayElementAtIndex(i); break; }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Symbol", GUILayout.Height(22))) slotsP.arraySize++;
        }

        // ──────────────────────────────────────────────
        // Build Preview
        // ──────────────────────────────────────────────

        private void DrawBuildPreview()
        {
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_selBuild < 0 || _selBuild >= _buildProfiles.arraySize)
            { EditorGUILayout.LabelField("Select a build profile.", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true)); return; }

            var pp = _buildProfiles.GetArrayElementAtIndex(_selBuild);
            var nameP = pp.FindPropertyRelative("profileName");
            var ssP = pp.FindPropertyRelative("sceneSetIndex");
            var dsP = pp.FindPropertyRelative("scriptDefinitionSetIndex");
            var cfgP = pp.FindPropertyRelative("buildConfiguration");
            var folderP = pp.FindPropertyRelative("buildFolder.template");
            var nameTplP = pp.FindPropertyRelative("buildNameTemplate.template");
            BuildProfile profile = s.buildProfiles[_selBuild];
            string readiness = profile.HasValidSceneSet(s)
                ? "Scene set configured."
                : "Select a scene set before building.";

            EditorGuiLayout.DrawSectionHeader(
                "Build Profile Configuration",
                $"{profile.buildConfiguration.platform} • {readiness}"
            );

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            Color previousBuildBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.BuildGreen;
            if (GUILayout.Button(
                    PlatformIconUtility.BuildActionIcon,
                    GUILayout.Width(78)
                ))
            {
                _so.ApplyModifiedProperties();
                var selectedProfile = s.buildProfiles[_selBuild];
                CustomPopupDropdown.ShowLastRect(
                    NoBuildDropdowns.CreateBuildConfirmPopup(
                        selectedProfile, s,
                        () => BuildExecutor.Build(selectedProfile),
                        (option, serial) =>
                            BuildExecutor
                                .BuildAndRunWithOptions(
                                    selectedProfile, option,
                                    serial
                                )
                        )
                    );
            }
            GUI.backgroundColor = previousBuildBackground;
            if (s.buildProfiles[_selBuild].buildConfiguration.platform == BuildTarget.Android)
            {
                Color previousRunBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.RunBlue;
                if (GUILayout.Button(
                        PlatformIconUtility.BuildAndRunActionIcon,
                        GUILayout.Width(112)
                    ))
                {
                    _so.ApplyModifiedProperties();
                    CustomPopupDropdown.ShowLastRect(
                        NoBuildDropdowns.CreateDeviceSelectPopup(
                            (option, serial) =>
                            {
                                BuildExecutor
                                    .BuildAndRunWithOptions(
                                        s.buildProfiles[
                                            _selBuild],
                                        option, serial);
                            }
                        )
                    );
                }
                GUI.backgroundColor = previousRunBackground;
            }
            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                {
                    _buildProfiles.DeleteArrayElementAtIndex(_selBuild); _so.ApplyModifiedProperties();
                    _selBuild = Mathf.Min(_selBuild, _buildProfiles.arraySize - 1); return;
                }
            }
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            nameP.stringValue = LblTxt("Name", nameP.stringValue);

            string[] ssN = s.sceneSets.Select(x => x.setName).ToArray();
            ssP.intValue = LPopup("Scene Set", ssP.intValue + 1,
                new[] { "(None)" }.Concat(ssN).ToArray()) - 1;

            string[] dsN = s.scriptDefinitionSets.Select(x => x.setName).ToArray();
            dsP.intValue = LPopup("Define Set", dsP.intValue + 1,
                new[] { "(None)" }.Concat(dsN).ToArray()) - 1;
            GUILayout.Space(6);

            // Build folder with [?]
            LblTxtHint("Build Folder", folderP);
            string fPrev = BuildNameResolver.Resolve(folderP.stringValue,
                _selBuild < s.buildProfiles.Count ? s.buildProfiles[_selBuild] : null, s);
            EditorGUILayout.LabelField("", "\u2192 " + fPrev, EditorStyles.miniLabel);

            // Build name with [?]
            LblTxtHint("Build Name", nameTplP);
            string nPrev = BuildNameResolver.Resolve(nameTplP.stringValue,
                _selBuild < s.buildProfiles.Count ? s.buildProfiles[_selBuild] : null, s);
            EditorGUILayout.LabelField("", "\u2192 " + nPrev, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("", "Full: " + fPrev + "/" + nPrev, EditorStyles.miniLabel);
            GUILayout.Space(8);

            // ── Build Configuration (two-column, one per line) ──
            var platP = cfgP.FindPropertyRelative("platform");
            DrawPlatformSelector(platP);
            BuildTarget selPlat = (BuildTarget)platP.enumValueIndex;
            GUILayout.Space(4);

            // General
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            var devBuildP = cfgP.FindPropertyRelative("developmentBuild");
            LblChk("Development Build", devBuildP);
            if (devBuildP.boolValue)
            {
                EditorGUI.indentLevel++;
                LblChk(
                    "Script Debugging",
                    cfgP.FindPropertyRelative("allowDebugging")
                );
                LblChk(
                    "Connect Profiler",
                    cfgP.FindPropertyRelative("connectWithProfiler")
                );
                EditorGUI.indentLevel--;
            }

            var sbeP = cfgP.FindPropertyRelative("scriptingBackend");
            LblBtnGroup("Scripting Backend", sbeP,
                new[] { "Mono", "IL2CPP" },
                new[] { (int)ScriptingImplementation.Mono2x, (int)ScriptingImplementation.IL2CPP });
            if (sbeP.enumValueIndex
                == (int)ScriptingImplementation.IL2CPP)
            {
                EditorGUI.indentLevel++;
                LblBtnGroup(
                    "IL2CPP Code Gen",
                    cfgP.FindPropertyRelative("il2CppCodeGeneration"),
                    new[] { "Opt Size", "Opt Speed" },
                    new[]
                    {
                        (int)Il2CppCodeGeneration.OptimizeSize,
                        (int)Il2CppCodeGeneration.OptimizeSpeed
                    }
                );
                LblChk(
                    "Strip Engine Code",
                    cfgP.FindPropertyRelative("stripEngineCode")
                );
                EditorGUI.indentLevel--;
            }

            LblProp("Stripping Level", cfgP.FindPropertyRelative("strippingLevel"));
            LblTxt("Bundle Identifier", cfgP.FindPropertyRelative("bundleIdentifierOverride"));
            LblTxt("Product Name", cfgP.FindPropertyRelative("productNameOverride"));

            // Windows
            if (selPlat == BuildTarget.StandaloneWindows || selPlat == BuildTarget.StandaloneWindows64)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("Windows", EditorStyles.boldLabel);
                LblChk("Create VS Solution", cfgP.FindPropertyRelative("windowsCreateVSProject"));
                LblChk("Copy PDB Files", cfgP.FindPropertyRelative("windowsCopyPDB"));
                LblChk("Copy References", cfgP.FindPropertyRelative("windowsCopyReferences"));
            }

            // Android
            if (selPlat == BuildTarget.Android)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("Android", EditorStyles.boldLabel);
                LblChk("Export Project", cfgP.FindPropertyRelative("androidExportProject"));
                SerializedProperty buildAppBundle = cfgP.FindPropertyRelative(
                    "androidBuildAppBundle"
                );
                LblChk("Build App Bundle (AAB)", buildAppBundle);
                if (!buildAppBundle.boolValue)
                {
                    EditorGUI.indentLevel++;
                    LblChk(
                        "Split Binary",
                        cfgP.FindPropertyRelative("androidSplitBinary")
                    );
                    EditorGUI.indentLevel--;
                }
#if UNITY_ANDROID
                LblProp("Debug Symbols", cfgP.FindPropertyRelative(
                    "debugSymbolLevel"));
                LblSymbolFormat("Symbol Output", cfgP.FindPropertyRelative(
                    "debugSymbolFormat"));
#endif
                LblProp("Target Architectures", cfgP.FindPropertyRelative("androidTargetArchitecture"));
            }

            // iOS
            if (selPlat == BuildTarget.iOS)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("iOS", EditorStyles.boldLabel);
                LblChk("Symlink Framework", cfgP.FindPropertyRelative("iosSymlinkFramework"));
                LblChk("Run in Xcode", cfgP.FindPropertyRelative("iosRunInXcode"));
                LblTxt("Team ID", cfgP.FindPropertyRelative("iosTeamId"));
                LblChk("Automatic Signing", cfgP.FindPropertyRelative("iosAutomaticSigning"));
            }
        }

        // ──────────────────────────────────────────────
        // Flag Preview
        // ──────────────────────────────────────────────

        private void DrawFlagPreview()
        {
            NoBuildSettings s =
                (NoBuildSettings)_so.targetObject;
            if (_selFlag < 0
                || _selFlag >= _flagDefs.arraySize)
            {
                EditorGUILayout.LabelField(
                    "Select a flag definition above.",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.ExpandHeight(true));
                return;
            }

            var fp = _flagDefs.GetArrayElementAtIndex(
                _selFlag);
            var nameP = fp.FindPropertyRelative("name");
            var typeP = fp.FindPropertyRelative("type");
            var setIdxP = fp.FindPropertyRelative(
                "scriptDefinitionSetIndex");
            var customP = fp.FindPropertyRelative(
                "customDefineSymbol");
            var trueP = fp.FindPropertyRelative("trueFlag");
            var falseP = fp.FindPropertyRelative("falseFlag");

            EditorGuiLayout.DrawSectionHeader(
                "Flag Definition Configuration",
                "Resolve a build-name placeholder from a template or custom define."
            );
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                FlagDisplayName(
                    s.flagDefinitions[_selFlag],
                    _selFlag, s),
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete",
                    GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog(
                        "Delete",
                        "Delete this flag definition?",
                        "Delete", "Cancel"))
                {
                    _flagDefs.DeleteArrayElementAtIndex(
                        _selFlag);
                    _so.ApplyModifiedProperties();
                    _selFlag = Mathf.Min(
                        _selFlag,
                        _flagDefs.arraySize - 1);
                    return;
                }
            }
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Name
            nameP.stringValue = LblTxt("Name", nameP.stringValue);
            var idP = fp.FindPropertyRelative("id");
            idP.stringValue = LblTxt("Id", idP.stringValue);
            GUILayout.Space(4);

            // Type button group
            LblBtnGroup("Type", typeP,
                new[] { "Template", "Custom" },
                new[] { (int)FlagDefinitionType.Template, (int)FlagDefinitionType.Custom });
            GUILayout.Space(4);

            // Conditional: Template → set index popup,
            // Custom → text field
            FlagDefinitionType newType =
                (FlagDefinitionType)typeP.enumValueIndex;
            if (newType == FlagDefinitionType.Template)
            {
                string[] setNames = s.scriptDefinitionSets
                    .Select(x => x.setName).ToArray();
                if (setNames.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No Script Definition Sets defined. " +
                        "Create one in the Defines tab first.",
                        MessageType.Info);
                }
                else
                {
                    setIdxP.intValue = LPopup(
                        "Defines Set",
                        setIdxP.intValue + 1,
                        new[] { "(None)" }
                            .Concat(setNames)
                            .ToArray()) - 1;

                    // Slot dropdown — show symbols in the
                    // selected set
                    if (setIdxP.intValue >= 0
                        && setIdxP.intValue
                        < s.scriptDefinitionSets.Count)
                    {
                        ScriptDefinitionSet selectedSet = s.scriptDefinitionSets[setIdxP.intValue];
                        string[] slotNames = selectedSet.slots.Select(sl => sl.defineSymbol ?? "(empty)").ToArray();

                        if (slotNames.Length > 0)
                        {
                            var slotIdxP = fp.FindPropertyRelative("scriptDefinitionSlotIndex");
                            slotIdxP.intValue = LPopup(
                                "Symbol", slotIdxP.intValue + 1,
                                new[] { "(None)" }.Concat(slotNames).ToArray()
                            ) - 1;
                        }
                        else
                        {
                            EditorGUILayout.LabelField(
                                "Symbol",
                                "(set has no symbols)",
                                EditorStyles.miniLabel
                            );
                        }
                    }
                }
            }
            else
            {
                LblTxt("Define Symbol", customP);
            }

            GUILayout.Space(6);
            LblTxt("True Flag", trueP);
            LblTxt("False Flag", falseP);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Result Preview",
                EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Active:  ",
                GUILayout.Width(65));
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(
                    trueP.stringValue)
                    ? "(empty)"
                    : trueP.stringValue,
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Inactive:",
                GUILayout.Width(65));
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(
                    falseP.stringValue)
                    ? "(empty)"
                    : falseP.stringValue,
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════
        // ── Device Panel (ADB)
        // ══════════════════════════════════════════════════

        private void DrawDevicePanel()
        {
            EditorGuiLayout.DrawHeaderBox(
                "Devices",
                "Manage Android Debug Bridge connections and install builds."
            );
            string adbPath = AdbUtility.AdbPath;
            bool adbFound = !string.IsNullOrEmpty(adbPath)
                && System.IO.File.Exists(adbPath);

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Environment", EditorGuiStyles.SectionHeader);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ADB:", GUILayout.Width(40));
            Color previousAdbColor = GUI.color;
            GUI.color = adbFound
                ? EditorGuiColors.ActiveGreen
                : EditorGuiColors.RemoveRed;
            EditorGUILayout.LabelField(
                adbFound ? "Found" : "Not found",
                EditorStyles.boldLabel, GUILayout.Width(65));
            GUI.color = previousAdbColor;
            EditorGUILayout.LabelField(adbPath ?? "(unknown)",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            string pkg = "";
#if UNITY_ANDROID
            pkg = PlayerSettings.GetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android);
#endif
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Package:",
                GUILayout.Width(60));
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(pkg) ? "(not set)"
                    : pkg,
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Operations", EditorGuiStyles.SectionHeader);
            EditorGUILayout.BeginHorizontal();
            Color previousRefreshBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RunBlue;
            if (GUILayout.Button("\u21BB Refresh Devices",
                    GUILayout.Height(26)))
            {
                RefreshDevices();
            }
            GUI.backgroundColor = previousRefreshBackground;

            GUILayout.Space(8);
            Color previousApkBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.BuildGreen;
            if (GUILayout.Button("Install APK...",
                    GUILayout.Height(26)))
            {
                string picked = EditorUtility.OpenFilePanel(
                    "Select APK", "", "apk");
                if (!string.IsNullOrEmpty(picked))
                    _installApkPath = picked;
            }
            GUI.backgroundColor = previousApkBackground;

            GUILayout.Space(8);
            Color previousAabBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.WarningAmber;
            if (GUILayout.Button("Install AAB...",
                    GUILayout.Height(26)))
            {
                string picked = EditorUtility.OpenFilePanel(
                    "Select AAB", "", "aab");
                if (!string.IsNullOrEmpty(picked))
                    _installAabPath = picked;
            }
            GUI.backgroundColor = previousAabBackground;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField(
                "Connected Devices",
                EditorGuiStyles.SectionHeader
            );

            var devices = GetCachedDevices();

            if (devices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No devices connected.\n" +
                    "Connect a device via USB and enable USB debugging, " +
                    "or start an emulator.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i];
                    DrawDeviceRow(d, pkg, i);
                    if (i < devices.Count - 1)
                        GUILayout.Space(2);
                }
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(6);

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField(
                "Wireless ADB",
                EditorGuiStyles.SectionHeader
            );

            EditorGUILayout.BeginHorizontal();
            Color previousScanBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RunBlue;
            if (GUILayout.Button(
                    "\uD83D\uDD0D Scan Network",
                    GUILayout.Height(26)))
            {
                EditorUtility.DisplayProgressBar(
                    "NoBuild",
                    "Scanning for wireless devices...",
                    0.5f);
                _wirelessDevices =
                    AdbUtility.ScanWirelessDevices();
                EditorUtility.ClearProgressBar();
                if (_wirelessDevices.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "NoBuild",
                        "No wireless devices found.\n" +
                        "Ensure devices have wireless " +
                        "debugging enabled.",
                        "OK");
                }
            }
            GUI.backgroundColor = previousScanBackground;
            GUILayout.Space(8);
            if (_wirelessDevices != null
                && _wirelessDevices.Count > 0)
            {
                _pairingCode =
                    EditorGUILayout.TextField(
                        "Pair Code:", _pairingCode,
                        GUILayout.Width(140));
            }
            EditorGUILayout.EndHorizontal();

            if (_wirelessDevices == null
                || _wirelessDevices.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "  (no devices discovered yet)",
                    EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0;
                    i < _wirelessDevices.Count; i++)
                {
                    var w = _wirelessDevices[i];
                    DrawWirelessRow(w, i);
                }
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(6);

            string lastSerial = EditorPrefs.GetString(
                "NoBuild_LastAdbDevice", "");
            if (!string.IsNullOrEmpty(lastSerial))
            {
                EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
                EditorGUILayout.LabelField(
                    "Remembered Device",
                    EditorGuiStyles.SectionHeader
                );
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(lastSerial,
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                Color previousForgetBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.RemoveRed;
                if (GUILayout.Button("Forget",
                        GUILayout.Width(65)))
                {
                    EditorPrefs.DeleteKey(
                        "NoBuild_LastAdbDevice");
                }
                GUI.backgroundColor = previousForgetBackground;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawDeviceRow(
            AdbDeviceInfo device, string packageName, int index)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // State dot
            bool isOnline = device.State == "device";
            GUI.color = isOnline ? Color.green : Color.yellow;
            GUILayout.Label("\u25CF", GUILayout.Width(16));
            GUI.color = Color.white;

            // Model + Serial
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(device.Model,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(device.Serial,
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Launch button
            GUI.enabled = isOnline && !string.IsNullOrEmpty(
                packageName);
            GUI.backgroundColor =
                new Color(0.3f, 0.6f, 0.3f);
            if (GUILayout.Button("Launch", GUILayout.Width(65),
                    GUILayout.Height(24)))
            {
                AdbUtility.LaunchApp(device.Serial,
                    packageName);
                EditorPrefs.SetString(
                    "NoBuild_LastAdbDevice",
                    device.Serial);
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Install APK button (if APK path is set)
            if (!string.IsNullOrEmpty(_installApkPath)
                && isOnline)
            {
                GUI.backgroundColor =
                    new Color(0.45f, 0.45f, 0.9f);
                if (GUILayout.Button(
                        "Install",
                        GUILayout.Width(60),
                        GUILayout.Height(24)))
                {
                    string apkFile = System.IO.Path.GetFileName(
                        _installApkPath);
                    if (EditorUtility.DisplayDialog(
                            "Install APK",
                            $"Install {apkFile} to " +
                            $"{device.Model}?",
                            "Install", "Cancel"))
                    {
                        EditorUtility.DisplayProgressBar(
                            "NoBuild",
                            $"Installing {apkFile}...",
                            0.5f);
                        bool ok =
                            DeviceInstaller
                                .InstallWithRecovery(
                                    device.Serial,
                                    device.DisplayName,
                                    packageName,
                                    () => AdbUtility
                                        .InstallApk(
                                            _installApkPath,
                                            device.Serial));
                        EditorUtility.ClearProgressBar();
                        if (ok)
                        {
                            EditorPrefs.SetString(
                                "NoBuild_LastAdbDevice",
                                device.Serial);
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            // Install AAB button (if AAB path is set)
            if (!string.IsNullOrEmpty(_installAabPath)
                && isOnline)
            {
                GUI.backgroundColor =
                    new Color(0.7f, 0.45f, 0.15f);
                if (GUILayout.Button(
                        "Install AAB",
                        GUILayout.Width(75),
                        GUILayout.Height(24)))
                {
                    InstallAabToDevice(device);
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }


        private void InstallAabToDevice(AdbDeviceInfo device)
        {
            string aabFile = System.IO.Path.GetFileName(
                _installAabPath);
            if (!EditorUtility.DisplayDialog(
                    "Install AAB",
                    $"Install {aabFile} to " +
                    $"{device.Model}?\n\n" +
                    "The AAB will be converted to APKS first.",
                    "Install", "Cancel"))
            {
                return;
            }

            string tempApksPath = null;
            try
            {
                EditorUtility.DisplayProgressBar(
                    "NoBuild",
                    $"Converting {aabFile}...",
                    0.3f);
                tempApksPath =
                    AabUtility.BuildApks(
                        _installAabPath);

                EditorUtility.DisplayProgressBar(
                    "NoBuild",
                    $"Installing to {device.Model}...",
                    0.6f);
                bool ok =
                    DeviceInstaller.InstallWithRecovery(
                        device.Serial,
                        device.DisplayName,
                        AdbUtility.GetPackageName(),
                        () => AabUtility.InstallApks(
                            tempApksPath,
                            device.Serial));
                EditorUtility.ClearProgressBar();

                if (ok)
                {
                    EditorPrefs.SetString(
                        "NoBuild_LastAdbDevice",
                        device.Serial);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "NoBuild — Install Failed",
                    $"AAB install failed.\n\n" +
                    $"{ex.Message}",
                    "OK");
            }
            finally
            {
                if (tempApksPath != null)
                    AabUtility.Cleanup(tempApksPath);
            }
        }

        private void DrawWirelessRow(
            WirelessDeviceInfo device, int index)
        {
            EditorGUILayout.BeginHorizontal(
                EditorStyles.helpBox);

            // Status dot
            GUI.color = device.IsConnected
                ? Color.green : Color.gray;
            GUILayout.Label("\u25CF", GUILayout.Width(16));
            GUI.color = Color.white;

            // Device info
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(
                device.IsConnected
                    ? $"{device.Serial} \u2713 Connected"
                    : device.Serial,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                device.Endpoint,
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (device.IsConnected)
            {
                GUI.backgroundColor =
                    new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("Disconnect",
                        GUILayout.Width(85),
                        GUILayout.Height(22)))
                {
                    AdbUtility.DisconnectWireless(
                        device.IpAddress,
                        device.Port);
                    RefreshDevices();
                    _wirelessDevices =
                        AdbUtility.ScanWirelessDevices();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor =
                    new Color(0.3f, 0.6f, 0.3f);
                if (GUILayout.Button("Connect",
                        GUILayout.Width(70),
                        GUILayout.Height(22)))
                {
                    bool ok = AdbUtility
                        .ConnectWireless(
                            device.IpAddress,
                            device.Port);
                    if (ok)
                    {
                        RefreshDevices();
                        _wirelessDevices =
                            AdbUtility
                                .ScanWirelessDevices();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "NoBuild",
                            "Connection failed.\n" +
                            "Try using the pairing " +
                            "code if required.",
                            "OK");
                    }
                }
                GUI.backgroundColor = Color.white;

                // Pair button
                if (!string.IsNullOrEmpty(
                        _pairingCode))
                {
                    GUI.backgroundColor =
                        new Color(0.7f, 0.6f, 0.2f);
                    if (GUILayout.Button("Pair",
                            GUILayout.Width(50),
                            GUILayout.Height(22)))
                    {
                        bool paired =
                            AdbUtility.PairDevice(
                                device.IpAddress,
                                device.Port,
                                _pairingCode);
                        if (paired)
                        {
                            // After pairing,
                            // try connecting
                            AdbUtility
                                .ConnectWireless(
                                    device.IpAddress,
                                    device.Port);
                            RefreshDevices();
                            _wirelessDevices =
                                AdbUtility
                                    .ScanWirelessDevices
                                    ();
                        }
                        else
                        {
                            EditorUtility
                                .DisplayDialog(
                                    "NoBuild",
                                    "Pairing failed. " +
                                    "Check the code.",
                                    "OK");
                        }
                    }
                    GUI.backgroundColor =
                        Color.white;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private List<AdbDeviceInfo> GetCachedDevices()
        {
            if (_cachedDevices == null
                || (EditorApplication.timeSinceStartup
                    - _lastDeviceRefreshTime)
                > DeviceRefreshInterval)
            {
                RefreshDevices();
            }
            return _cachedDevices
                ?? (_cachedDevices = new List<AdbDeviceInfo>());
        }

        private void RefreshDevices()
        {
            _cachedDevices = AdbUtility.GetDevices();
            _lastDeviceRefreshTime =
                EditorApplication.timeSinceStartup;
        }

        // ══════════════════════════════════════════════════
        // ── Platform Icon Grid Selector
        // ══════════════════════════════════════════════════

        private void DrawPlatformSelector(SerializedProperty platP)
        {
            BuildTarget currentPlatform =
                (BuildTarget)platP.enumValueIndex;
            GUIContent icon = PlatformIconUtility.GetPlatformIcon(
                currentPlatform);
            string displayName = PlatformIconUtility
                .GetPlatformDisplayName(currentPlatform);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Platform",
                GUILayout.Width(LabelW));

            GUIContent btnContent = new()
            {
                image = icon?.image,
                text = "  " + displayName
            };

            GUIStyle btnStyle = EditorStyles.popup;
            Rect btnRect = GUILayoutUtility.GetRect(
                btnContent, btnStyle,
                GUILayout.MinWidth(160));

            if (GUI.Button(btnRect, btnContent, btnStyle))
            {
                CustomPopupDropdown.Show(
                    btnRect,
                    NoBuildDropdowns.CreatePlatformGridDropdown(
                        currentPlatform,
                        newPlatform =>
                        {
                            platP.enumValueIndex =
                                (int)newPlatform;
                            platP.serializedObject
                                .ApplyModifiedProperties();
                        }
                    )
                );
            }

            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════
        // ── Two-Column Layout Helpers
        // ══════════════════════════════════════════════════

        private static void LblChk(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            prop.boolValue = EditorGUILayout.Toggle(prop.boolValue);
            EditorGUILayout.EndHorizontal();
        }

        private static void LblTxt(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
            EditorGUILayout.EndHorizontal();
        }

        private static string LblTxt(string label, string val)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            string result = EditorGUILayout.TextField(val);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static void LblProp(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            EditorGUILayout.PropertyField(prop, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        private static int LPopup(string label, int val, string[] opts)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            int r = EditorGUILayout.Popup(val, opts);
            EditorGUILayout.EndHorizontal();
            return r;
        }

        private static void LblTxtHint(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
            if (GUILayout.Button("?", GUILayout.Width(22)))
            {
                Rect btnRect =
                    GUILayoutUtility.GetLastRect();
                PlaceholderGuide.Show(btnRect,
                    key =>
                    {
                        prop.stringValue += key;
                        prop.serializedObject.ApplyModifiedProperties();
                    });
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void LblBtnGroup(string label, SerializedProperty prop,
            string[] names, int[] values)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));

            int currentIdx = System.Array.IndexOf(values, prop.enumValueIndex);
            if (currentIdx < 0) currentIdx = 0;

            for (int i = 0; i < names.Length; i++)
            {
                bool isActive = currentIdx == i;
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = isActive
                    ? new Color(0.25f, 0.45f, 0.75f)
                    : new Color(0.35f, 0.35f, 0.35f);

                if (GUILayout.Button(names[i], GUILayout.Height(20)))
                    prop.enumValueIndex = values[i];

                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndHorizontal();
        }

#if UNITY_ANDROID
        private static void LblSymbolFormat(
            string label, SerializedProperty prop)
        {
            const int zipFlag = 1;           // Zip
            const int bundleFlag = 2;         // IncludeInBundle
            const int legacyFlag = 4;         // LegacyExtensions

            int current = prop.intValue;

            // ── Output type dropdown ──
            int outputPart = current & (zipFlag | bundleFlag);
            string[] outputNames = { "Zip", "Bundle", "Zip + Bundle" };
            int[] outputValues = { zipFlag, bundleFlag,
                zipFlag | bundleFlag };

            int outputIdx = System.Array.IndexOf(
                outputValues, outputPart);
            if (outputIdx < 0) outputIdx = 2;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            int newOutputIdx = EditorGUILayout.Popup(
                outputIdx, outputNames);
            EditorGUILayout.EndHorizontal();

            // ── Extensions dropdown ──
            int extPart = current & legacyFlag;
            string[] extNames = { ".so", ".so.dbg" };
            int[] extValues = { 0, legacyFlag };

            int extIdx = extPart == legacyFlag ? 1 : 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Sym Ext",
                GUILayout.Width(LabelW));
            int newExtIdx = EditorGUILayout.Popup(
                extIdx, extNames);
            EditorGUILayout.EndHorizontal();

            // Apply changes
            if (newOutputIdx != outputIdx || newExtIdx != extIdx)
            {
                int newValue = outputValues[newOutputIdx]
                    | extValues[newExtIdx];
                prop.intValue = newValue;
            }
        }
#endif

        // ══════════════════════════════════════════════════
        // ── Helpers
        // ══════════════════════════════════════════════════

        private void Refresh()
        {
            _so?.Dispose();
            _so = new SerializedObject(NoBuildResourceUtility.GetOrCreateSettings());
            _sceneSets = _so.FindProperty("sceneSets");
            _defineSets = _so.FindProperty("scriptDefinitionSets");
            _buildProfiles = _so.FindProperty("buildProfiles");
            _flagDefs = _so.FindProperty("flagDefinitions");
            _activeScene = _so.FindProperty("activeSceneSetIndex");
            _activeDefine = _so.FindProperty("activeScriptDefinitionSetIndex");

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            _selSet = Mathf.Min(_selSet, s.sceneSets.Count - 1);
            _selDef = Mathf.Min(_selDef, s.scriptDefinitionSets.Count - 1);
            _selBuild = Mathf.Min(_selBuild, s.buildProfiles.Count - 1);
            _selFlag = Mathf.Min(_selFlag, s.flagDefinitions.Count - 1);
        }

    }
}
