// ═══════════════════════════════════════════════════════════
// ── NoBuildSceneSetToolbarElement ─────────────────────
// ═══════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Left-side toolbar: Scene Set dropdown + numbered combination shortcut buttons [1]..[9].
    /// </summary>
    public sealed class NoBuildSceneSetToolbarElement : VisualElement
    {
        private const float DropdownW = 110f;
        private const float BtnW = 22f;

        private readonly IMGUIContainer _imgui;
        private GUIStyle _ddStyle, _btnStyle;

        public NoBuildSceneSetToolbarElement()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;

            _imgui = new IMGUIContainer(OnIMGUI);
            _imgui.style.flexDirection = FlexDirection.Row;
            _imgui.style.alignItems = Align.Center;
            _imgui.style.flexShrink = 0;
            Add(_imgui);

            NoBuildToolbarState.RepaintRequested += () => _imgui.MarkDirtyRepaint();
        }

        private void EnsureStyles()
        {
            if (_ddStyle != null) return;
            _ddStyle = new GUIStyle("Dropdown")
            {
                fixedHeight = 22, alignment = TextAnchor.MiddleLeft,
                fontSize = 11, margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 0, 0, 0)
            };
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 22, fixedWidth = BtnW, fontSize = 10,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        private void OnIMGUI()
        {
            EnsureStyles();
            NoBuildSettings s = NoBuildToolbarState.GetSettings();
            if (s == null) return;

            Sep();

            // ── Scene Set dropdown ──
            string label = s.activeSceneSetIndex >= 0
                           && s.activeSceneSetIndex < s.sceneSets.Count
                ? s.sceneSets[s.activeSceneSetIndex].setName : "No Set";

            GUIContent dc = EditorGUIUtility.IconContent("SceneAsset Icon");
            dc.text = " " + Trunc(label, 12);
            dc.tooltip = "Active scene set: " + label;

            if (GUILayout.Button(dc, _ddStyle, GUILayout.Width(DropdownW)))
            {
                UnityEditor.PopupWindow.Show(
                    GUILayoutUtility.GetLastRect(),
                    NoBuildDropdowns.CreateSceneSetDropdown(
                        s,
                        set =>
                        {
                            int idx = s.sceneSets.IndexOf(set);
                            s.activeSceneSetIndex = idx;
                            EditorUtility.SetDirty(s);
                            SceneSwitcher.SwitchToSet(set);
                            NoBuildToolbarState.RequestRepaint();
                        },
                        combo => SceneSwitcher.SwitchToCombination(combo)
                    )
                );
            }

            GUILayout.Space(3);

            // ── Combination buttons [1]..[9] ──
            List<SceneCombination> combos = s.GetEnabledCombinations();

            for (int i = 0; i < 9; i++)
            {
                bool valid = i < combos.Count;
                SceneCombination c = valid ? combos[i] : null;
                string tip = valid ? "Switch to: " + c.DisplayName : "";

                GUI.enabled = valid;
                if (GUILayout.Button(
                        new GUIContent((i + 1).ToString(), tip),
                        _btnStyle, GUILayout.Width(BtnW)))
                {
                    SceneSwitcher.SwitchToCombination(c);
                }

                GUI.enabled = true;
            }

            Sep();
        }

        private static void Sep()
        {
            GUILayout.Space(2);
            Color old = GUI.color;
            GUI.color = new Color(0.35f, 0.35f, 0.35f, 0.5f);
            GUILayout.Label("\u2502", GUILayout.Width(8));
            GUI.color = old;
            GUILayout.Space(2);
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > max ? s.Substring(0, max - 1) + "\u2026" : s;
        }
    }
}
