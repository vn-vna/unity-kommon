using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Scheherazade.Common.Editor;
using Com.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    [InitializeOnLoad]
    internal static class NoBuildToolbarBootstrapper
    {
        private const int MaxAttempts = 300;
        private const string RightZone = "ToolbarZoneRightAlign";
        private const string ElementName =
            "Scheherazade.NoBuild.Toolbar";
        private static int _attempts;
        private static bool _done;

        static NoBuildToolbarBootstrapper()
        {
            EditorApplication.update -= TryInit;
            EditorApplication.update += TryInit;
        }

        private static void TryInit()
        {
            if (_done) return;
            _attempts++;
            try
            {
                Type t = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
                if (t == null) { Fail("Type not found"); return; }
                var objs = Resources.FindObjectsOfTypeAll(t);
                if (objs == null || objs.Length == 0) { if (_attempts >= MaxAttempts) Fail("Instance not found"); return; }
                var rf = t.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rf == null) { Fail("m_Root null"); return; }
                var root = rf.GetValue(objs[0]) as VisualElement;
                if (root == null) { Fail("Root null"); return; }
                VisualElement zone = Find(root, RightZone);
                if (zone == null) { if (_attempts >= MaxAttempts) Fail("Zone not found"); return; }
                VisualElement existing = zone.Q(
                    ElementName);
                existing?.RemoveFromHierarchy();

                var el = new NoBuildToolbarElement
                {
                    name = ElementName
                };
                zone.Insert(0, el);
                _done = true; EditorApplication.update -= TryInit;
                Debug.Log("[NoBuild] Toolbar injected.");
            }
            catch (Exception e) { if (_attempts >= MaxAttempts) Fail(e.Message); }
        }

        private static VisualElement Find(VisualElement e, string n)
        {
            if (e.name == n) return e;
            foreach (var c in e.Children()) { var r = Find(c, n); if (r != null) return r; }
            return null;
        }

        private static void Fail(string r)
        {
            Debug.LogWarning("[NoBuild] " + r + " Toolbar disabled.");
            EditorApplication.update -= TryInit;
        }
    }

    public sealed class NoBuildToolbarElement : VisualElement
    {
        private readonly Button _button;

        public NoBuildToolbarElement()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;
            style.marginLeft = 4;
            style.marginRight = 4;

            _button = new Button(OnClick)
            {
                tooltip = "NoBuild quick-switch menu"
            };
            _button.AddToClassList("unity-toolbar-button");
            _button.AddToClassList(
                "unity-editor-toolbar-element");
            _button.style.height = 22;
            _button.style.fontSize = 11;
            _button.style.paddingLeft = 8;
            _button.style.paddingRight = 8;
            Add(_button);

            RegisterCallback<AttachToPanelEvent>(
                _ => HandleAttached());
            RegisterCallback<DetachFromPanelEvent>(
                _ => HandleDetached());
            RefreshLabel();
        }

        private void HandleAttached()
        {
            NoBuildToolbarState.RepaintRequested -=
                RefreshLabel;
            NoBuildToolbarState.RepaintRequested +=
                RefreshLabel;
            RefreshLabel();
        }

        private void HandleDetached()
        {
            NoBuildToolbarState.RepaintRequested -=
                RefreshLabel;
        }

        private void RefreshLabel()
        {
            NoBuildSettings settings =
                NoBuildResourceUtility.GetSettings();
            string label =
                NoBuildToolbarLabelResolver.Resolve(settings);
            _button.text = label + " ▼";
            _button.tooltip = label
                + "\nOpen NoBuild quick-switch menu";
        }

        private void OnClick()
        {
            NoBuildSettings s = NoBuildToolbarState.GetSettings();
            if (s == null) return;
            CustomPopupDropdown.Show(
                new Rect(worldBound.x, worldBound.yMax, 0, 0),
                new NoBuildCombinedPopup(s)
            );
        }
    }

    internal sealed class NoBuildCombinedPopup : PopupWindowContent
    {
        private const float PopupW = 310f;
        private const float PopupH = 440f;
        private const float BulletW = 20f;
        private const float BadgeW = 22f;
        private const float BadgeH = 18f;
        private const float CountW = 65f;
        private const float DefCountW = 40f;
        private const float BtnW = 50f;
        private const float BtnH = 22f;
        private const float SectSpace = 6f;
        private const float FootSpace = 4f;
        private const int LabelMax = 28;
        private const int PrevMax = 35;
        private const int Zero = 0;
        private const int One = 1;
        private static readonly string BulletOn = "\u25C9";
        private static readonly string BulletOff = "\u25CB";

        private readonly NoBuildSettings _settings;
        private bool _foldA = true;
        private bool _foldB = true;
        private bool _foldC = true;
        private bool _foldD = true;
        private Vector2 _scroll;

        public NoBuildCombinedPopup(NoBuildSettings s) { _settings = s; }

        public override Vector2 GetWindowSize() => new Vector2(PopupW, PopupH);

        public override void OnGUI(Rect rect)
        {
            EditorGuiLayout.DrawHeaderBox(
                "No Build",
                "Switch scenes and defines, then start a configured build."
            );
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSceneSets(); GUILayout.Space(SectSpace);
            DrawCombos();   GUILayout.Space(SectSpace);
            DrawDefines();  GUILayout.Space(SectSpace);
            DrawBuild();
            EditorGUILayout.EndScrollView();
            GUILayout.Space(FootSpace);
            if (GUILayout.Button("Manage..."))
            { SettingsService.OpenProjectSettings(NoBuildSettingsProvider.Path); editorWindow.Close(); }
        }

        private void DrawSceneSets()
        {
            int cnt = _settings.sceneSets.Count;
            _foldA = EditorGUILayout.Foldout(_foldA, "Scene Sets" + (cnt > Zero ? " (" + cnt + ")" : ""), true);
            if (!_foldA || cnt == Zero) return;
            for (int i = Zero; i < cnt; i++)
            {
                var s = _settings.sceneSets[i];
                bool a = _settings.activeSceneSetIndex == i;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.color = a
                    ? EditorGuiColors.ActiveGreen
                    : EditorGuiColors.InactiveGray;
                GUILayout.Label(a ? BulletOn : BulletOff, GUILayout.Width(BulletW));
                GUI.color = Color.white;
                if (GUILayout.Button(EditorGuiStrings.Truncate(s.setName, LabelMax), EditorGuiStyles.HoverLabel))
                {
                    if (SceneSwitcher.SwitchToSet(s))
                    {
                        _settings.activeSceneSetIndex = i;
                        EditorUtility.SetDirty(_settings);
                        NoBuildToolbarState.RequestRepaint();
                        editorWindow.Close();
                    }
                }

                EditorGUI.BeginDisabledGroup(
                    EditorApplication.isPlayingOrWillChangePlaymode);
                if (GUILayout.Button(
                        new GUIContent(
                            "▶",
                            $"Play from scene set '{s.setName}'"),
                        GUILayout.Width(24),
                        GUILayout.Height(BtnH)))
                {
                    if (SceneSwitcher.PlayFromSet(s))
                    {
                        _settings.activeSceneSetIndex = i;
                        EditorUtility.SetDirty(_settings);
                        NoBuildToolbarState.RequestRepaint();
                        editorWindow.Close();
                    }
                }
                EditorGUI.EndDisabledGroup();

                int n = s.scenes.Count(
                    slot => slot != null
                        && slot.enabled
                        && slot.IsValid);
                GUILayout.Label(n + " scenes", EditorStyles.miniLabel, GUILayout.Width(CountW));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawCombos()
        {
            var active = _settings.ActiveSceneSet;
            var combos = active?.GetEnabledCombinations() ?? new List<SceneCombination>();
            _foldB = EditorGUILayout.Foldout(_foldB, "Open Scenes" + (combos.Count > Zero ? " (" + combos.Count + ")" : ""), true);
            if (!_foldB || active == null || combos.Count == Zero) return;
            for (int i = Zero; i < combos.Count; i++)
            {
                var c = combos[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                Color previousBadgeColor = GUI.color;
                GUI.color = EditorGuiColors.InactiveGray;
                GUILayout.Label(
                    (i + One).ToString(),
                    EditorGuiStyles.Badge,
                    GUILayout.Width(BadgeW),
                    GUILayout.Height(BadgeH)
                );
                GUI.color = previousBadgeColor;
                if (GUILayout.Button(EditorGuiStrings.Truncate(c.DisplayName, LabelMax), EditorGuiStyles.HoverLabel))
                { SceneSwitcher.SwitchToCombination(c, active); editorWindow.Close(); }
                int sc = c.sceneReferences?.Count(r => r.enabled && r.IsValid) ?? Zero;
                GUILayout.Label(sc + " sc", EditorStyles.miniLabel, GUILayout.Width(35));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawDefines()
        {
            int cnt = _settings.scriptDefinitionSets.Count;
            _foldC = EditorGUILayout.Foldout(_foldC, "Script Defines" + (cnt > Zero ? " (" + cnt + ")" : ""), true);
            if (!_foldC || cnt == Zero) return;
            for (int i = Zero; i < cnt; i++)
            {
                var s = _settings.scriptDefinitionSets[i];
                bool a = _settings.activeScriptDefinitionSetIndex == i;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.color = a
                    ? EditorGuiColors.ActiveGreen
                    : EditorGuiColors.InactiveGray;
                GUILayout.Label(a ? BulletOn : BulletOff, GUILayout.Width(BulletW));
                GUI.color = Color.white;
                if (GUILayout.Button(EditorGuiStrings.Truncate(s.setName, LabelMax), EditorGuiStyles.HoverLabel))
                {
                    _settings.activeScriptDefinitionSetIndex = i;
                    EditorUtility.SetDirty(_settings);
                    ScriptDefinitionSwitcher.ApplySet(s);
                    editorWindow.Close();
                }
                int n = s.slots?.Count(
                    slot => slot != null && slot.enabled) ?? Zero;
                GUILayout.Label(n + " on", EditorStyles.miniLabel, GUILayout.Width(DefCountW));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawBuild()
        {
            int cnt = _settings.buildProfiles.Count;
            _foldD = EditorGUILayout.Foldout(_foldD, "Build" + (cnt > Zero ? " (" + cnt + ")" : ""), true);
            if (!_foldD || cnt == Zero) return;
            foreach (var bp in _settings.buildProfiles)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                GUIContent platIcon = PlatformIconUtility.GetPlatformIcon(
                    bp.buildConfiguration.platform);
                if (platIcon != null && platIcon.image != null)
                    GUILayout.Label(platIcon, GUILayout.Width(20),
                        GUILayout.Height(20));

                EditorGUILayout.BeginVertical();
                GUILayout.Label(EditorGuiStrings.Truncate(bp.profileName, LabelMax), EditorStyles.boldLabel);
                string prev = BuildNameResolver.Resolve(
                    bp.buildNameTemplate?.template ?? "{app-version}", bp, _settings);
                GUILayout.Label(EditorGuiStrings.Truncate(prev, PrevMax), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = EditorGuiColors.BuildGreen;
                if (GUILayout.Button(
                        PlatformIconUtility.BuildActionIcon,
                        GUILayout.Width(BtnW),
                        GUILayout.Height(BtnH)
                    ))
                { BuildExecutor.Build(bp); editorWindow.Close(); }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(2);
                if (bp.buildConfiguration.platform == BuildTarget.Android)
                {
                    GUI.backgroundColor = EditorGuiColors.RunBlue;
                    if (GUILayout.Button(
                            PlatformIconUtility.RunActionIcon,
                            GUILayout.Width(34),
                            GUILayout.Height(BtnH)
                        ))
                    {
                        CustomPopupDropdown.ShowLastRect(
                            NoBuildDropdowns.CreateDeviceSelectPopup(
                                (option, serial) =>
                                {
                                    BuildExecutor
                                        .BuildAndRunWithOptions(
                                            bp, option,
                                            serial);
                                }
                            )
                        );
                        editorWindow.Close();
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

    }
}

