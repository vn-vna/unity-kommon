// ═══════════════════════════════════════════════════════════
// ── NoBuildDefineSetToolbarElement ────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Right-side toolbar element: Script Define Set dropdown.
    /// </summary>
    public sealed class NoBuildDefineSetToolbarElement : VisualElement
    {
        private const float DropdownWidth = 90f;

        private readonly IMGUIContainer _imguiContainer;
        private GUIStyle _dropdownStyle;

        public NoBuildDefineSetToolbarElement()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;

            _imguiContainer = new IMGUIContainer(OnIMGUI);
            _imguiContainer.style.flexDirection = FlexDirection.Row;
            _imguiContainer.style.alignItems = Align.Center;
            _imguiContainer.style.flexShrink = 0;
            Add(_imguiContainer);

            NoBuildToolbarState.RepaintRequested += () => _imguiContainer.MarkDirtyRepaint();
        }

        private void EnsureStyles()
        {
            if (_dropdownStyle != null) return;
            _dropdownStyle = new GUIStyle("Dropdown")
            {
                fixedHeight = 22,
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 0, 0, 0)
            };
        }

        private void OnIMGUI()
        {
            EnsureStyles();

            NoBuildSettings settings = NoBuildToolbarState.GetSettings();
            if (settings == null) return;

            string activeLabel = settings.activeScriptDefinitionSetIndex >= 0
                                 && settings.activeScriptDefinitionSetIndex
                                 < settings.scriptDefinitionSets.Count
                ? settings.scriptDefinitionSets[settings.activeScriptDefinitionSetIndex].setName
                : "Defines";

            GUIContent content = new GUIContent(
                " " + Truncate(activeLabel, 10),
                $"Active define set: {activeLabel}"
            );

            if (GUILayout.Button(content, _dropdownStyle, GUILayout.Width(DropdownWidth)))
            {
                UnityEditor.PopupWindow.Show(
                    GUILayoutUtility.GetLastRect(),
                    NoBuildDropdowns.CreateScriptDefineDropdown(
                        settings,
                        set =>
                        {
                            int idx = settings.scriptDefinitionSets.IndexOf(set);
                            settings.activeScriptDefinitionSetIndex = idx;
                            EditorUtility.SetDirty(settings);
                            ScriptDefinitionSwitcher.ApplySet(set);
                            NoBuildToolbarState.RequestRepaint();
                        }
                    )
                );
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > max ? s.Substring(0, max - 1) + "\u2026" : s;
        }
    }
}
