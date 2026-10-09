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
            _button.text = label + (NoBuildDeviceOperations.IsBusy ? " · Busy ▼" : " ▼");
            _button.tooltip = label
                + "\nScenes: " + (NoBuildEditorState.ScenesMatch(settings?.ActiveSceneSet) ? "configured set is open" : "custom scene setup")
                + "\nDefines [" + EditorUserBuildSettings.selectedBuildTargetGroup + "]: "
                + (NoBuildEditorState.DefinesMatch(settings?.ActiveScriptDefinitionSet) ? "configured set is applied" : "custom / unapplied changes")
                + (NoBuildDeviceOperations.IsBusy ? "\n" + NoBuildDeviceOperations.Status : "")
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
        private string _search = "";
        private bool _focusSearch = true;
        private int _keyboardSelection;
        private string StateKey => "NoBuild.QuickMenu." + Application.dataPath;

        private sealed class QuickAction
        {
            public string Label;
            public bool Enabled;
            public Action<Rect> Execute;
        }

        public NoBuildCombinedPopup(NoBuildSettings s)
        {
            _settings = s;
            int folds = SessionState.GetInt(StateKey, 15);
            _foldA = (folds & 1) != 0; _foldB = (folds & 2) != 0;
            _foldC = (folds & 4) != 0; _foldD = (folds & 8) != 0;
        }

        public override void OnOpen() => NoBuildToolbarState.RepaintRequested += RepaintPopup;
        public override void OnClose()
        {
            NoBuildToolbarState.RepaintRequested -= RepaintPopup;
            SessionState.SetInt(StateKey, (_foldA ? 1 : 0) | (_foldB ? 2 : 0) | (_foldC ? 4 : 0) | (_foldD ? 8 : 0));
        }
        private void RepaintPopup() => editorWindow?.Repaint();

        public override Vector2 GetWindowSize() => new Vector2(PopupW, PopupH);

        public override void OnGUI(Rect rect)
        {
            EditorGuiLayout.DrawHeaderBox(
                "No Build",
                "Switch scenes and defines, then start a configured build."
            );
            HandleSearchKeyboard();
            GUI.SetNextControlName("NoBuild.QuickSearch");
            string search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (search != _search) { _search = search; _keyboardSelection = 0; _scroll = Vector2.zero; }
            if (_focusSearch && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl("NoBuild.QuickSearch");
                _focusSearch = false;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (!string.IsNullOrWhiteSpace(_search)) DrawSearchResults();
            else
            {
                DrawSceneSets(); GUILayout.Space(SectSpace);
                DrawCombos();   GUILayout.Space(SectSpace);
                DrawDefines();  GUILayout.Space(SectSpace);
                DrawBuild();
                if (_settings.sceneSets.Count + _settings.scriptDefinitionSets.Count + _settings.buildProfiles.Count == 0)
                    EditorGUILayout.HelpBox("Start by creating a scene set in Manage…", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
            if (NoBuildDeviceOperations.IsBusy)
            {
                EditorGUILayout.LabelField(NoBuildDeviceOperations.Status, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Cancel Device Operation")) NoBuildDeviceOperations.Cancel();
            }
            GUILayout.Space(FootSpace);
            if (GUILayout.Button("Manage..."))
            { SettingsService.OpenProjectSettings(NoBuildSettingsProvider.Path); editorWindow.Close(); }
        }

        private bool Matches(string name) => string.IsNullOrWhiteSpace(_search)
            || (name ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

        private List<QuickAction> GetSearchActions()
        {
            var actions = new List<QuickAction>();
            bool idle = !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
            for (int i = 0; i < _settings.sceneSets.Count; i++)
            {
                int index = i;
                SceneSet set = _settings.sceneSets[i];
                string label = "Scenes · " + set.setName;
                if (Matches(label)) actions.Add(new QuickAction
                {
                    Label = label, Enabled = idle,
                    Execute = _ => { if (NoBuildEditorState.OpenSet(_settings, index)) editorWindow.Close(); }
                });
            }
            SceneSet context = _settings.ActiveSceneSet;
            foreach (SceneCombination combination in context?.combinations ?? new List<SceneCombination>())
            {
                if (combination == null || !combination.enabled) continue;
                string label = "Open Scenes · " + combination.DisplayName;
                if (Matches(label)) actions.Add(new QuickAction
                {
                    Label = label, Enabled = idle && combination.ResolveScenes(context).Count > 0,
                    Execute = _ => { SceneSwitcher.SwitchToCombination(combination, context); editorWindow.Close(); }
                });
            }
            for (int i = 0; i < _settings.scriptDefinitionSets.Count; i++)
            {
                int index = i;
                string label = "Defines · " + _settings.scriptDefinitionSets[i].setName;
                if (Matches(label)) actions.Add(new QuickAction
                {
                    Label = label, Enabled = idle,
                    Execute = _ => { if (NoBuildEditorState.ApplyDefines(_settings, index)) editorWindow.Close(); }
                });
            }
            foreach (BuildProfile profile in _settings.buildProfiles)
            {
                string label = "Build · " + profile.profileName;
                if (Matches(label)) actions.Add(new QuickAction
                {
                    Label = label, Enabled = !NoBuildDeviceOperations.IsBusy,
                    Execute = rect => NoBuildBuildUI.ShowConfirmationFromPopup(rect, profile, _settings, editorWindow)
                });
            }
            return actions;
        }

        private void HandleSearchKeyboard()
        {
            if (string.IsNullOrWhiteSpace(_search) || Event.current.type != EventType.KeyDown) return;
            List<QuickAction> actions = GetSearchActions();
            if (Event.current.keyCode == KeyCode.Escape)
            {
                _search = ""; _keyboardSelection = 0;
                Event.current.Use();
            }
            else if (Event.current.keyCode == KeyCode.UpArrow || Event.current.keyCode == KeyCode.DownArrow)
            {
                _keyboardSelection = Mathf.Clamp(_keyboardSelection + (Event.current.keyCode == KeyCode.UpArrow ? -1 : 1), 0, Math.Max(0, actions.Count - 1));
                _scroll.y = _keyboardSelection * 28f;
                Event.current.Use();
                editorWindow.Repaint();
            }
            else if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && actions.Count > 0)
            {
                QuickAction action = actions[Mathf.Clamp(_keyboardSelection, 0, actions.Count - 1)];
                Event.current.Use();
                if (action.Enabled) action.Execute(new Rect(0, 0, PopupW, 25));
            }
        }

        private void DrawSearchResults()
        {
            List<QuickAction> actions = GetSearchActions();
            if (actions.Count == 0) EditorGUILayout.HelpBox("No matches. Try another name or clear the search.", MessageType.Info);
            for (int i = 0; i < actions.Count; i++)
            {
                QuickAction action = actions[i];
                using (new EditorGUI.DisabledScope(!action.Enabled))
                {
                    if (GUILayout.Button(new GUIContent((i == _keyboardSelection ? "› " : "  ") + action.Label, action.Label),
                        EditorStyles.miniButton, GUILayout.Height(26))) action.Execute(GUILayoutUtility.GetLastRect());
                }
            }
            EditorGUILayout.LabelField("↑/↓ Select · Enter Run · Esc Clear", EditorStyles.miniLabel);
        }

        private void DrawSceneSets()
        {
            int cnt = _settings.sceneSets.Count;
            _foldA = EditorGUILayout.Foldout(_foldA, "Scene Sets" + (cnt > Zero ? " (" + cnt + ")" : ""), true);
            if (!_foldA || cnt == Zero) return;
            for (int i = Zero; i < cnt; i++)
            {
                var s = _settings.sceneSets[i];
                if (!Matches(s.setName)) continue;
                bool a = NoBuildEditorState.ScenesMatch(s);
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.color = a
                    ? EditorGuiColors.ActiveGreen
                    : EditorGuiColors.InactiveGray;
                GUILayout.Label(a ? BulletOn : BulletOff, GUILayout.Width(BulletW));
                GUI.color = Color.white;
                if (GUILayout.Button(new GUIContent(EditorGuiStrings.Truncate(s.setName, LabelMax),
                    s.setName + "\n" + ShortcutManager.GetBinding(i, true)), EditorGuiStyles.HoverLabel))
                {
                    if (NoBuildEditorState.OpenSet(_settings, i))
                    {
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
                    if (NoBuildEditorState.OpenSet(_settings, i, play: true))
                    {
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
            var combos = active?.combinations ?? new List<SceneCombination>();
            _foldB = EditorGUILayout.Foldout(_foldB, "Open Scenes" + (combos.Count > Zero ? " (" + combos.Count + ")" : ""), true);
            if (!_foldB || active == null || combos.Count == Zero) return;
            for (int i = Zero; i < combos.Count; i++)
            {
                var c = combos[i];
                if (c == null || !c.enabled || !Matches(c.DisplayName)) continue;
                bool valid = c.ResolveScenes(active).Count > 0;
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
                using (new EditorGUI.DisabledScope(!valid || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                {
                    if (GUILayout.Button(new GUIContent(EditorGuiStrings.Truncate(c.DisplayName, LabelMax),
                        c.DisplayName + "\n" + ShortcutManager.GetBinding(i)), EditorGuiStyles.HoverLabel))
                    { SceneSwitcher.SwitchToCombination(c, active); editorWindow.Close(); }
                }
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
                if (!Matches(s.setName)) continue;
                bool a = NoBuildEditorState.DefinesMatch(s);
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.color = a
                    ? EditorGuiColors.ActiveGreen
                    : EditorGuiColors.InactiveGray;
                GUILayout.Label(a ? BulletOn : BulletOff, GUILayout.Width(BulletW));
                GUI.color = Color.white;
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button(new GUIContent(EditorGuiStrings.Truncate(s.setName, LabelMax),
                    s.setName + "\nApply to " + EditorUserBuildSettings.selectedBuildTargetGroup), EditorGuiStyles.HoverLabel))
                {
                    if (NoBuildEditorState.ApplyDefines(_settings, i)) editorWindow.Close();
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
                if (!Matches(bp.profileName)) continue;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                GUIContent platIcon = PlatformIconUtility.GetPlatformIcon(
                    bp.buildConfiguration.platform);
                if (platIcon != null && platIcon.image != null)
                    GUILayout.Label(platIcon, GUILayout.Width(20),
                        GUILayout.Height(20));

                EditorGUILayout.BeginVertical();
                GUILayout.Label(new GUIContent(EditorGuiStrings.Truncate(bp.profileName, LabelMax), bp.profileName), EditorStyles.boldLabel);
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
                { NoBuildBuildUI.ShowConfirmationFromPopup(GUILayoutUtility.GetLastRect(), bp, _settings, editorWindow); }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(2);
                if (bp.buildConfiguration.platform == BuildTarget.Android)
                {
                    GUI.backgroundColor = EditorGuiColors.RunBlue;
                    if (GUILayout.Button(
                            PlatformIconUtility.BuildAndRunActionIcon,
                            GUILayout.Width(105),
                            GUILayout.Height(BtnH)
                        ))
                    {
                        NoBuildBuildUI.ShowConfirmationFromPopup(GUILayoutUtility.GetLastRect(), bp, _settings, editorWindow);
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

    }
}

