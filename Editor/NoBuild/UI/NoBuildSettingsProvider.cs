// ═══════════════════════════════════════════════════════════
// ── NoBuildSettingsProvider ───────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Com.Scheherazade.Common.Editor;
using Com.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    public sealed class NoBuildSettingsProvider : SettingsProvider
    {
        public const string Path = "Project/Tools/No Build";
        private const float LabelW = EditorGuiLayout.DefaultLabelWidth;
        private static readonly string[] Tabs =
            { "Scene Sets", "Defines", "Build Profiles", "Devices", "Flags" };
        private static readonly string[] StateFilters =
            { "All", "Matches Editor", "Differs" };
        private static readonly string[] PlatformFilters =
            { "All", "Android", "Other Platforms" };
        private static readonly string[] FlagFilters =
            { "All", "Template", "Custom" };

        private SerializedObject _so;
        private SerializedProperty _sceneSets, _defineSets, _buildProfiles, _flagDefs;
        private SerializedProperty _toolbarLabelTemplate;
        private int _tab;
        private int _selSet = -1, _selDef = -1, _selBuild = -1, _selFlag = -1;
        private Vector2 _pvScroll;
        private string _searchQuery = string.Empty;
        private int _filterIndex;
        private int _expandedEntryIndex = -1;
        private bool _showAppearance;
        private bool _focusName;
        private bool _revealEntry;
        private float _listStartY;
        private string SessionKey => "NoBuild.UI." + Application.dataPath;

        [Serializable]
        private sealed class BrowserState
        {
            public int tab, scene = -1, define = -1, build = -1, flag = -1, expanded = -1, filter;
            public string search = "";
            public Vector2 scroll;
            public bool appearance;
        }

        // ── Device Tab State ───────────────────────
        private string _installApkPath
        {
            get => SessionState.GetString(SessionKey + ".ApkPath", "");
            set => SessionState.SetString(SessionKey + ".ApkPath", value);
        }
        private string _installAabPath
        {
            get => SessionState.GetString(SessionKey + ".AabPath", "");
            set => SessionState.SetString(SessionKey + ".AabPath", value);
        }
        private string ApkPackage
        {
            get => SessionState.GetString(SessionKey + ".ApkPackage", "");
            set => SessionState.SetString(SessionKey + ".ApkPackage", value);
        }
        private string AabPackage
        {
            get => SessionState.GetString(SessionKey + ".AabPackage", "");
            set => SessionState.SetString(SessionKey + ".AabPackage", value);
        }
        private string LaunchPackage
        {
            get => SessionState.GetString(SessionKey + ".LaunchPackage", AdbUtility.GetPackageName());
            set => SessionState.SetString(SessionKey + ".LaunchPackage", value);
        }
        private bool _showToolPaths;
        private List<WirelessDeviceInfo> _wirelessDevices;
        private string _pairingCode = "";

        private NoBuildSettingsProvider(string p, SettingsScope sc,
            IEnumerable<string> kw = null) : base(p, sc, kw)
        {
            activateHandler = (_, _2) =>
            {
                RestoreBrowserState();
                Refresh();
                AssemblyReloadEvents.beforeAssemblyReload += SaveBrowserState;
                NoBuildToolbarState.RepaintRequested += Repaint;
            };
            deactivateHandler = () =>
            {
                SaveBrowserState();
                AssemblyReloadEvents.beforeAssemblyReload -= SaveBrowserState;
                NoBuildToolbarState.RepaintRequested -= Repaint;
                _so?.Dispose();
                _so = null;
            };
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new NoBuildSettingsProvider(Path,
            SettingsScope.Project,
            new[] { "nobuild", "scene", "switch", "define", "build", "profile", "combo" });

        [MenuItem("Dev Menu/Tools/NoBuild")]
        private static void OpenSettings() => SettingsService.OpenProjectSettings(Path);

        private void SaveBrowserState()
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new BrowserState
            {
                tab = _tab, scene = _selSet, define = _selDef, build = _selBuild, flag = _selFlag,
                expanded = _expandedEntryIndex, filter = _filterIndex, search = _searchQuery,
                scroll = _pvScroll, appearance = _showAppearance
            }));
        }

        private void RestoreBrowserState()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return;
            BrowserState state = JsonUtility.FromJson<BrowserState>(json);
            _tab = Mathf.Clamp(state.tab, 0, Tabs.Length - 1);
            _selSet = state.scene; _selDef = state.define; _selBuild = state.build; _selFlag = state.flag;
            _expandedEntryIndex = state.expanded; _filterIndex = Mathf.Clamp(state.filter, 0, 2);
            _searchQuery = state.search ?? ""; _pvScroll = state.scroll; _showAppearance = state.appearance;
        }

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
            _showAppearance = EditorGUILayout.Foldout(_showAppearance, "Appearance", true);
            if (_showAppearance) DrawToolbarLabelSettings(s);
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

            if (_so.ApplyModifiedProperties())
            {
                NoBuildToolbarState.RequestRepaint();
            }
            if (GUI.changed) SaveBrowserState();
        }

        private static void DrawProviderHeader(NoBuildSettings settings)
        {
            SceneSet scene = settings.ActiveSceneSet;
            ScriptDefinitionSet defines = settings.ActiveScriptDefinitionSet;
            string sceneSet = scene == null ? "Custom" : scene.setName
                + (NoBuildEditorState.ScenesMatch(scene) ? " (open)" : " (shortcut context; scenes differ)");
            string defineSet = defines == null ? "Custom" : defines.setName
                + (NoBuildEditorState.DefinesMatch(defines) ? " (applied)" : " (changes not applied)");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("No Build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Scenes: {sceneSet}  •  Defines [{EditorUserBuildSettings.selectedBuildTargetGroup}]: {defineSet}",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            NoBuildBuildUI.DrawLastResult();
        }

        private void DrawToolbarLabelSettings(
            NoBuildSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Toolbar Label",
                GUILayout.Width(LabelW));

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                _toolbarLabelTemplate,
                GUIContent.none);
            bool changed = EditorGUI.EndChangeCheck();

            if (GUILayout.Button("Reset", GUILayout.Width(48)))
            {
                _toolbarLabelTemplate.stringValue = "NoBuild";
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "{scene-set}  {define-set}  {platform}  "
                + "{scene-count}  {define-count}  "
                + "{project-name}  {scene-status}  {define-status}",
                EditorStyles.miniLabel);

            if (changed)
            {
                _so.ApplyModifiedProperties();
                NoBuildToolbarState.RequestRepaint();
            }

            EditorGUILayout.LabelField(
                "Preview: "
                + NoBuildToolbarLabelResolver.Resolve(settings),
                EditorStyles.miniBoldLabel);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
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
                GUIUtility.ExitGUI();
            }

            EditorGUI.BeginDisabledGroup(!HasSelection() || !MatchesCollectionFilter(GetSelectedEntryName(),
                GetSelectedEntryIndex(), GetActiveEntryIndex()));
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RunBlue;
            if (GUILayout.Button(
                    new GUIContent("Duplicate Selected", "Duplicate " + GetSelectedEntryName()),
                    GUILayout.Height(22)
                ))
            {
                CloneSelected();
                GUI.backgroundColor = previousBackground;
                EditorGUI.EndDisabledGroup();
                GUIUtility.ExitGUI();
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
                EditorStyles.boldLabel
            );
            _listStartY = GUILayoutUtility.GetLastRect().yMax + 2;
            GUILayout.Space(2);
            bool hasVisibleEntries = false;

            switch (_tab)
            {
                case 0:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.sceneSets.Select(x => x.setName).ToList(),
                        s.SelectedSceneSetIndex,
                        ref _selSet
                    );
                    break;
                case 1:
                    hasVisibleEntries = DrawCollectionEntries(
                        s.scriptDefinitionSets.Select(x => x.setName).ToList(),
                        s.SelectedDefineSetIndex,
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
                    GetCurrentCollectionCount() == 0
                        ? $"No {GetCollectionLabel().ToLowerInvariant()} created."
                        : "No matching entries.",
                    GetCurrentCollectionCount() == 0
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

        private int GetSelectedEntryIndex() => _tab switch
        {
            0 => _selSet, 1 => _selDef, 2 => _selBuild, 4 => _selFlag, _ => -1
        };

        private int GetActiveEntryIndex()
        {
            var settings = (NoBuildSettings)_so.targetObject;
            return _tab switch { 0 => settings.SelectedSceneSetIndex, 1 => settings.SelectedDefineSetIndex, _ => -1 };
        }

        private string GetSelectedEntryName()
        {
            if (!HasSelection()) return "";
            var settings = (NoBuildSettings)_so.targetObject;
            return _tab switch
            {
                0 => settings.sceneSets[_selSet].setName,
                1 => settings.scriptDefinitionSets[_selDef].setName,
                2 => settings.buildProfiles[_selBuild].profileName,
                4 => FlagDisplayName(settings.flagDefinitions[_selFlag], _selFlag, settings),
                _ => ""
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
                var settings = (NoBuildSettings)_so.targetObject;
                bool isAct = _tab == 0 ? NoBuildEditorState.ScenesMatch(settings.sceneSets[i])
                    : _tab == 1 && NoBuildEditorState.DefinesMatch(settings.scriptDefinitionSets[i]);
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
                SaveBrowserState();
            }

            if (GUILayout.Button(
                    isExpanded ? "Collapse" : "Expand",
                    GUILayout.Width(70)
                ))
            {
                selectedIndex = entryIndex;
                _expandedEntryIndex = isExpanded ? -1 : entryIndex;
                SaveBrowserState();
            }

            EditorGUILayout.EndHorizontal();

            if (isExpanded)
            {
                GUILayout.Space(4);
                DrawPreview();
            }

            EditorGUILayout.EndVertical();
            Rect cardRect = GUILayoutUtility.GetLastRect();
            if (_revealEntry && selectedIndex == entryIndex && Event.current.type == EventType.Repaint)
            {
                _pvScroll.y = Mathf.Max(0, cardRect.y - _listStartY);
                _revealEntry = false;
                SaveBrowserState();
                Repaint();
            }
            if (isActive)
            {
                DrawCardOutline(cardRect, EditorGuiColors.ActiveGreen);
            }
            else if (isSelected)
            {
                DrawCardOutline(cardRect, EditorGuiColors.RunBlue);
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
            var settings = (NoBuildSettings)_so.targetObject;
            bool matches = _tab == 0 ? NoBuildEditorState.ScenesMatch(settings.sceneSets[entryIndex])
                : NoBuildEditorState.DefinesMatch(settings.scriptDefinitionSets[entryIndex]);
            return _filterIndex switch
            {
                1 => matches,
                2 => !matches,
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
            NoBuildSettings settings = (NoBuildSettings)_so.targetObject;
            _so.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Create NoBuild Entry");
            switch (_tab)
            {
                case 0:
                    settings.sceneSets.Add(new SceneSet { setName = "Scene Set " + (settings.sceneSets.Count + 1) });
                    _selSet = settings.sceneSets.Count - 1;
                    break;
                case 1:
                    settings.scriptDefinitionSets.Add(new ScriptDefinitionSet { setName = "Define Set " + (settings.scriptDefinitionSets.Count + 1) });
                    _selDef = settings.scriptDefinitionSets.Count - 1;
                    break;
                case 2:
                    settings.buildProfiles.Add(new BuildProfile { profileName = "Build Profile " + (settings.buildProfiles.Count + 1) });
                    _selBuild = settings.buildProfiles.Count - 1;
                    break;
                case 4:
                    settings.flagDefinitions.Add(new FlagDefinition
                    {
                        name = "Flag " + (settings.flagDefinitions.Count + 1),
                        id = GetUniqueFlagId(settings, "flag")
                    });
                    _selFlag = settings.flagDefinitions.Count - 1;
                    break;
            }
            EditorUtility.SetDirty(settings);
            _so.Update();
            RevealSelection();
        }

        private void RevealSelection()
        {
            _searchQuery = "";
            _filterIndex = 0;
            _expandedEntryIndex = _tab switch { 0 => _selSet, 1 => _selDef, 2 => _selBuild, 4 => _selFlag, _ => -1 };
            _pvScroll.y = 0;
            _focusName = true;
            _revealEntry = true;
            SaveBrowserState();
            Repaint();
        }

        internal static string GetUniqueFlagId(NoBuildSettings settings, string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId)) baseId = "flag";
            string candidate = baseId;
            int suffix = 2;
            while (settings.flagDefinitions.Any(flag => string.Equals(flag?.id, candidate, StringComparison.OrdinalIgnoreCase)))
                candidate = baseId + "-" + suffix++;
            return candidate;
        }

        private void CloneSelected()
        {
            NoBuildSettings settings =
                (NoBuildSettings)_so.targetObject;
            if (settings == null) return;

            _so.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Duplicate NoBuild Entry");

            switch (_tab)
            {
                case 0: CloneSceneSet(settings); break;
                case 1: CloneDefineSet(settings); break;
                case 2: CloneBuildProfile(settings); break;
                case 4: CloneFlagDefinition(settings); break;
            }

            _so.Update();
            EditorUtility.SetDirty(settings);
            RevealSelection();
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

            int insertionIndex = _selSet + 1;
            RemapSceneSetReferencesAfterInsertion(
                settings,
                insertionIndex);
            settings.sceneSets.Insert(insertionIndex, clone);
            _selSet = insertionIndex;
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

            int insertionIndex = _selDef + 1;
            RemapDefineSetReferencesAfterInsertion(
                settings,
                insertionIndex);
            settings.scriptDefinitionSets.Insert(
                insertionIndex,
                clone);
            _selDef = insertionIndex;
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
        }

        private void CloneFlagDefinition(NoBuildSettings settings)
        {
            if (_selFlag < 0
                || _selFlag >= settings.flagDefinitions.Count)
                return;

            var source = settings.flagDefinitions[_selFlag];
            var clone = new FlagDefinition
            {
                id = GetUniqueFlagId(settings, source.id + "-copy"),
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
            var buildOrderP = sp.FindPropertyRelative(
                "buildOrderOverride");
            var combosP = sp.FindPropertyRelative("combinations");
            bool active = NoBuildEditorState.ScenesMatch(s.sceneSets[_selSet]);

            EditorGuiLayout.DrawSectionHeader(
                "Scene Set Configuration",
                active ? "These scenes are open. The first scene is active." : "Open this set to replace the current scene setup."
            );
            EditorGUILayout.BeginHorizontal();
            Color oldC = GUI.contentColor;
            if (active) GUI.contentColor = EditorGuiColors.ActiveGreen;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            if (!active || s.SelectedSceneSetIndex != _selSet)
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.BuildGreen;
                if (GUILayout.Button(active ? "Use for Shortcuts" : "Open Scene Set", GUILayout.Width(120)))
                {
                    _so.ApplyModifiedProperties();
                    if (active) s.SelectedSceneSetIndex = _selSet;
                    else NoBuildEditorState.OpenSet(s, _selSet);
                }

                GUI.backgroundColor = previousBackground;
            }

            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                {
                    int removedIndex = _selSet;
                    Undo.RecordObject(s, "Delete NoBuild Scene Set");
                    _sceneSets.DeleteArrayElementAtIndex(removedIndex);
                    _so.ApplyModifiedProperties();
                    RemapSceneSetReferencesAfterRemoval(
                        s,
                        removedIndex);
                    EditorUtility.SetDirty(s);
                    NoBuildToolbarState.RequestRepaint();
                    _selSet = Mathf.Min(_selSet, _sceneSets.arraySize - 1);
                    _expandedEntryIndex = _selSet;
                    SaveBrowserState();
                    GUI.backgroundColor = previousDeleteBackground;
                    GUIUtility.ExitGUI();
                    return;
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

            if (GUILayout.Button("Capture Open Scenes", GUILayout.Height(22)))
            {
                Scene activeScene = SceneManager.GetActiveScene();
                var openScenes = new List<UnityEngine.SceneManagement.Scene>();
                for (int i = 0; i < SceneManager.sceneCount; i++) openScenes.Add(SceneManager.GetSceneAt(i));
                if (openScenes.Any(scene => scene.isLoaded && string.IsNullOrEmpty(scene.path)))
                {
                    EditorUtility.DisplayDialog("Save Scenes First", "Save untitled scenes before capturing them. The configured set has been kept.", "OK");
                    GUIUtility.ExitGUI();
                    return;
                }
                scenesP.ClearArray();
                foreach (Scene scene in openScenes.OrderByDescending(scene => scene == activeScene))
                {
                    if (string.IsNullOrEmpty(scene.path) || !scene.isLoaded) continue;
                    int index = scenesP.arraySize++;
                    SerializedProperty slot = scenesP.GetArrayElementAtIndex(index);
                    slot.FindPropertyRelative("scene").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                    slot.FindPropertyRelative("enabled").boolValue = true;
                    slot.FindPropertyRelative("displayName").stringValue = "";
                }
                _so.ApplyModifiedProperties();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("+ Add Scene", GUILayout.Height(22),
                    GUILayout.Width(100)))
            {
                scenesP.arraySize++;
                scenesP.GetArrayElementAtIndex(scenesP.arraySize - 1)
                    .FindPropertyRelative("enabled").boolValue = true;
                scenesP.GetArrayElementAtIndex(scenesP.arraySize - 1)
                    .FindPropertyRelative("scene").objectReferenceValue = null;
                scenesP.GetArrayElementAtIndex(scenesP.arraySize - 1)
                    .FindPropertyRelative("displayName").stringValue = "";
            }
            GUILayout.Space(8);

            EditorGUILayout.LabelField(
                "Build Order Override",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional. When empty, the scene order above is "
                + "used for builds.",
                MessageType.None);
            DrawSceneReorderableList(buildOrderP);
            if (GUILayout.Button("Copy Scene Order to Build Override"))
            {
                _so.ApplyModifiedProperties();
                Undo.RecordObject(s, "Copy NoBuild Build Order");
                s.sceneSets[_selSet].buildOrderOverride = s.sceneSets[_selSet].scenes.ConvertAll(slot => slot.Clone());
                EditorUtility.SetDirty(s);
                _so.Update();
            }
            if (GUILayout.Button(
                    "+ Add Build Scene",
                    GUILayout.Height(22),
                    GUILayout.Width(125)))
            {
                buildOrderP.arraySize++;
                buildOrderP.GetArrayElementAtIndex(
                        buildOrderP.arraySize - 1)
                    .FindPropertyRelative("enabled")
                    .boolValue = true;
                buildOrderP.GetArrayElementAtIndex(buildOrderP.arraySize - 1)
                    .FindPropertyRelative("scene").objectReferenceValue = null;
                buildOrderP.GetArrayElementAtIndex(buildOrderP.arraySize - 1)
                    .FindPropertyRelative("displayName").stringValue = "";
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
                EditorStyles.boldLabel
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
                combination.FindPropertyRelative("sceneReferences").ClearArray();
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
                $"Slot {index + 1} · {ShortcutManager.GetBinding(index)}",
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

            bool hasStartup = false;
            for (int i = 0; i < scenesP.arraySize; i++)
            {
                var slot = scenesP.GetArrayElementAtIndex(i);
                var en = slot.FindPropertyRelative("enabled");
                var sc = slot.FindPropertyRelative("scene");

                EditorGUILayout.BeginHorizontal();

                bool startup = !hasStartup && en.boolValue && sc.objectReferenceValue != null;
                if (startup) hasStartup = true;
                GUILayout.Label(startup ? "Startup" : (i + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(48));
                bool previousEnabled = GUI.enabled;
                Color previousBackground = GUI.backgroundColor;

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
                GUI.enabled = previousEnabled && i > 0;
                if (GUILayout.Button("\u25B2",
                        GUILayout.Width(24), GUILayout.Height(18)))
                {
                    scenesP.MoveArrayElement(i, i - 1);
                    _so.ApplyModifiedProperties();
                }
                GUI.enabled = previousEnabled;

                // Down button
                GUI.enabled = previousEnabled && i < scenesP.arraySize - 1;
                if (GUILayout.Button("\u25BC",
                        GUILayout.Width(24), GUILayout.Height(18)))
                {
                    scenesP.MoveArrayElement(i, i + 1);
                    _so.ApplyModifiedProperties();
                }
                GUI.enabled = previousEnabled;

                // Remove button
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("\u00D7",
                        GUILayout.Width(22), GUILayout.Height(18)))
                {
                    scenesP.DeleteArrayElementAtIndex(i);
                    _so.ApplyModifiedProperties();
                    GUI.backgroundColor = previousBackground;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = previousBackground;

                EditorGUILayout.EndHorizontal();
                if (en.boolValue && sc.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("Assign a scene or disable this slot.", MessageType.Warning);
            }
        }

        private void DrawComboSceneReferences(
            SerializedProperty sceneRefsP,
            SerializedProperty parentScenesP)
        {
            // Build display names from parent scene set
            List<string> sceneNames = new();
            List<SceneAsset> sceneAssets = new();
            for (int i = 0; i < parentScenesP.arraySize; i++)
            {
                var slot = parentScenesP.GetArrayElementAtIndex(i);
                var dName = slot.FindPropertyRelative("displayName");
                var scObj = slot.FindPropertyRelative("scene");
                SceneAsset scene = scObj.objectReferenceValue
                    as SceneAsset;
                string name = dName.stringValue;
                if (string.IsNullOrEmpty(name) && scene != null)
                {
                    name = scene.name;
                }

                if (string.IsNullOrEmpty(name))
                {
                    name = $"Scene {i}";
                }

                sceneNames.Add(name);
                sceneAssets.Add(scene);
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
                var sceneP = refP.FindPropertyRelative("scene");
                var indexP = refP.FindPropertyRelative("sceneIndex");
                var enP = refP.FindPropertyRelative("enabled");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"Scene {j + 1}",
                    GUILayout.Width(LabelW)
                );

                enP.boolValue = EditorGUILayout.Toggle(
                    enP.boolValue, GUILayout.Width(16));

                SceneAsset referencedScene =
                    sceneP.objectReferenceValue as SceneAsset;
                int selectedIndex = referencedScene != null
                    ? sceneAssets.IndexOf(referencedScene)
                    : Mathf.Clamp(
                        indexP.intValue,
                        0,
                        sceneNames.Count - 1);

                if (referencedScene != null && selectedIndex < 0)
                {
                    EditorGUILayout.LabelField(
                        $"Missing from set: {referencedScene.name}",
                        EditorStyles.miniLabel);
                }
                else
                {
                    int newIndex = EditorGUILayout.Popup(
                        selectedIndex,
                        nameArray);
                    indexP.intValue = newIndex;
                    sceneP.objectReferenceValue =
                        sceneAssets[newIndex];
                }

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
                newRef.FindPropertyRelative("scene")
                    .objectReferenceValue = sceneAssets[0];
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
            bool active = NoBuildEditorState.DefinesMatch(s.scriptDefinitionSets[_selDef]);

            EditorGuiLayout.DrawSectionHeader(
                "Script Define Set Configuration",
                $"{EditorUserBuildSettings.selectedBuildTargetGroup} · "
                + (active ? "Configured symbols match Player Settings." : "Changes are not applied.")
            );
            EditorGUILayout.BeginHorizontal();
            Color oldC = GUI.contentColor;
            if (active) GUI.contentColor = EditorGuiColors.ActiveGreen;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope((active && s.SelectedDefineSetIndex == _selDef)
                || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.BuildGreen;
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    _so.ApplyModifiedProperties();
                    NoBuildEditorState.ApplyDefines(s, _selDef);
                }

                GUI.backgroundColor = previousBackground;
            }
            Color previousDeleteBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.RemoveRed;
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                {
                    int removedIndex = _selDef;
                    Undo.RecordObject(s, "Delete NoBuild Define Set");
                    _defineSets.DeleteArrayElementAtIndex(removedIndex);
                    _so.ApplyModifiedProperties();
                    RemapDefineSetReferencesAfterRemoval(
                        s,
                        removedIndex);
                    EditorUtility.SetDirty(s);
                    NoBuildToolbarState.RequestRepaint();
                    _selDef = Mathf.Min(_selDef, _defineSets.arraySize - 1);
                    _expandedEntryIndex = _selDef;
                    SaveBrowserState();
                    GUI.backgroundColor = previousDeleteBackground;
                    GUIUtility.ExitGUI();
                    return;
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
                {
                    Undo.RecordObject(s, "Remove NoBuild Symbol");
                    slotsP.DeleteArrayElementAtIndex(i);
                    _so.ApplyModifiedProperties();
                    RemapDefineSlotReferencesAfterRemoval(
                        s,
                        _selDef,
                        i);
                    EditorUtility.SetDirty(s);
                    _so.Update();
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                string symbolError = ScriptDefinitionSwitcher.ValidateSymbol(def.stringValue);
                if (symbolError != null) EditorGUILayout.HelpBox(symbolError, MessageType.Error);
            }

            if (GUILayout.Button("+ Add Symbol", GUILayout.Height(22)))
            {
                int index = slotsP.arraySize++;
                var slot = slotsP.GetArrayElementAtIndex(index);
                slot.FindPropertyRelative("defineSymbol").stringValue = "";
                slot.FindPropertyRelative("enabled").boolValue = true;
            }
            EditorGUILayout.HelpBox("Applying merges this set: enabled symbols are added, disabled symbols are removed, and other symbols are preserved.", MessageType.None);
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
            BuildPreflightResult preflight = BuildPreflight.Evaluate(profile, s);
            string readiness = preflight.Summary;

            EditorGuiLayout.DrawSectionHeader(
                "Build Profile Configuration",
                $"{profile.buildConfiguration.platform} • {readiness}"
            );

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            Color previousBuildBackground = GUI.backgroundColor;
            GUI.backgroundColor = EditorGuiColors.BuildGreen;
            using (new EditorGUI.DisabledScope(!preflight.CanBuild || NoBuildDeviceOperations.IsBusy))
            if (GUILayout.Button(
                    PlatformIconUtility.BuildActionIcon,
                    GUILayout.Width(78)
                ))
            {
                _so.ApplyModifiedProperties();
                NoBuildBuildUI.ShowConfirmation(GUILayoutUtility.GetLastRect(), s.buildProfiles[_selBuild], s);
            }
            GUI.backgroundColor = previousBuildBackground;
            if (s.buildProfiles[_selBuild].buildConfiguration.platform == BuildTarget.Android)
            {
                Color previousRunBackground = GUI.backgroundColor;
                GUI.backgroundColor = EditorGuiColors.RunBlue;
                using (new EditorGUI.DisabledScope(!preflight.CanBuild || profile.buildConfiguration.androidExportProject || NoBuildDeviceOperations.IsBusy))
                if (GUILayout.Button(
                        PlatformIconUtility.BuildAndRunActionIcon,
                        GUILayout.Width(112)
                    ))
                {
                    _so.ApplyModifiedProperties();
                    NoBuildBuildUI.ShowConfirmation(GUILayoutUtility.GetLastRect(), s.buildProfiles[_selBuild], s);
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
                    _selBuild = Mathf.Min(_selBuild, _buildProfiles.arraySize - 1);
                    _expandedEntryIndex = _selBuild;
                    SaveBrowserState();
                    GUI.backgroundColor = previousDeleteBackground;
                    GUIUtility.ExitGUI();
                    return;
                }
            }
            GUI.backgroundColor = previousDeleteBackground;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            NoBuildBuildUI.DrawReadiness(profile, s, preflight);

            nameP.stringValue = LblTxt("Name", nameP.stringValue);

            string[] ssN = s.sceneSets.Select(x => x.setName).ToArray();
            DrawReferencePopup("Scene Set", ssP, ssN, 0);

            string[] dsN = s.scriptDefinitionSets.Select(x => x.setName).ToArray();
            DrawReferencePopup("Define Set", dsP, dsN, 1);
            GUILayout.Space(6);

            // Build folder with [?]
            LblTxtHint("Build Folder", folderP);
            string fPrev = BuildNameResolver.Resolve(folderP.stringValue,
                _selBuild < s.buildProfiles.Count ? s.buildProfiles[_selBuild] : null, s);
            EditorGUILayout.LabelField("", "\u2192 " + fPrev, EditorStyles.miniLabel);

            // Build name with [?]
            LblTxtHint("Build Name", nameTplP);
            string nPrev = BuildNameResolver.ResolveFileName(nameTplP.stringValue,
                _selBuild < s.buildProfiles.Count ? s.buildProfiles[_selBuild] : null, s);
            EditorGUILayout.LabelField("", "\u2192 " + nPrev, EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel("Full: " + preflight.OutputPath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(32));
            GUILayout.Space(8);

            // ── Build Configuration (two-column, one per line) ──
            var platP = cfgP.FindPropertyRelative("platform");
            DrawPlatformSelector(platP);
            BuildTarget selPlat = (BuildTarget)platP.intValue;
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
            EditorGUILayout.LabelField("Blank overrides use Player Settings.", EditorStyles.miniLabel);

            // Windows
            if (selPlat == BuildTarget.StandaloneWindows || selPlat == BuildTarget.StandaloneWindows64)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("Windows", EditorStyles.boldLabel);
                LblChk(
                    "Create VS Solution",
                    cfgP.FindPropertyRelative(
                        "windowsCreateVSProject"));
                EditorGUILayout.HelpBox(
                    "Unity 6 no longer exposes the legacy Copy PDB "
                    + "and Copy References build options. Debug "
                    + "symbols follow the selected build mode.",
                    MessageType.None);
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
                    _expandedEntryIndex = _selFlag;
                    SaveBrowserState();
                    GUI.backgroundColor = previousDeleteBackground;
                    GUIUtility.ExitGUI();
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
            if (string.IsNullOrWhiteSpace(idP.stringValue)
                || s.flagDefinitions.Where((flag, index) => index != _selFlag)
                    .Any(flag => string.Equals(flag.id, idP.stringValue, StringComparison.OrdinalIgnoreCase)))
                EditorGUILayout.HelpBox("Use a non-empty, unique flag ID for named tokens.", MessageType.Error);
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
            _pvScroll = EditorGUILayout.BeginScrollView(_pvScroll);
            EditorGuiLayout.DrawHeaderBox(
                "Devices",
                "Manage Android Debug Bridge connections and install builds."
            );
            string adbPath = AdbUtility.AdbPath;
            AdbDeviceCache.EnsureFresh();

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ADB:", GUILayout.Width(40));
            EditorGUILayout.LabelField(
                AdbDeviceCache.IsRefreshing ? "Checking…" : !string.IsNullOrEmpty(AdbDeviceCache.Error) ? "Unavailable" : "Ready",
                EditorStyles.boldLabel, GUILayout.Width(85));
            EditorGUILayout.LabelField(new GUIContent(adbPath ?? "(unknown)", adbPath),
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(AdbDeviceCache.Error))
                EditorGUILayout.HelpBox(AdbDeviceCache.Error + "\nCheck the tool path below or Unity Preferences → External Tools.", MessageType.Error);
            _showToolPaths = EditorGUILayout.Foldout(_showToolPaths, "Tool Paths (local to this machine)", true);
            if (_showToolPaths)
            {
                string adbOverride = DrawToolPath("ADB", AdbUtility.CustomAdbPath);
                if (adbOverride != AdbUtility.CustomAdbPath) AdbUtility.CustomAdbPath = adbOverride;
                AabUtility.CustomJavaPath = DrawToolPath("Java", AabUtility.CustomJavaPath);
                AabUtility.CustomBundletoolPath = DrawToolPath("bundletool.jar", AabUtility.CustomBundletoolPath, "jar");
                EditorGUILayout.HelpBox("Blank paths use automatic discovery. AAB operations download bundletool into Library/NoBuild if it is missing.", MessageType.None);
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Launch Package", GUILayout.Width(LabelW));
            LaunchPackage = EditorGUILayout.TextField(LaunchPackage);
            if (GUILayout.Button("Use Project", GUILayout.Width(90))) LaunchPackage = AdbUtility.GetPackageName();
            EditorGUILayout.EndHorizontal();
            if (!AdbUtility.IsValidPackageName(LaunchPackage))
                EditorGUILayout.HelpBox("Use a valid Android package, such as com.company.game, to enable Launch.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);

            if (NoBuildDeviceOperations.IsBusy)
            {
                EditorGUILayout.HelpBox(NoBuildDeviceOperations.Status, MessageType.Info);
                if (GUILayout.Button("Cancel Device Operation")) NoBuildDeviceOperations.Cancel();
            }
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(AdbDeviceCache.IsRefreshing))
            if (GUILayout.Button("\u21BB Refresh Devices",
                    GUILayout.Height(26)))
            {
                AdbUtility.RefreshAvailability();
                RefreshDevices();
            }
            using (new EditorGUI.DisabledScope(NoBuildDeviceOperations.IsBusy))
            {
                if (GUILayout.Button("Select APK…", GUILayout.Height(26))) SelectArtifact(false);
                if (GUILayout.Button("Select AAB…", GUILayout.Height(26))) SelectArtifact(true);
            }
            EditorGUILayout.EndHorizontal();
            DrawSelectedArtifact(false);
            DrawSelectedArtifact(true);
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);

            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Connected Devices", EditorStyles.boldLabel);
            if (AdbDeviceCache.HasChecked)
                EditorGUILayout.LabelField("Last checked " + Math.Max(0, (int)(EditorApplication.timeSinceStartup - AdbDeviceCache.LastRefresh)) + "s ago", EditorStyles.miniLabel);
            var devices = GetCachedDevices();
            if (devices.Count == 0)
            {
                EditorGUILayout.HelpBox(AdbDeviceCache.IsRefreshing ? "Refreshing devices…"
                    : "No devices connected. Connect via USB and enable USB debugging, or start an emulator.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    DrawDeviceRow(devices[i], LaunchPackage, i);
                }
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
            EditorGUILayout.LabelField("Wireless ADB", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(NoBuildDeviceOperations.IsBusy))
                if (GUILayout.Button("Scan Network", GUILayout.Height(26))) ScanWirelessDevices();
            _pairingCode = EditorGUILayout.TextField("Pair Code", _pairingCode, GUILayout.MinWidth(160));
            EditorGUILayout.EndHorizontal();
            if (_wirelessDevices == null || _wirelessDevices.Count == 0)
                EditorGUILayout.LabelField("Enable Wireless debugging on the device, then scan. Pairing uses the six-digit device code.", EditorStyles.wordWrappedMiniLabel);
            else
            {
                for (int i = 0; i < _wirelessDevices.Count; i++) DrawWirelessRow(_wirelessDevices[i], i);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
            string lastSerial = EditorPrefs.GetString(NoBuildDeviceOperations.RememberedDeviceKey, "");
            if (!string.IsNullOrEmpty(lastSerial))
            {
                EditorGUILayout.BeginVertical(EditorGuiStyles.Card);
                EditorGUILayout.LabelField("Remembered Device", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(lastSerial, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Forget", GUILayout.Width(65))) EditorPrefs.DeleteKey(NoBuildDeviceOperations.RememberedDeviceKey);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private static string DrawToolPath(string label, string value, string extension = "")
        {
            EditorGUILayout.BeginHorizontal();
            string path = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("Browse…", GUILayout.Width(65)))
            {
                string selected = EditorUtility.OpenFilePanel("Select " + label, "", extension);
                if (!string.IsNullOrEmpty(selected)) path = selected;
            }
            EditorGUILayout.EndHorizontal();
            return path;
        }

        private void SelectArtifact(bool aab)
        {
            string selected = EditorUtility.OpenFilePanel("Select " + (aab ? "AAB" : "APK"), "", aab ? "aab" : "apk");
            if (string.IsNullOrEmpty(selected)) return;
            if (aab) { _installAabPath = selected; AabPackage = ""; }
            else { _installApkPath = selected; ApkPackage = ""; }
            InspectArtifact(selected, aab);
        }

        private void InspectArtifact(string path, bool aab)
        {
            NoBuildDeviceOperations.InspectArtifact(path, aab, package =>
            {
                if (aab) AabPackage = package; else ApkPackage = package;
                LaunchPackage = package;
            });
        }

        private void DrawSelectedArtifact(bool aab)
        {
            string path = aab ? _installAabPath : _installApkPath;
            if (string.IsNullOrEmpty(path)) return;
            string package = aab ? AabPackage : ApkPackage;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent((aab ? "AAB: " : "APK: ") + System.IO.Path.GetFileName(path), path), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(NoBuildDeviceOperations.IsBusy))
            {
                if (string.IsNullOrEmpty(package) && GUILayout.Button("Read Package", GUILayout.Width(95))) InspectArtifact(path, aab);
                if (GUILayout.Button("Clear", GUILayout.Width(48)))
                {
                    if (aab) { _installAabPath = ""; AabPackage = ""; }
                    else { _installApkPath = ""; ApkPackage = ""; }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(package) ? "Package not verified; installation is disabled." : package, EditorStyles.miniLabel);
        }

        private void ScanWirelessDevices()
        {
            NoBuildDeviceOperations.Start("Scanning wireless devices…", async token =>
            {
                _wirelessDevices = await AdbUtility.ScanWirelessDevicesAsync(token);
                NoBuildEditorState.SetResult($"Discovered {_wirelessDevices.Count} wireless device(s).");
            });
        }

        private void DrawDeviceRow(
            AdbDeviceInfo device, string packageName, int index)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool isOnline = device.State == "device";
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(device.Model, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(device.Serial, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(device.State, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!isOnline || !AdbUtility.IsValidPackageName(packageName) || NoBuildDeviceOperations.IsBusy))
            {
                if (GUILayout.Button("Launch", GUILayout.Width(65), GUILayout.Height(24)))
                    NoBuildDeviceOperations.Launch(device.Serial, packageName);
            }
            if (!string.IsNullOrEmpty(_installApkPath))
            {
                using (new EditorGUI.DisabledScope(!isOnline || string.IsNullOrEmpty(ApkPackage) || NoBuildDeviceOperations.IsBusy))
                {
                    if (GUILayout.Button("Install APK", GUILayout.Width(80), GUILayout.Height(24)))
                    {
                        string file = System.IO.Path.GetFileName(_installApkPath);
                        if (EditorUtility.DisplayDialog("Install APK", $"Install {file} to {device.DisplayName}?\n\nPackage: {ApkPackage}", "Install", "Cancel"))
                            NoBuildDeviceOperations.Install(_installApkPath, ApkPackage, device.Serial, device.DisplayName, false);
                    }
                }
            }
            if (!string.IsNullOrEmpty(_installAabPath))
            {
                using (new EditorGUI.DisabledScope(!isOnline || string.IsNullOrEmpty(AabPackage) || NoBuildDeviceOperations.IsBusy))
                {
                    if (GUILayout.Button("Install AAB", GUILayout.Width(80), GUILayout.Height(24)))
                        InstallAabToDevice(device);
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!isOnline)
                EditorGUILayout.HelpBox(device.State == "unauthorized"
                    ? "Unlock the device and accept the USB debugging authorization prompt, then refresh."
                    : "Device is offline. Reconnect USB or wireless debugging, then refresh.", MessageType.Warning);
        }


        private void InstallAabToDevice(AdbDeviceInfo device)
        {
            string aabFile = System.IO.Path.GetFileName(
                _installAabPath);
            if (!EditorUtility.DisplayDialog(
                    "Install AAB",
                    $"Install {aabFile} to " +
                    $"{device.Model}?\n\n" +
                    "The AAB will be converted to APKS first.\nPackage: " + AabPackage,
                    "Install", "Cancel"))
            {
                return;
            }

            NoBuildDeviceOperations.Install(_installAabPath, AabPackage, device.Serial, device.DisplayName, true);
        }

        private void DrawWirelessRow(
            WirelessDeviceInfo device, int index)
        {
            device.IsConnected = AdbUtility.IsWirelessConnected(device, AdbDeviceCache.Devices);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
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
            using (new EditorGUI.DisabledScope(NoBuildDeviceOperations.IsBusy))
            {
                if (device.IsConnected)
                {
                    if (GUILayout.Button("Disconnect", GUILayout.Width(85), GUILayout.Height(22)))
                        RunWirelessCommand($"disconnect {device.IpAddress}:{device.Port}", "Disconnecting wireless device…");
                }
                else
                {
                    using (new EditorGUI.DisabledScope(device.Port <= 0))
                    {
                        if (GUILayout.Button("Connect", GUILayout.Width(70), GUILayout.Height(22)))
                            RunWirelessCommand($"connect {device.IpAddress}:{device.Port}", "Connecting wireless device…");
                    }
                    if (device.PairingPort > 0)
                    {
                        using (new EditorGUI.DisabledScope(_pairingCode.Length != 6 || !_pairingCode.All(character => character >= '0' && character <= '9')))
                        {
                            if (GUILayout.Button("Pair", GUILayout.Width(50), GUILayout.Height(22)))
                            {
                                RunWirelessCommand($"pair {device.IpAddress}:{device.PairingPort} {_pairingCode}", "Pairing wireless device…");
                                _pairingCode = "";
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private List<AdbDeviceInfo> GetCachedDevices()
        {
            AdbDeviceCache.EnsureFresh();
            return AdbDeviceCache.Devices;
        }

        private void RefreshDevices()
        {
            AdbDeviceCache.Refresh();
        }

        private void RunWirelessCommand(string command, string status)
        {
            NoBuildDeviceOperations.Start(status, async token =>
            {
                ProcessExecutionResult result = await AdbUtility.RunAsync(command, cancellationToken: token);
                if (result.TimedOut || result.ExitCode != 0 || result.CombinedOutput.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                    || result.CombinedOutput.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException(result.CombinedOutput);
                NoBuildEditorState.SetResult(result.CombinedOutput.Trim());
                _wirelessDevices = await AdbUtility.ScanWirelessDevicesAsync(token);
            });
        }

        // ══════════════════════════════════════════════════
        // ── Platform Icon Grid Selector
        // ══════════════════════════════════════════════════

        private void DrawPlatformSelector(SerializedProperty platP)
        {
            BuildTarget currentPlatform =
                (BuildTarget)platP.intValue;
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
                            platP.intValue =
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

        private string LblTxt(string label, string val)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            if (label == "Name") GUI.SetNextControlName("NoBuild.EntryName");
            string result = EditorGUILayout.TextField(val);
            if (label == "Name" && _focusName && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl("NoBuild.EntryName");
                _focusName = false;
            }
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

        private void DrawReferencePopup(string label, SerializedProperty property, string[] names, int tab)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelW));
            bool missing = property.intValue < -1 || property.intValue >= names.Length;
            string[] choices = new[] { "(None)" }.Concat(names).ToArray();
            if (missing) choices = choices.Concat(new[] { "(Missing reference)" }).ToArray();
            int oldChoice = missing ? choices.Length - 1 : property.intValue + 1;
            int choice = EditorGUILayout.Popup(oldChoice, choices);
            if (choice != oldChoice) property.intValue = choice - 1;
            using (new EditorGUI.DisabledScope(property.intValue < 0 || property.intValue >= names.Length))
            {
                if (GUILayout.Button("Edit…", GUILayout.Width(52)))
                {
                    int index = property.intValue;
                    _so.ApplyModifiedProperties();
                    _tab = tab;
                    if (tab == 0) _selSet = index; else _selDef = index;
                    RevealSelection();
                    EditorGUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                    return;
                }
            }
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
            if (GUILayout.Button("Insert Token…", GUILayout.Width(95)))
            {
                Rect btnRect =
                    GUILayoutUtility.GetLastRect();
                PlaceholderGuide.Show(btnRect,
                    key =>
                    {
                        prop.stringValue += key;
                        prop.serializedObject.ApplyModifiedProperties();
                    }, (NoBuildSettings)prop.serializedObject.targetObject);
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

        private static void RemapSceneSetReferencesAfterRemoval(
            NoBuildSettings settings,
            int removedIndex)
        {
            NoBuildEditorState.RemapContexts(settings, removedIndex, insertion: false, defines: false);
            foreach (BuildProfile profile in settings.buildProfiles)
            {
                if (profile.sceneSetIndex == removedIndex)
                {
                    profile.sceneSetIndex = -1;
                }
                else if (profile.sceneSetIndex > removedIndex)
                {
                    profile.sceneSetIndex--;
                }
            }
        }

        private static void RemapSceneSetReferencesAfterInsertion(
            NoBuildSettings settings,
            int insertionIndex)
        {
            NoBuildEditorState.RemapContexts(settings, insertionIndex, insertion: true, defines: false);

            foreach (BuildProfile profile in settings.buildProfiles)
            {
                if (profile.sceneSetIndex >= insertionIndex)
                {
                    profile.sceneSetIndex++;
                }
            }
        }

        private static void RemapDefineSetReferencesAfterRemoval(
            NoBuildSettings settings,
            int removedIndex)
        {
            NoBuildEditorState.RemapContexts(settings, removedIndex, insertion: false, defines: true);
            foreach (BuildProfile profile in settings.buildProfiles)
            {
                profile.scriptDefinitionSetIndex =
                    RemapIndexAfterRemoval(
                        profile.scriptDefinitionSetIndex,
                        removedIndex);
            }

            foreach (FlagDefinition flag in settings.flagDefinitions)
            {
                flag.scriptDefinitionSetIndex =
                    RemapIndexAfterRemoval(
                        flag.scriptDefinitionSetIndex,
                        removedIndex);
                if (flag.scriptDefinitionSetIndex < 0)
                {
                    flag.scriptDefinitionSlotIndex = -1;
                }
            }
        }

        private static void RemapDefineSetReferencesAfterInsertion(
            NoBuildSettings settings,
            int insertionIndex)
        {
            NoBuildEditorState.RemapContexts(settings, insertionIndex, insertion: true, defines: true);

            foreach (BuildProfile profile in settings.buildProfiles)
            {
                if (profile.scriptDefinitionSetIndex
                    >= insertionIndex)
                {
                    profile.scriptDefinitionSetIndex++;
                }
            }

            foreach (FlagDefinition flag in settings.flagDefinitions)
            {
                if (flag.scriptDefinitionSetIndex
                    >= insertionIndex)
                {
                    flag.scriptDefinitionSetIndex++;
                }
            }
        }

        private static void RemapDefineSlotReferencesAfterRemoval(
            NoBuildSettings settings,
            int defineSetIndex,
            int removedSlotIndex)
        {
            foreach (FlagDefinition flag in settings.flagDefinitions)
            {
                if (flag.type != FlagDefinitionType.Template
                    || flag.scriptDefinitionSetIndex
                    != defineSetIndex)
                {
                    continue;
                }

                flag.scriptDefinitionSlotIndex =
                    RemapIndexAfterRemoval(
                        flag.scriptDefinitionSlotIndex,
                        removedSlotIndex);
            }
        }

        private static int RemapIndexAfterRemoval(
            int value,
            int removedIndex)
        {
            if (value == removedIndex) return -1;
            return value > removedIndex ? value - 1 : value;
        }

        private void Refresh()
        {
            _so?.Dispose();
            _so = new SerializedObject(NoBuildResourceUtility.GetOrCreateSettings());
            _sceneSets = _so.FindProperty("sceneSets");
            _defineSets = _so.FindProperty("scriptDefinitionSets");
            _buildProfiles = _so.FindProperty("buildProfiles");
            _flagDefs = _so.FindProperty("flagDefinitions");
            _toolbarLabelTemplate = _so.FindProperty(
                "toolbarLabelTemplate");

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            _selSet = Mathf.Min(_selSet, s.sceneSets.Count - 1);
            _selDef = Mathf.Min(_selDef, s.scriptDefinitionSets.Count - 1);
            _selBuild = Mathf.Min(_selBuild, s.buildProfiles.Count - 1);
            _selFlag = Mathf.Min(_selFlag, s.flagDefinitions.Count - 1);
        }

    }
}
