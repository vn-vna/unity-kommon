// ═══════════════════════════════════════════════════════════
// ── NoBuildWindow ─────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    public sealed class NoBuildWindow : EditorWindow
    {
        private bool _compactMode = true;
        private Vector2 _scroll;
        private NoBuildSettings _settings;
        private bool _dirty;

        private static readonly string[] TabNames =
            { "Scene Sets", "Combinations", "Defines", "Build Profiles" };
        private int _tab;

        [MenuItem("Dev Menu/Tools/NoBuild")]
        public static void ShowWindow()
        {
            NoBuildWindow w = GetWindow<NoBuildWindow>("NoBuild");
            w.minSize = new Vector2(400, 320);
            w.Show();
        }

        private void OnEnable()
        {
            _settings = NoBuildResourceUtility.GetOrCreateSettings();
            _dirty = false;
        }

        private void OnDisable()
        {
            if (_dirty && _settings != null)
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                EditorGUILayout.HelpBox(
                    "NoBuild settings not loaded. Open Project Settings \u2192 NoBuild.",
                    MessageType.Warning);
                if (GUILayout.Button("Create")) _settings =
                    NoBuildResourceUtility.GetOrCreateSettings();
                return;
            }

            DrawToolbar();
            GUILayout.Space(4);

            if (_compactMode) DrawCompact();
            else DrawFull();

            if (_dirty) { EditorUtility.SetDirty(_settings); _dirty = false; }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("NoBuild", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            _compactMode = GUILayout.Toggle(_compactMode, "Compact",
                EditorStyles.toolbarButton, GUILayout.Width(70));
            if (GUILayout.Toggle(!_compactMode, "Full",
                    EditorStyles.toolbarButton, GUILayout.Width(50)))
                _compactMode = false;

            GUILayout.Space(8);
            if (GUILayout.Button("Project Settings", EditorStyles.toolbarButton,
                    GUILayout.Width(110)))
                SettingsService.OpenProjectSettings("Project/NoBuild");
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCompact()
        {
            EditorGUILayout.LabelField("Quick Switch", EditorStyles.boldLabel);
            GUILayout.Space(4);

            // Scene Set
            string[] ssNames = _settings.sceneSets.ConvertAll(x => x.setName).ToArray();
            int newSS = EditorGUILayout.Popup("Scene Set",
                _settings.activeSceneSetIndex + 1,
                Concat("(None)", ssNames)) - 1;
            if (newSS != _settings.activeSceneSetIndex && newSS >= -1)
            {
                _settings.activeSceneSetIndex = newSS; _dirty = true;
                if (newSS >= 0) SceneSwitcher.SwitchToSet(
                    _settings.sceneSets[newSS]);
            }

            // Defines
            string[] dsNames = _settings.scriptDefinitionSets
                .ConvertAll(x => x.setName).ToArray();
            int newDS = EditorGUILayout.Popup("Defines",
                _settings.activeScriptDefinitionSetIndex + 1,
                Concat("(None)", dsNames)) - 1;
            if (newDS != _settings.activeScriptDefinitionSetIndex && newDS >= -1)
            {
                _settings.activeScriptDefinitionSetIndex = newDS; _dirty = true;
                if (newDS >= 0) ScriptDefinitionSwitcher.ApplySet(
                    _settings.scriptDefinitionSets[newDS]);
            }

            GUILayout.Space(12);

            // Combinations (from active set)
            var activeSet = _settings.ActiveSceneSet;
            if (activeSet != null && activeSet.combinations.Count > 0)
            {
                EditorGUILayout.LabelField("Open Scenes (Combinations)", EditorStyles.boldLabel);
                var combos = activeSet.GetEnabledCombinations();
                for (int i = 0; i < Mathf.Min(combos.Count, 9); i++)
                {
                    var c = combos[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("  " + (i + 1) + ".", GUILayout.Width(30));
                    if (GUILayout.Button(c.DisplayName, EditorStyles.label))
                        SceneSwitcher.SwitchToCombination(c, activeSet);
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(12);

            // Build
            EditorGUILayout.LabelField("Quick Build", EditorStyles.boldLabel);
            if (_settings.buildProfiles.Count == 0)
                EditorGUILayout.HelpBox("No build profiles.", MessageType.Info);
            else
                foreach (var bp in _settings.buildProfiles)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUIContent icon = PlatformIconUtility.GetPlatformIcon(
                        bp.buildConfiguration.platform);
                    if (icon != null && icon.image != null)
                        GUILayout.Label(icon, GUILayout.Width(22),
                            GUILayout.Height(22));

                    string prev = BuildNameResolver.Resolve(
                        bp.buildNameTemplate?.template ?? "{app-version}",
                        bp, _settings);
                    EditorGUILayout.LabelField(bp.profileName);
                    EditorGUILayout.LabelField(prev, EditorStyles.miniLabel);
                    if (GUILayout.Button("Build", GUILayout.Width(60)))
                        BuildExecutor.Build(bp);
                    EditorGUILayout.EndHorizontal();
                }
        }

        private void DrawFull()
        {
            DrawCustomTabBar(ref _tab, TabNames);
            GUILayout.Space(8);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            switch (_tab)
            {
                case 0: DrawFullSceneSets(); break;
                case 1: DrawFullCombos(); break;
                case 2: DrawFullDefines(); break;
                case 3: DrawFullBuildProfiles(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawFullSceneSets()
        {
            EditorGUILayout.LabelField("Scene Sets", EditorStyles.boldLabel);
            foreach (var s in _settings.sceneSets)
            {
                bool a = _settings.ActiveSceneSet == s;
                EditorGUILayout.BeginHorizontal();
                if (a) { GUI.color = Color.green; GUILayout.Label("\u25CF", GUILayout.Width(20)); GUI.color = Color.white; }
                EditorGUILayout.LabelField(s.setName, EditorStyles.boldLabel);
                if (!a && GUILayout.Button("Switch", GUILayout.Width(60)))
                {
                    _settings.activeSceneSetIndex =
                        _settings.sceneSets.IndexOf(s);
                    _dirty = true;
                    SceneSwitcher.SwitchToSet(s);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFullCombos()
        {
            EditorGUILayout.LabelField("Combinations (Shortcuts)", EditorStyles.boldLabel);
            var activeSet = _settings.ActiveSceneSet;
            if (activeSet == null) { EditorGUILayout.HelpBox("No active scene set.", MessageType.Info); return; }
            foreach (var c in activeSet.combinations)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = c.IsValid;
                GUILayout.Label(c.DisplayName);
                GUI.enabled = true;
                if (c.IsValid && GUILayout.Button("Switch",
                        GUILayout.Width(60)))
                    SceneSwitcher.SwitchToCombination(c, activeSet);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFullDefines()
        {
            EditorGUILayout.LabelField("Script Defines", EditorStyles.boldLabel);
            foreach (var set in _settings.scriptDefinitionSets)
            {
                bool a = _settings.ActiveScriptDefinitionSet == set;
                EditorGUILayout.BeginHorizontal();
                if (a) { GUI.color = Color.green; GUILayout.Label("\u25CF", GUILayout.Width(20)); GUI.color = Color.white; }
                EditorGUILayout.LabelField(set.setName, EditorStyles.boldLabel);
                if (!a && GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    _settings.activeScriptDefinitionSetIndex =
                        _settings.scriptDefinitionSets.IndexOf(set);
                    _dirty = true;
                    ScriptDefinitionSwitcher.ApplySet(set);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFullBuildProfiles()
        {
            EditorGUILayout.LabelField("Build Profiles", EditorStyles.boldLabel);
            foreach (var bp in _settings.buildProfiles)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                GUIContent icon = PlatformIconUtility.GetPlatformIcon(
                    bp.buildConfiguration.platform);
                if (icon != null && icon.image != null)
                    GUILayout.Label(icon, GUILayout.Width(24),
                        GUILayout.Height(24));

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(bp.profileName,
                    EditorStyles.boldLabel);
                string prev = BuildNameResolver.Resolve(
                    bp.buildNameTemplate?.template ?? "{app-version}",
                    bp, _settings);
                EditorGUILayout.LabelField(prev, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("\u25B6 Build", GUILayout.Width(70),
                        GUILayout.Height(28)))
                    BuildExecutor.Build(bp);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string[] Concat(string first, string[] rest)
        {
            string[] r = new string[rest.Length + 1];
            r[0] = first; Array.Copy(rest, 0, r, 1, rest.Length);
            return r;
        }

        private void DrawCustomTabBar(ref int selectedTab, string[] tabNames)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isActive = selectedTab == i;

                Color oldBg = GUI.backgroundColor;
                Color oldContent = GUI.contentColor;

                if (isActive)
                {
                    GUI.backgroundColor = new Color(0.25f, 0.45f, 0.75f, 1f);
                    GUI.contentColor = Color.white;
                }
                else
                {
                    GUI.backgroundColor = Color.clear;
                    GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
                }

                GUIStyle tabStyle = new(GUI.skin.button)
                {
                    fixedHeight = 24f,
                    padding = new RectOffset(12, 12, 2, 2),
                    margin = new RectOffset(1, 1, 2, 0),
                    fontSize = 11,
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                    normal = { background = Texture2D.whiteTexture },
                    border = new RectOffset(0, 0, 0, 0)
                };

                Rect btnRect = GUILayoutUtility.GetRect(
                    new GUIContent(tabNames[i]), tabStyle,
                    GUILayout.ExpandWidth(false));

                if (isActive)
                {
                    Rect accentRect = new(
                        btnRect.x, btnRect.yMax - 2f,
                        btnRect.width, 2f);
                    EditorGUI.DrawRect(accentRect,
                        new Color(0.3f, 0.6f, 1f));
                }

                if (GUI.Button(btnRect, tabNames[i], tabStyle))
                    selectedTab = i;

                GUI.backgroundColor = oldBg;
                GUI.contentColor = oldContent;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
    }
}
