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
        private static readonly string[] Tabs =
            { "Scene Sets", "Combinations", "Defines", "Build Profiles" };

        private SerializedObject _so;
        private SerializedProperty _sceneSets, _combos, _defineSets, _buildProfiles;
        private SerializedProperty _activeScene, _activeDefine, _shortcuts;
        private int _tab;
        private Vector2 _scroll;

        private NoBuildSettingsProvider(string p, SettingsScope sc,
            IEnumerable<string> kw = null) : base(p, sc, kw)
        {
            activateHandler = (_, _2) => Refresh();
            deactivateHandler = () => { _so?.Dispose(); _so = null; };
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new NoBuildSettingsProvider(Path, SettingsScope.Project,
                new[] { "nobuild", "scene", "switch", "define", "build", "profile", "combo" });
        }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);
            if (_so?.targetObject == null) { Refresh(); if (_so?.targetObject == null) return; }
            _so.Update();

            // Header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("NoBuild", EditorStyles.boldLabel,
                GUILayout.Width(100));
            EditorGUILayout.PropertyField(_shortcuts,
                new GUIContent("Shortcuts"));
            if (GUILayout.Button("Open Window", GUILayout.Width(110)))
                NoBuildWindow.ShowWindow();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "Scene sets, combinations, defines, and build profiles.",
                EditorStyles.miniLabel);
            GUILayout.Space(4);

            _tab = GUILayout.Toolbar(_tab, Tabs);
            GUILayout.Space(8);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            switch (_tab)
            {
                case 0: DrawSceneSets(); break;
                case 1: DrawCombinations(); break;
                case 2: DrawDefines(); break;
                case 3: DrawBuildProfiles(); break;
            }

            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();
        }

        // ══════════════════════════════════════════════════
        // ── Tab 0: Scene Sets
        // ══════════════════════════════════════════════════

        private void DrawSceneSets()
        {
            EditorGUILayout.LabelField("Scene Sets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Defined collections of scenes for full-set switching and builds.",
                EditorStyles.miniLabel);
            GUILayout.Space(4);

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (s.sceneSets.Count > 0)
            {
                string[] names = s.sceneSets.Select(x => x.setName).ToArray();
                string[] opts = new string[names.Length + 1];
                opts[0] = "(None)"; Array.Copy(names, 0, opts, 1, names.Length);
                int val = EditorGUILayout.Popup("Active Set",
                    s.activeSceneSetIndex + 1, opts) - 1;
                if (val != s.activeSceneSetIndex)
                {
                    s.activeSceneSetIndex = val; EditorUtility.SetDirty(s);
                }
            }

            GUILayout.Space(8);

            for (int i = 0; i < _sceneSets.arraySize; i++)
            {
                SerializedProperty sp = _sceneSets.GetArrayElementAtIndex(i);
                DrawSetElement(sp, i);
            }

            if (GUILayout.Button("+ Add Scene Set", GUILayout.Height(28)))
            {
                _sceneSets.arraySize++;
                _so.ApplyModifiedProperties();
                int ni = _sceneSets.arraySize - 1;
                _sceneSets.GetArrayElementAtIndex(ni)
                    .FindPropertyRelative("setName").stringValue =
                    "Scene Set " + (ni + 1);
            }
        }

        private void DrawSetElement(SerializedProperty sp, int idx)
        {
            var nameP = sp.FindPropertyRelative("setName");
            var scenesP = sp.FindPropertyRelative("scenes");
            var buildP = sp.FindPropertyRelative("buildOrderOverride");
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            bool active = s.activeSceneSetIndex == idx;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (active) { GUI.color = Color.green; GUILayout.Label("\u25CF", GUILayout.Width(20)); GUI.color = Color.white; }
            nameP.stringValue = EditorGUILayout.TextField(
                nameP.stringValue, EditorStyles.boldLabel);
            if (!active && GUILayout.Button("Set Active", GUILayout.Width(80)))
            {
                s.activeSceneSetIndex = idx; EditorUtility.SetDirty(s);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("\u00D7", GUILayout.Width(24)))
            {
                if (EditorUtility.DisplayDialog("Remove Set",
                        $"Remove '{nameP.stringValue}'?", "Remove", "Cancel"))
                {
                    _sceneSets.DeleteArrayElementAtIndex(idx);
                    _so.ApplyModifiedProperties();
                    if (s.activeSceneSetIndex == idx) s.activeSceneSetIndex = -1;
                    if (s.activeSceneSetIndex > idx) s.activeSceneSetIndex--;
                    return;
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Scenes list
            DrawSlotList("Scenes", scenesP);

            // Build order override
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Build Order Override (optional)",
                EditorStyles.miniBoldLabel);
            if (buildP.arraySize == 0)
                EditorGUILayout.LabelField("  (uses natural order)",
                    EditorStyles.miniLabel);

            for (int j = 0; j < buildP.arraySize; j++)
            {
                var slot = buildP.GetArrayElementAtIndex(j);
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = Color.gray;
                GUILayout.Label(" " + (j + 1) + " ", GUI.skin.box,
                    GUILayout.Width(28));
                GUI.backgroundColor = Color.white;
                var en = slot.FindPropertyRelative("enabled");
                en.boolValue = EditorGUILayout.Toggle(en.boolValue,
                    GUILayout.Width(20));
                slot.FindPropertyRelative("scene").objectReferenceValue =
                    EditorGUILayout.ObjectField(
                        slot.FindPropertyRelative("scene").objectReferenceValue,
                        typeof(SceneAsset), false);
                if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                { buildP.DeleteArrayElementAtIndex(j); break; }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add to build order",
                    GUILayout.Height(20)))
            {
                buildP.arraySize++;
                buildP.GetArrayElementAtIndex(buildP.arraySize - 1)
                    .FindPropertyRelative("enabled").boolValue = true;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawSlotList(string label, SerializedProperty listP)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            for (int i = 0; i < listP.arraySize; i++)
            {
                var slot = listP.GetArrayElementAtIndex(i);
                var en = slot.FindPropertyRelative("enabled");
                var sc = slot.FindPropertyRelative("scene");
                EditorGUILayout.BeginHorizontal();
                en.boolValue = EditorGUILayout.Toggle(en.boolValue,
                    GUILayout.Width(20));
                sc.objectReferenceValue = EditorGUILayout.ObjectField(
                    sc.objectReferenceValue, typeof(SceneAsset), false);
                if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                { listP.DeleteArrayElementAtIndex(i); break; }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Scene", GUILayout.Height(20)))
            {
                listP.arraySize++;
                listP.GetArrayElementAtIndex(listP.arraySize - 1)
                    .FindPropertyRelative("enabled").boolValue = true;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Tab 1: Combinations
        // ══════════════════════════════════════════════════

        private void DrawCombinations()
        {
            EditorGUILayout.LabelField("Scene Combinations (Shortcuts)",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Toolbar buttons [1]..[9] and Shift+Ctrl+1..9 map to these in order. " +
                "Each combination switches to a single target scene.",
                EditorStyles.miniLabel);
            GUILayout.Space(4);

            if (_combos.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No combinations. Add some to enable quick-scene switching.",
                    MessageType.Info);
            }

            for (int i = 0; i < _combos.arraySize; i++)
            {
                var cp = _combos.GetArrayElementAtIndex(i);
                var nameP = cp.FindPropertyRelative("name");
                var enP = cp.FindPropertyRelative("enabled");
                var targetP = cp.FindPropertyRelative("targetScene");
                var tScene = targetP.FindPropertyRelative("scene");
                var tEn = targetP.FindPropertyRelative("enabled");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                // Shortcut number badge
                GUI.backgroundColor = Color.gray;
                GUILayout.Label(" " + (i + 1) + " ", GUI.skin.box,
                    GUILayout.Width(26));
                GUI.backgroundColor = Color.white;

                enP.boolValue = EditorGUILayout.Toggle(enP.boolValue,
                    GUILayout.Width(20));
                nameP.stringValue = EditorGUILayout.TextField(
                    nameP.stringValue);

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("\u00D7", GUILayout.Width(24)))
                { _combos.DeleteArrayElementAtIndex(i); break; }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;
                tEn.boolValue = EditorGUILayout.Toggle("Target enabled",
                    tEn.boolValue, GUILayout.Width(150));
                tScene.objectReferenceValue = EditorGUILayout.ObjectField(
                    "Target Scene", tScene.objectReferenceValue,
                    typeof(SceneAsset), false);
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            GUILayout.Space(4);
            if (GUILayout.Button("+ Add Combination", GUILayout.Height(28)))
            {
                _combos.arraySize++;
                _so.ApplyModifiedProperties();
                int ni = _combos.arraySize - 1;
                var nc = _combos.GetArrayElementAtIndex(ni);
                nc.FindPropertyRelative("name").stringValue =
                    "Combo " + (ni + 1);
                nc.FindPropertyRelative("enabled").boolValue = true;
                nc.FindPropertyRelative("targetScene")
                    .FindPropertyRelative("enabled").boolValue = true;
            }

            // Reorder hint
            EditorGUILayout.HelpBox(
                "Drag to reorder in the Inspector (lock icon → Debug mode) " +
                "or remove and re-add in the desired order.",
                MessageType.Info);
        }

        // ══════════════════════════════════════════════════
        // ── Tab 2: Defines
        // ══════════════════════════════════════════════════

        private void DrawDefines()
        {
            EditorGUILayout.LabelField("Script Definition Sets",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Collections of scripting define symbols for conditional compilation.",
                EditorStyles.miniLabel);
            GUILayout.Space(4);

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            if (s.scriptDefinitionSets.Count > 0)
            {
                string[] names = s.scriptDefinitionSets
                    .Select(x => x.setName).ToArray();
                string[] opts = new string[names.Length + 1];
                opts[0] = "(None)"; Array.Copy(names, 0, opts, 1, names.Length);
                int val = EditorGUILayout.Popup("Active Set",
                    s.activeScriptDefinitionSetIndex + 1, opts) - 1;
                if (val != s.activeScriptDefinitionSetIndex)
                {
                    s.activeScriptDefinitionSetIndex = val;
                    EditorUtility.SetDirty(s);
                    if (val >= 0)
                        ScriptDefinitionSwitcher.ApplySet(
                            s.scriptDefinitionSets[val]);
                }
            }

            GUILayout.Space(8);

            for (int i = 0; i < _defineSets.arraySize; i++)
            {
                var sp = _defineSets.GetArrayElementAtIndex(i);
                DrawDefineElement(sp, i);
            }

            if (GUILayout.Button("+ Add Define Set", GUILayout.Height(28)))
            {
                _defineSets.arraySize++;
                _so.ApplyModifiedProperties();
                _defineSets.GetArrayElementAtIndex(
                    _defineSets.arraySize - 1)
                    .FindPropertyRelative("setName").stringValue =
                    "Define Set " + _defineSets.arraySize;
            }
        }

        private void DrawDefineElement(SerializedProperty sp, int idx)
        {
            var nameP = sp.FindPropertyRelative("setName");
            var slotsP = sp.FindPropertyRelative("slots");
            NoBuildSettings s = (NoBuildSettings)_so.targetObject;
            bool active = s.activeScriptDefinitionSetIndex == idx;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (active) { GUI.color = Color.green; GUILayout.Label("\u25CF", GUILayout.Width(20)); GUI.color = Color.white; }
            nameP.stringValue = EditorGUILayout.TextField(
                nameP.stringValue, EditorStyles.boldLabel);
            if (!active && GUILayout.Button("Apply", GUILayout.Width(60)))
            {
                s.activeScriptDefinitionSetIndex = idx;
                EditorUtility.SetDirty(s);
                _so.ApplyModifiedProperties();
                ScriptDefinitionSwitcher.ApplySet(
                    s.scriptDefinitionSets[idx]);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("\u00D7", GUILayout.Width(24)))
            {
                if (EditorUtility.DisplayDialog("Remove Set",
                        $"Remove '{nameP.stringValue}'?", "Remove", "Cancel"))
                {
                    _defineSets.DeleteArrayElementAtIndex(idx);
                    _so.ApplyModifiedProperties();
                    if (s.activeScriptDefinitionSetIndex == idx)
                        s.activeScriptDefinitionSetIndex = -1;
                    if (s.activeScriptDefinitionSetIndex > idx)
                        s.activeScriptDefinitionSetIndex--;
                    return;
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            for (int j = 0; j < slotsP.arraySize; j++)
            {
                var slot = slotsP.GetArrayElementAtIndex(j);
                var def = slot.FindPropertyRelative("defineSymbol");
                var en = slot.FindPropertyRelative("enabled");
                EditorGUILayout.BeginHorizontal();
                en.boolValue = EditorGUILayout.Toggle(en.boolValue,
                    GUILayout.Width(20));
                def.stringValue = EditorGUILayout.TextField(
                    def.stringValue);
                if (GUILayout.Button("\u2212", GUILayout.Width(22)))
                { slotsP.DeleteArrayElementAtIndex(j); break; }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Symbol", GUILayout.Height(20)))
                slotsP.arraySize++;
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        // ══════════════════════════════════════════════════
        // ── Tab 3: Build Profiles
        // ══════════════════════════════════════════════════

        private void DrawBuildProfiles()
        {
            EditorGUILayout.LabelField("Build Profiles", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Configure one-click builds with scene sets, defines, and platform settings.",
                EditorStyles.miniLabel);
            GUILayout.Space(4);

            if (_buildProfiles.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No build profiles. Click 'Add Build Profile' to create one.",
                    MessageType.Info);
            }

            NoBuildSettings s = (NoBuildSettings)_so.targetObject;

            for (int i = 0; i < _buildProfiles.arraySize; i++)
            {
                var pp = _buildProfiles.GetArrayElementAtIndex(i);
                var nameP = pp.FindPropertyRelative("profileName");
                var ssP = pp.FindPropertyRelative("sceneSetIndex");
                var dsP = pp.FindPropertyRelative("scriptDefinitionSetIndex");
                var cfgP = pp.FindPropertyRelative("buildConfiguration");
                var tmplP = pp.FindPropertyRelative("buildNameTemplate.template");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                nameP.stringValue = EditorGUILayout.TextField(
                    nameP.stringValue, EditorStyles.boldLabel);

                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                if (GUILayout.Button("\u25B6 Build", GUILayout.Width(80)))
                {
                    _so.ApplyModifiedProperties();
                    BuildExecutor.Build(s.buildProfiles[i]);
                }

                GUI.backgroundColor = Color.white;

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("\u00D7", GUILayout.Width(24)))
                { _buildProfiles.DeleteArrayElementAtIndex(i); break; }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;
                string[] ssNames = s.sceneSets.Select(x => x.setName).ToArray();
                ssP.intValue = EditorGUILayout.Popup("Scene Set",
                    ssP.intValue + 1,
                    new[] { "(None)" }.Concat(ssNames).ToArray()) - 1;

                string[] dsNames = s.scriptDefinitionSets
                    .Select(x => x.setName).ToArray();
                dsP.intValue = EditorGUILayout.Popup("Define Set",
                    dsP.intValue + 1,
                    new[] { "(None)" }.Concat(dsNames).ToArray()) - 1;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Build Name", GUILayout.Width(80));
                tmplP.stringValue = EditorGUILayout.TextField(
                    tmplP.stringValue);
                EditorGUILayout.EndHorizontal();

                string prev = BuildNameResolver.Resolve(
                    tmplP.stringValue,
                    s.buildProfiles.Count > i ? s.buildProfiles[i] : null, s);
                EditorGUILayout.LabelField("  Preview: " + prev,
                    EditorStyles.miniLabel);

                var platformP = cfgP.FindPropertyRelative("platform");
                var devP = cfgP.FindPropertyRelative("developmentBuild");
                var dbgP = cfgP.FindPropertyRelative("allowDebugging");
                var profP = cfgP.FindPropertyRelative("connectWithProfiler");
                var beP = cfgP.FindPropertyRelative("scriptingBackend");
                var ilP = cfgP.FindPropertyRelative("il2CppCodeGeneration");
                var slP = cfgP.FindPropertyRelative("strippingLevel");
                var seP = cfgP.FindPropertyRelative("stripEngineCode");
                var biP = cfgP.FindPropertyRelative("bundleIdentifierOverride");
                var pnP = cfgP.FindPropertyRelative("productNameOverride");

                EditorGUILayout.PropertyField(platformP);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(devP, GUILayout.Width(150));
                EditorGUILayout.PropertyField(dbgP, GUILayout.Width(150));
                EditorGUILayout.PropertyField(profP, GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(beP);
                EditorGUILayout.PropertyField(ilP);
                EditorGUILayout.PropertyField(slP);
                EditorGUILayout.PropertyField(seP);
                EditorGUILayout.PropertyField(biP);
                EditorGUILayout.PropertyField(pnP);
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                GUILayout.Space(6);
            }

            if (GUILayout.Button("+ Add Build Profile", GUILayout.Height(28)))
            {
                _buildProfiles.arraySize++;
                _so.ApplyModifiedProperties();
                _buildProfiles.GetArrayElementAtIndex(
                    _buildProfiles.arraySize - 1)
                    .FindPropertyRelative("profileName").stringValue =
                    "Build Profile " + _buildProfiles.arraySize;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Helpers
        // ══════════════════════════════════════════════════

        private void Refresh()
        {
            _so?.Dispose();
            NoBuildSettings s = NoBuildResourceUtility.GetOrCreateSettings();
            _so = new SerializedObject(s);
            _sceneSets = _so.FindProperty("sceneSets");
            _combos = _so.FindProperty("combinations");
            _defineSets = _so.FindProperty("scriptDefinitionSets");
            _buildProfiles = _so.FindProperty("buildProfiles");
            _activeScene = _so.FindProperty("activeSceneSetIndex");
            _activeDefine = _so.FindProperty("activeScriptDefinitionSetIndex");
            _shortcuts = _so.FindProperty("shortcutsEnabled");
        }
    }
}
