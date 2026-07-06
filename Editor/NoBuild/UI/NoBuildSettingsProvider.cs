// ═══════════════════════════════════════════════════════════
// ── NoBuildSettingsProvider ───────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    public sealed class NoBuildSettingsProvider : SettingsProvider
    {
        private const string Path = "Project/NoBuild";
        private const float SidebarW = 190f;
        private const float LabelW = 130f;
        private const float EntryH = 26f;
        private static readonly string[] Tabs = { "Scene Sets", "Defines", "Build Profiles" };

        private SerializedObject _so;
        private SerializedProperty _sceneSets, _defineSets, _buildProfiles;
        private SerializedProperty _activeScene, _activeDefine, _shortcuts;
        private int _tab;
        private int _selSet = -1, _selDef = -1, _selBuild = -1;
        private Vector2 _sbScroll, _pvScroll;

        // ── GUI Styles (lazy) ────────────────────────
        private GUIStyle _entryStyle, _entrySelStyle, _entryActStyle, _badgeStyle;
        private bool _stylesBuilt;

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            Texture2D blank = new Texture2D(1, 1);
            blank.SetPixel(0, 0, Color.clear); blank.Apply();

            _entryStyle = new GUIStyle
            {
                fixedHeight = EntryH,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 4, 0, 0),
                margin = new RectOffset(),
                clipping = TextClipping.Clip,
                fontSize = 11,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f), background = blank },
                hover = { textColor = Color.white, background = blank },
            };
            _entrySelStyle = new GUIStyle(_entryStyle)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
            _entryActStyle = new GUIStyle(_entryStyle)
            {
                normal = { textColor = new Color(0.3f, 0.95f, 0.3f) }
            };
            _badgeStyle = new GUIStyle(GUI.skin.box)
            {
                fixedHeight = 18, fixedWidth = 22,
                alignment = TextAnchor.MiddleCenter, fontSize = 10,
                padding = new RectOffset(), margin = new RectOffset(2, 4, 2, 2)
            };
        }

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
            BuildStyles();

            // Auto-select first entry when tab has no selection
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_tab == 0 && (_selSet < 0 || _selSet >= s.sceneSets.Count) && s.sceneSets.Count > 0)
                _selSet = 0;
            if (_tab == 1 && (_selDef < 0 || _selDef >= s.scriptDefinitionSets.Count) && s.scriptDefinitionSets.Count > 0)
                _selDef = 0;
            if (_tab == 2 && (_selBuild < 0 || _selBuild >= s.buildProfiles.Count) && s.buildProfiles.Count > 0)
                _selBuild = 0;

            DrawHeader();
            _tab = GUILayout.Toolbar(_tab, Tabs);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            GUILayout.Space(2);
            DrawPreview();
            EditorGUILayout.EndHorizontal();

            _so.ApplyModifiedProperties();
        }

        // ══════════════════════════════════════════════════
        // ── Header
        // ══════════════════════════════════════════════════

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("NoBuild", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.PropertyField(_shortcuts, new GUIContent("Shortcuts"));
            if (GUILayout.Button("Open Window", GUILayout.Width(110))) NoBuildWindow.ShowWindow();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Scene sets, defines, and build profiles.", EditorStyles.miniLabel);
            GUILayout.Space(4);
        }

        // ══════════════════════════════════════════════════
        // ── Sidebar (fixed-width, constant-height entries)
        // ══════════════════════════════════════════════════

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarW));
            _sbScroll = EditorGUILayout.BeginScrollView(_sbScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;

            switch (_tab)
            {
                case 0: DrawSidebarList(s.sceneSets.Select(x => x.setName).ToList(),
                    s.activeSceneSetIndex, ref _selSet); break;
                case 1: DrawSidebarList(s.scriptDefinitionSets.Select(x => x.setName).ToList(),
                    s.activeScriptDefinitionSetIndex, ref _selDef); break;
                case 2: DrawSidebarList(s.buildProfiles.Select(x =>
                    x.profileName + "  [" + x.buildConfiguration.platform + "]").ToList(),
                    -1, ref _selBuild); break;
            }

            EditorGUILayout.EndScrollView();
            GUILayout.Space(4);

            string add = _tab switch { 0 => "+ Scene Set", 1 => "+ Define Set", _ => "+ Build Profile" };
            if (GUILayout.Button(add, GUILayout.Height(28))) AddNew();
            EditorGUILayout.EndVertical();

            // Vertical separator
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
        }

        private void DrawSidebarList(List<string> names, int activeIdx, ref int selIdx)
        {
            for (int i = 0; i < names.Count; i++)
            {
                bool isSel = selIdx == i;
                bool isAct = activeIdx == i;

                Rect rowRect = GUILayoutUtility.GetRect(
                    new GUIContent(names[i]), _entryStyle,
                    GUILayout.Height(EntryH), GUILayout.ExpandWidth(true));

                // ── Background + Border ──
                Color bgColor, borderColor;
                float borderW;

                if (isAct)
                {
                    bgColor    = new Color(0.15f, 0.5f, 0.15f, 0.45f);
                    borderColor = isSel
                        ? new Color(0.35f, 0.65f, 0.9f, 1f)
                        : new Color(0.2f, 0.55f, 0.2f, 0.7f);
                    borderW = isSel ? 2f : 1f;
                }
                else if (isSel)
                {
                    bgColor    = new Color(0.22f, 0.42f, 0.7f, 0.35f);
                    borderColor = new Color(0.35f, 0.55f, 0.85f, 0.9f);
                    borderW = 2f;
                }
                else
                {
                    bgColor    = new Color(0.25f, 0.25f, 0.25f, 0.15f);
                    borderColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
                    borderW = 1f;
                }

                // Fill
                EditorGUI.DrawRect(rowRect, bgColor);
                // Inner fill (slightly smaller for border effect)
                Rect inner = rowRect;
                inner.x += borderW; inner.y += borderW;
                inner.width -= borderW * 2f; inner.height -= borderW * 2f;
                EditorGUI.DrawRect(inner, new Color(bgColor.r, bgColor.g, bgColor.b, bgColor.a * 0.7f));
                // Border (draw edges)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, borderW), borderColor);               // top
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - borderW, rowRect.width, borderW), borderColor);    // bottom
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, borderW, rowRect.height), borderColor);                // left
                EditorGUI.DrawRect(new Rect(rowRect.xMax - borderW, rowRect.y, borderW, rowRect.height), borderColor);    // right

                // ── Label ──
                var style = isSel ? _entrySelStyle : isAct ? _entryActStyle : _entryStyle;
                string label = Trunc(names[i], 18);
                if (GUI.Button(rowRect, label, style))
                {
                    selIdx = i;
                }
            }
        }

        private void AddNew()
        {
            switch (_tab)
            {
                case 0: _sceneSets.arraySize++; _so.ApplyModifiedProperties();
                    _selSet = _sceneSets.arraySize - 1;
                    _sceneSets.GetArrayElementAtIndex(_selSet)
                        .FindPropertyRelative("setName").stringValue = "Scene Set " + _sceneSets.arraySize;
                    break;
                case 1: _defineSets.arraySize++; _so.ApplyModifiedProperties();
                    _selDef = _defineSets.arraySize - 1;
                    _defineSets.GetArrayElementAtIndex(_selDef)
                        .FindPropertyRelative("setName").stringValue = "Define Set " + _defineSets.arraySize;
                    break;
                case 2: _buildProfiles.arraySize++; _so.ApplyModifiedProperties();
                    _selBuild = _buildProfiles.arraySize - 1;
                    _buildProfiles.GetArrayElementAtIndex(_selBuild)
                        .FindPropertyRelative("profileName").stringValue = "Build Profile " + _buildProfiles.arraySize;
                    break;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Preview Panel
        // ══════════════════════════════════════════════════

        private void DrawPreview()
        {
            EditorGUILayout.BeginVertical();
            _pvScroll = EditorGUILayout.BeginScrollView(_pvScroll);

            switch (_tab) { case 0: DrawSceneSetPreview(); break; case 1: DrawDefinePreview(); break; case 2: DrawBuildPreview(); break; }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ──────────────────────────────────────────────
        // Scene Set Preview
        // ──────────────────────────────────────────────

        private void DrawSceneSetPreview()
        {
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (_selSet < 0 || _selSet >= _sceneSets.arraySize)
            { EditorGUILayout.LabelField("Select a scene set from the sidebar.", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true)); return; }

            var sp = _sceneSets.GetArrayElementAtIndex(_selSet);
            var nameP = sp.FindPropertyRelative("setName");
            var scenesP = sp.FindPropertyRelative("scenes");
            var buildP = sp.FindPropertyRelative("buildOrderOverride");
            var combosP = sp.FindPropertyRelative("combinations");
            bool active = s.activeSceneSetIndex == _selSet;

            // Title
            EditorGUILayout.BeginHorizontal();
            var oldC = GUI.contentColor;
            if (active) GUI.contentColor = Color.green;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            if (!active && GUILayout.Button("Set Active", GUILayout.Width(80)))
            { s.activeSceneSetIndex = _selSet; EditorUtility.SetDirty(s); }
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            { if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                { _sceneSets.DeleteArrayElementAtIndex(_selSet); _so.ApplyModifiedProperties();
                  if (s.activeSceneSetIndex == _selSet) s.activeSceneSetIndex = -1;
                  if (s.activeSceneSetIndex > _selSet) s.activeSceneSetIndex--;
                  _selSet = Mathf.Min(_selSet, _sceneSets.arraySize - 1); return; } }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Name
            nameP.stringValue = LblTxt("Name", nameP.stringValue);
            GUILayout.Space(6);

            // ── Scenes + Build Order (integrated) ──
            EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);
            bool hasCustomOrder = buildP.arraySize > 0;
            var source = hasCustomOrder ? buildP : scenesP;

            if (source.arraySize == 0)
                EditorGUILayout.LabelField("  (no scenes)", EditorStyles.miniLabel);

            for (int i = 0; i < source.arraySize; i++)
            {
                var slot = source.GetArrayElementAtIndex(i);
                var en = slot.FindPropertyRelative("enabled");
                var sc = slot.FindPropertyRelative("scene");

                EditorGUILayout.BeginHorizontal();

                // Position badge
                GUI.backgroundColor = hasCustomOrder ? new Color(0.4f, 0.6f, 0.9f) : Color.gray;
                GUILayout.Label((i + 1).ToString(), _badgeStyle);
                GUI.backgroundColor = Color.white;

                en.boolValue = EditorGUILayout.Toggle(en.boolValue, GUILayout.Width(16));
                sc.objectReferenceValue = EditorGUILayout.ObjectField(
                    sc.objectReferenceValue, typeof(SceneAsset), false);

                // Reorder controls (only for custom order)
                if (hasCustomOrder)
                {
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("\u25B2", GUILayout.Width(22)))
                    { buildP.MoveArrayElement(i, i - 1); break; }
                    GUI.enabled = i < buildP.arraySize - 1;
                    if (GUILayout.Button("\u25BC", GUILayout.Width(22)))
                    { buildP.MoveArrayElement(i, i + 1); break; }
                    GUI.enabled = true;
                }

                if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                { source.DeleteArrayElementAtIndex(i); break; }

                EditorGUILayout.EndHorizontal();
            }

            // Add scene button
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Scene", GUILayout.Height(22), GUILayout.Width(100)))
            {
                var target = hasCustomOrder ? buildP : scenesP;
                target.arraySize++;
                target.GetArrayElementAtIndex(target.arraySize - 1)
                    .FindPropertyRelative("enabled").boolValue = true;
            }

            // Enable/disable custom order
            GUILayout.FlexibleSpace();
            if (hasCustomOrder)
            {
                if (GUILayout.Button("Reset Order", GUILayout.Width(100)))
                {
                    buildP.ClearArray();
                    _so.ApplyModifiedProperties();
                }
            }
            else
            {
                GUI.color = new Color(0.7f, 0.7f, 1f);
                if (GUILayout.Button("Customize Order", GUILayout.Width(120)))
                {
                    // Copy scenes to build order override
                    buildP.arraySize = scenesP.arraySize;
                    for (int i = 0; i < scenesP.arraySize; i++)
                    {
                        var src = scenesP.GetArrayElementAtIndex(i);
                        var dst = buildP.GetArrayElementAtIndex(i);
                        dst.FindPropertyRelative("scene").objectReferenceValue =
                            src.FindPropertyRelative("scene").objectReferenceValue;
                        dst.FindPropertyRelative("enabled").boolValue =
                            src.FindPropertyRelative("enabled").boolValue;
                    }
                }
                GUI.color = Color.white;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ── Combinations ──
            EditorGUILayout.LabelField("Shortcut Combinations", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  Toolbar [1]..[9] map to these when this set is active.",
                EditorStyles.miniLabel);

            for (int i = 0; i < combosP.arraySize; i++)
            {
                var cp = combosP.GetArrayElementAtIndex(i);
                var cName = cp.FindPropertyRelative("name");
                var cEn = cp.FindPropertyRelative("enabled");
                var cScenes = cp.FindPropertyRelative("scenes");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = Color.gray;
                GUILayout.Label((i + 1).ToString(), _badgeStyle);
                GUI.backgroundColor = Color.white;
                cEn.boolValue = EditorGUILayout.Toggle(cEn.boolValue, GUILayout.Width(16));
                cName.stringValue = EditorGUILayout.TextField(cName.stringValue, GUILayout.Width(120));
                if (GUILayout.Button("\u00D7", GUILayout.Width(22)))
                { combosP.DeleteArrayElementAtIndex(i); break; }
                EditorGUILayout.EndHorizontal();

                // Scene rows
                for (int j = 0; j < cScenes.arraySize; j++)
                {
                    var slotP = cScenes.GetArrayElementAtIndex(j);
                    var sEn = slotP.FindPropertyRelative("enabled");
                    var sSc = slotP.FindPropertyRelative("scene");

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(42); // indent under badge+checkbox
                    sEn.boolValue = EditorGUILayout.Toggle(sEn.boolValue, GUILayout.Width(16));
                    sSc.objectReferenceValue = EditorGUILayout.ObjectField(
                        sSc.objectReferenceValue, typeof(SceneAsset), false);

                    GUI.enabled = j > 0;
                    if (GUILayout.Button("\u25B2", GUILayout.Width(22)))
                    { cScenes.MoveArrayElement(j, j - 1); break; }
                    GUI.enabled = j < cScenes.arraySize - 1;
                    if (GUILayout.Button("\u25BC", GUILayout.Width(22)))
                    { cScenes.MoveArrayElement(j, j + 1); break; }
                    GUI.enabled = true;
                    if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                    { cScenes.DeleteArrayElementAtIndex(j); break; }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("+ Add Scene", GUILayout.Height(20)))
                {
                    cScenes.arraySize++;
                    cScenes.GetArrayElementAtIndex(cScenes.arraySize - 1)
                        .FindPropertyRelative("enabled").boolValue = true;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            if (GUILayout.Button("+ Add Combination", GUILayout.Height(22)))
            {
                combosP.arraySize++; var nc = combosP.GetArrayElementAtIndex(combosP.arraySize - 1);
                nc.FindPropertyRelative("name").stringValue = "Combo " + combosP.arraySize;
                nc.FindPropertyRelative("enabled").boolValue = true;
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

            EditorGUILayout.BeginHorizontal();
            var oldC = GUI.contentColor;
            if (active) GUI.contentColor = Color.green;
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUI.contentColor = oldC; GUILayout.FlexibleSpace();
            if (!active && GUILayout.Button("Apply", GUILayout.Width(60)))
            { s.activeScriptDefinitionSetIndex = _selDef; EditorUtility.SetDirty(s);
                _so.ApplyModifiedProperties();
                ScriptDefinitionSwitcher.ApplySet(s.scriptDefinitionSets[_selDef]); }
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            { if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                { _defineSets.DeleteArrayElementAtIndex(_selDef); _so.ApplyModifiedProperties();
                  if (s.activeScriptDefinitionSetIndex == _selDef) s.activeScriptDefinitionSetIndex = -1;
                  if (s.activeScriptDefinitionSetIndex > _selDef) s.activeScriptDefinitionSetIndex--;
                  _selDef = Mathf.Min(_selDef, _defineSets.arraySize - 1); return; } }
            GUI.backgroundColor = Color.white;
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

            // Title
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(nameP.stringValue, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("\u25B6 Build", GUILayout.Width(70)))
            { _so.ApplyModifiedProperties();
                BuildExecutor.Build(s.buildProfiles[_selBuild]); }
            GUI.backgroundColor = Color.white;
            if (s.buildProfiles[_selBuild].buildConfiguration.platform == BuildTarget.Android)
            {
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f);
                if (GUILayout.Button("\u25B6\u25B6 Run", GUILayout.Width(65)))
                { _so.ApplyModifiedProperties();
                    BuildExecutor.BuildAndRun(s.buildProfiles[_selBuild]); }
                GUI.backgroundColor = Color.white;
            }
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            { if (EditorUtility.DisplayDialog("Delete", $"Delete '{nameP.stringValue}'?", "Delete", "Cancel"))
                { _buildProfiles.DeleteArrayElementAtIndex(_selBuild); _so.ApplyModifiedProperties();
                  _selBuild = Mathf.Min(_selBuild, _buildProfiles.arraySize - 1); return; } }
            GUI.backgroundColor = Color.white;
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
            EditorGUILayout.PropertyField(platP);
            BuildTarget selPlat = (BuildTarget)platP.enumValueIndex;
            GUILayout.Space(4);

            // General
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            LblChk("Development Build", cfgP.FindPropertyRelative("developmentBuild"));
            LblChk("Script Debugging", cfgP.FindPropertyRelative("allowDebugging"));
            LblChk("Connect Profiler", cfgP.FindPropertyRelative("connectWithProfiler"));
            LblProp("Scripting Backend", cfgP.FindPropertyRelative("scriptingBackend"));
            LblProp("IL2CPP Code Gen", cfgP.FindPropertyRelative("il2CppCodeGeneration"));
            LblProp("Stripping Level", cfgP.FindPropertyRelative("strippingLevel"));
            LblChk("Strip Engine Code", cfgP.FindPropertyRelative("stripEngineCode"));
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
                LblChk("Build App Bundle (AAB)", cfgP.FindPropertyRelative("androidBuildAppBundle"));
                LblChk("Split Binary", cfgP.FindPropertyRelative("androidSplitBinary"));
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
                PlaceholderGuide.Show(GUILayoutUtility.GetLastRect());
            EditorGUILayout.EndHorizontal();
        }

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
            _activeScene = _so.FindProperty("activeSceneSetIndex");
            _activeDefine = _so.FindProperty("activeScriptDefinitionSetIndex");
            _shortcuts = _so.FindProperty("shortcutsEnabled");

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            _selSet = Mathf.Min(_selSet, s.sceneSets.Count - 1);
            _selDef = Mathf.Min(_selDef, s.scriptDefinitionSets.Count - 1);
            _selBuild = Mathf.Min(_selBuild, s.buildProfiles.Count - 1);
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > max ? s.Substring(0, max - 1) + "\u2026" : s;
        }
    }
}
