// ═══════════════════════════════════════════════════════════
// ── NoBuildDropdowns ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    internal static class NoBuildDropdowns
    {
        private const float HeaderH = 46f;
        private const float RowH = 28f;
        private const float SectionH = 18f;
        private const float FooterH = 30f;
        private const float Pad = 10f;
        private const float MaxH = 420f;
        private const float MinW = 400f;

        private static float ClampH(float h) => Mathf.Min(MaxH, Mathf.Max(130f, h));

        // ── Shared Hover Style ──────────────────────
        private static GUIStyle _hoverLabelStyle;
        private static bool _hoverStyleBuilt;

        private static GUIStyle PopupRowStyle
        {
            get
            {
                if (!_hoverStyleBuilt) BuildHoverStyle();
                return _hoverLabelStyle;
            }
        }

        private static void BuildHoverStyle()
        {
            _hoverStyleBuilt = true;
            _hoverLabelStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 3, 3),
                hover = new GUIStyleState
                {
                    textColor = Color.white,
                    background = MakeTex(1, 1,
                        new Color(0.25f, 0.5f, 0.85f, 0.5f))
                }
            };
        }

        private static Texture2D MakeTex(
            int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D tex = new(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        public static PopupWindowContent CreateSceneSetDropdown(
            NoBuildSettings s,
            Action<SceneSet> onSet,
            Action<SceneCombination> onCombo)
        {
            return new SceneDropdownContent(s, onSet, onCombo);
        }

        public static PopupWindowContent CreateScriptDefineDropdown(
            NoBuildSettings s, Action<ScriptDefinitionSet> onSet)
        {
            return new DefineDropdownContent(s, onSet);
        }

        public static PopupWindowContent CreateBuildDropdown(
            NoBuildSettings s, Action<BuildProfile> onBuild)
        {
            return new BuildDropdownContent(s, onBuild);
        }

        public static PopupWindowContent CreatePlatformGridDropdown(
            BuildTarget currentPlatform,
            Action<BuildTarget> onPlatformSelected)
        {
            return new PlatformGridContent(currentPlatform, onPlatformSelected);
        }

        public static PopupWindowContent CreateDeviceSelectPopup(
            Action<BuildExecutor.DeviceOption, string> onSelected)
        {
            return new DeviceSelectContent(onSelected);
        }

        // ══════════════════════════════════════════════════
        // ── Scene Dropdown (Sets + Combinations)
        // ══════════════════════════════════════════════════

        private sealed class SceneDropdownContent : PopupWindowContent
        {
            private readonly NoBuildSettings _s;
            private readonly Action<SceneSet> _onSet;
            private readonly Action<SceneCombination> _onCombo;
            private Vector2 _scroll;

            public SceneDropdownContent(NoBuildSettings s,
                Action<SceneSet> onSet, Action<SceneCombination> onCombo)
            {
                _s = s; _onSet = onSet; _onCombo = onCombo;
            }

            public override Vector2 GetWindowSize()
            {
                int sets = _s.sceneSets.Count;
                SceneSet active = _s.ActiveSceneSet;
                int combos = active?.combinations?.Count ?? 0;
                float h = HeaderH + (sets > 0 ? SectionH + sets * RowH : 0)
                          + (combos > 0 ? SectionH + combos * RowH + 8 : 0)
                          + FooterH + Pad;
                return new Vector2(MinW, ClampH(h));
            }

            public override void OnGUI(Rect rect)
            {
                DrawSectionHeader("Scene Sets",
                    "Switch all scenes at once.");

                if (_s.sceneSets.Count == 0)
                {
                    EditorGUILayout.LabelField("  (none)",
                        EditorStyles.miniLabel);
                }
                else
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);

                    for (int i = 0; i < _s.sceneSets.Count; i++)
                    {
                        SceneSet set = _s.sceneSets[i];
                        bool isActive = _s.activeSceneSetIndex == i;

                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                        GUI.color = isActive ? Color.green : Color.gray;
                        GUILayout.Label(isActive ? "\u25C9" : "\u25CB",
                            GUILayout.Width(20));
                        GUI.color = Color.white;

                        if (GUILayout.Button(Trunc(set.setName, 32),
                                PopupRowStyle))
                        {
                            _s.activeSceneSetIndex = i;
                            EditorUtility.SetDirty(_s);
                            _onSet?.Invoke(set);
                            editorWindow.Close();
                        }

                        int valid = set.scenes.Count(sl => sl.enabled && sl.IsValid);

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                GUILayout.Space(6);

                // ── Open Scenes (Combinations from active set) ──
                SceneSet activeSet = _s.ActiveSceneSet;
                List<SceneCombination> combos = activeSet?.combinations;

                DrawSectionHeader("Open Scenes",
                    activeSet != null
                        ? $"Quick-switch for '{activeSet.setName}' [1]..[9]."
                        : "Quick-switch shortcuts [1]..[9].");

                if (activeSet == null || combos == null || combos.Count == 0)
                {
                    EditorGUILayout.LabelField("  (no active set or no combinations)",
                        EditorStyles.miniLabel);
                }
                else
                {
                    for (int i = 0; i < combos.Count; i++)
                    {
                        SceneCombination c = combos[i];
                        bool valid = c.IsValid;

                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                        GUI.enabled = valid;
                        GUI.color = valid ? Color.white : Color.gray;
                        string num = (i + 1).ToString();
                        GUILayout.Label(
                            num.Length == 1 ? " " + num : num,
                            EditorStyles.boldLabel,
                            GUILayout.Width(20)
                        );
                        GUI.color = Color.white;
                        GUI.enabled = true;

                        string label = c.DisplayName;
                        if (!valid) label += " (invalid)";

                        if (valid)
                        {
                            if (GUILayout.Button(Trunc(label, 38), PopupRowStyle))
                            {
                                _onCombo?.Invoke(c);
                                editorWindow.Close();
                            }
                        }
                        else
                        {
                            GUILayout.Label(Trunc(label, 38), EditorStyles.label);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(4);
                if (GUILayout.Button("Manage..."))
                {
                    SettingsService.OpenProjectSettings(NoBuildSettingsProvider.Path);
                    editorWindow.Close();
                }
            }

            private static void DrawSectionHeader(string title, string sub)
            {
                GUILayout.Space(2);
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(sub, EditorStyles.miniLabel);
                GUILayout.Space(2);
            }
        }

        // ══════════════════════════════════════════════════
        // ── Define Dropdown
        // ══════════════════════════════════════════════════

        private sealed class DefineDropdownContent : PopupWindowContent
        {
            private readonly NoBuildSettings _s;
            private readonly Action<ScriptDefinitionSet> _onSet;
            private Vector2 _scroll;

            public DefineDropdownContent(NoBuildSettings s,
                Action<ScriptDefinitionSet> onSet)
            { _s = s; _onSet = onSet; }

            public override Vector2 GetWindowSize()
            {
                int n = _s.scriptDefinitionSets.Count;
                return new Vector2(MinW, ClampH(HeaderH + n * RowH + FooterH + Pad));
            }

            public override void OnGUI(Rect rect)
            {
                DrawHdr("Script Defines", "Toggle scripting define symbols per set.");

                if (_s.scriptDefinitionSets.Count == 0)
                {
                    DrawEmpty("No define sets.", "Create one in Project Settings \u2192 NoBuild.");
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                for (int i = 0; i < _s.scriptDefinitionSets.Count; i++)
                {
                    var set = _s.scriptDefinitionSets[i];
                    bool active = _s.activeScriptDefinitionSetIndex == i;

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUI.color = active ? Color.green : Color.gray;
                    GUILayout.Label(active ? "\u25C9" : "\u25CB",
                        GUILayout.Width(20));
                    GUI.color = Color.white;
                    if (GUILayout.Button(Trunc(set.setName, 30),
                            PopupRowStyle))
                    {
                        _s.activeScriptDefinitionSetIndex = i;
                        EditorUtility.SetDirty(_s);
                        ScriptDefinitionSwitcher.ApplySet(set);
                        _onSet?.Invoke(set);
                        editorWindow.Close();
                    }

                    int n = set.slots?.Count(sl => sl.enabled) ?? 0;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(4);
                if (GUILayout.Button("Manage..."))
                {
                    SettingsService.OpenProjectSettings(NoBuildSettingsProvider.Path);
                    editorWindow.Close();
                }
            }
        }

        // ══════════════════════════════════════════════════
        // ── Build Dropdown
        // ══════════════════════════════════════════════════

        private sealed class BuildDropdownContent : PopupWindowContent
        {
            private readonly NoBuildSettings _s;
            private readonly Action<BuildProfile> _onBuild;
            private Vector2 _scroll;

            public BuildDropdownContent(NoBuildSettings s,
                Action<BuildProfile> onBuild)
            { _s = s; _onBuild = onBuild; }

            public override Vector2 GetWindowSize()
            {
                if (_s.buildProfiles.Count == 0) return new Vector2(330, 130);
                int groups = _s.buildProfiles
                    .Select(p => p.buildConfiguration.platform).Distinct().Count();
                float h = HeaderH + groups * 22f
                    + _s.buildProfiles.Count * 48f + FooterH + Pad;
                return new Vector2(330, ClampH(h));
            }

            public override void OnGUI(Rect rect)
            {
                DrawHdr("Build Profiles",
                    "One-click builds with configured settings.");

                if (_s.buildProfiles.Count == 0)
                {
                    DrawEmpty("No build profiles.",
                        "Create one in Project Settings \u2192 NoBuild.");
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                var groups = _s.buildProfiles
                    .Select((p, i) => (p, i))
                    .GroupBy(x => x.p.buildConfiguration.platform)
                    .OrderBy(g => g.Key.ToString());

                foreach (var g in groups)
                {
                    EditorGUILayout.LabelField(
                        "  " + ObjectNames.NicifyVariableName(g.Key.ToString()),
                        EditorStyles.boldLabel);

                    foreach (var (p, _) in g)
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                        GUIContent icon = PlatformIconUtility.GetPlatformIcon(
                            p.buildConfiguration.platform);
                        if (icon != null && icon.image != null)
                            GUILayout.Label(icon, GUILayout.Width(22), GUILayout.Height(22));

                        EditorGUILayout.BeginVertical();
                        GUILayout.Label(Trunc(p.profileName, 30),
                            EditorStyles.boldLabel);
                        string prev = BuildNameResolver.Resolve(
                            p.buildNameTemplate?.template ?? "{app-version}",
                            p, _s);
                        GUILayout.Label(Trunc(prev, 40),
                            EditorStyles.miniLabel);
                        EditorGUILayout.EndVertical();
                        GUILayout.FlexibleSpace();
                        Color old = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                        if (GUILayout.Button("Build", GUILayout.Width(52),
                                GUILayout.Height(22)))
                        {
                            _onBuild?.Invoke(p);
                            editorWindow.Close();
                        }

                        GUI.backgroundColor = old;
                        EditorGUILayout.EndHorizontal();
                    }

                    GUILayout.Space(2);
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(4);
                if (GUILayout.Button("Manage..."))
                {
                    SettingsService.OpenProjectSettings(NoBuildSettingsProvider.Path);
                    editorWindow.Close();
                }
            }
        }

        // ══════════════════════════════════════════════════
        // ── Platform Grid Dropdown
        // ══════════════════════════════════════════════════

        private sealed class PlatformGridContent : PopupWindowContent
        {
            private const int Columns = 3;
            private const float TileWidth = 96f;
            private const float TileHeight = 86f;
            private const float IconSize = 48f;
            private const float TilePad = 8f;

            private readonly BuildTarget _currentPlatform;
            private readonly Action<BuildTarget> _onPlatformSelected;
            private readonly BuildTarget[] _platforms;

            public PlatformGridContent(BuildTarget currentPlatform,
                Action<BuildTarget> onPlatformSelected)
            {
                _currentPlatform = currentPlatform;
                _onPlatformSelected = onPlatformSelected;
                _platforms = PlatformIconUtility.AvailablePlatforms;
            }

            public override Vector2 GetWindowSize()
            {
                int rows = Mathf.CeilToInt(
                    (float)_platforms.Length / Columns);
                float width = Columns * (TileWidth + TilePad) + TilePad * 2f;
                float height = rows * (TileHeight + TilePad) + TilePad * 2f
                    + 28f; // header
                return new Vector2(width, Mathf.Min(height, 400f));
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Select Platform",
                    EditorStyles.boldLabel);
                GUILayout.Space(4);

                for (int i = 0; i < _platforms.Length; i += Columns)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = 0;
                        j < Columns && i + j < _platforms.Length;
                        j++)
                    {
                        BuildTarget platform = _platforms[i + j];
                        bool isSelected = platform == _currentPlatform;
                        DrawPlatformTile(platform, isSelected);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            private void DrawPlatformTile(
                BuildTarget platform, bool isSelected)
            {
                GUIContent icon =
                    PlatformIconUtility.GetPlatformIcon(platform);
                string displayName =
                    PlatformIconUtility.GetPlatformDisplayName(platform);

                Rect bgRect = GUILayoutUtility.GetRect(
                    TileWidth, TileHeight);

                // Hover highlight
                bool isHovered = bgRect.Contains(
                    Event.current.mousePosition);
                if (!isSelected && isHovered)
                {
                    EditorGUI.DrawRect(bgRect,
                        new Color(0.35f, 0.5f, 0.75f,
                            0.3f));
                }

                // Selection highlight
                if (isSelected)
                {
                    EditorGUI.DrawRect(bgRect,
                        new Color(0.2f, 0.4f, 0.7f, 0.45f));
                }

                // Icon centered in the tile
                if (icon != null && icon.image != null)
                {
                    Rect iconRect = new(
                        bgRect.x + (TileWidth - IconSize) / 2f,
                        bgRect.y + 6f,
                        IconSize, IconSize);
                    GUI.DrawTexture(iconRect,
                        icon.image, ScaleMode.ScaleToFit);
                }

                // Label below the icon
                Rect labelRect = new(
                    bgRect.x + 4f,
                    bgRect.y + IconSize + 8f,
                    TileWidth - 8f, 18f);
                GUIStyle labelStyle = new(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    fontStyle = isSelected
                        ? FontStyle.Bold
                        : FontStyle.Normal,
                    wordWrap = true
                };
                GUI.Label(labelRect, displayName, labelStyle);

                // Click detection on the whole tile
                if (Event.current.type == EventType.MouseDown
                    && bgRect.Contains(
                        Event.current.mousePosition))
                {
                    _onPlatformSelected?.Invoke(platform);
                    editorWindow.Close();
                    Event.current.Use();
                }
            }
        }

        // ══════════════════════════════════════════════════
        // ── Device Select Popup (Build & Run)
        // ══════════════════════════════════════════════════

        private sealed class DeviceSelectContent : PopupWindowContent
        {
            private readonly Action<
                BuildExecutor.DeviceOption, string> _onSelected;
            private List<AdbDeviceInfo> _devices;

            public DeviceSelectContent(
                Action<BuildExecutor.DeviceOption, string>
                    onSelected)
            {
                _onSelected = onSelected;
                _devices = AdbUtility.GetDevices();
            }

            public override Vector2 GetWindowSize()
            {
                int rows = 2 // First Device + All Devices
                    + _devices.Count;
                float h = HeaderH + rows * RowH + FooterH + Pad;
                return new Vector2(MinW, ClampH(h));
            }

            public override void OnGUI(Rect rect)
            {
                DrawHdr("Build & Run", "Select target device(s).");

                if (_devices.Count == 0)
                {
                    DrawEmpty("No devices connected.", "Connect via USB or start an emulator.");
                    return;
                }

                // ── First Device ──
                EditorGUILayout.BeginHorizontal(
                    EditorStyles.helpBox);
                if (GUILayout.Button("First Device", PopupRowStyle))
                {
                    _onSelected?.Invoke(BuildExecutor.DeviceOption.FirstDevice, null);
                    editorWindow.Close();
                }
                EditorGUILayout.EndHorizontal();

                // ── All Devices ──
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Button($"All Devices [{_devices.Count} device(s)]", PopupRowStyle))
                {
                    _onSelected?.Invoke(BuildExecutor.DeviceOption.AllDevices, null);
                    editorWindow.Close();
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);

                // ── Individual Devices ──
                EditorGUILayout.LabelField("Specific Device", EditorStyles.miniLabel);
                for (int i = 0; i < _devices.Count; i++)
                {
                    var d = _devices[i];
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    if (GUILayout.Button(Trunc(d.DisplayName, 40), PopupRowStyle))
                    {
                        _onSelected?.Invoke(BuildExecutor.DeviceOption.SpecificDevice, d.Serial);
                        editorWindow.Close();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        // ══════════════════════════════════════════════════
        // ── Helpers
        // ══════════════════════════════════════════════════

        private static void DrawHdr(string t, string s)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(t, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(s, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private static void DrawEmpty(string m, string s)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(m, EditorStyles.centeredGreyMiniLabel);
            GUILayout.Label(s, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > max ? s.Substring(0, max - 1) + "\u2026" : s;
        }
    }
}
