// ═══════════════════════════════════════════════════════════
// ── NoBuildBuildToolbarElement ────────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Right-side toolbar element: Build dropdown + Settings gear button.
    /// </summary>
    public sealed class NoBuildBuildToolbarElement : VisualElement
    {
        private const float DropdownWidth = 85f;
        private const float SmallButtonWidth = 22f;

        private readonly IMGUIContainer _imguiContainer;
        private GUIStyle _dropdownStyle;

        public NoBuildBuildToolbarElement()
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

            // Build dropdown
            GUIContent buildContent = EditorGUIUtility.IconContent("BuildSettings.Editor.Small");
            int count = settings.buildProfiles.Count;
            buildContent.text = count > 0 ? $" Build ({count})" : " Build";
            buildContent.tooltip = "Quick build with configured profiles";

            if (GUILayout.Button(buildContent, _dropdownStyle, GUILayout.Width(DropdownWidth + 10)))
            {
                UnityEditor.PopupWindow.Show(
                    GUILayoutUtility.GetLastRect(),
                    NoBuildDropdowns.CreateBuildDropdown(
                        settings,
                        profile => BuildExecutor.Build(profile)
                    )
                );
            }

            GUILayout.Space(2);

            // Settings gear
            GUIContent gearIcon = EditorGUIUtility.IconContent("_Popup");
            gearIcon.tooltip = "Open NoBuild Project Settings";
            if (GUILayout.Button(gearIcon, GUILayout.Width(SmallButtonWidth),
                    GUILayout.Height(SmallButtonWidth)))
            {
                SettingsService.OpenProjectSettings("Project/NoBuild");
            }

            // NoBuild window icon
            GUIContent windowIcon = EditorGUIUtility.IconContent(
                "UnityEditor.SceneHierarchyWindow");
            windowIcon.tooltip = "Open NoBuild Window";
            if (GUILayout.Button(windowIcon, GUILayout.Width(SmallButtonWidth),
                    GUILayout.Height(SmallButtonWidth)))
            {
                NoBuildWindow.ShowWindow();
            }

            // Separator
            GUILayout.Space(2);
            Color old = GUI.color;
            GUI.color = new Color(0.35f, 0.35f, 0.35f, 0.5f);
            GUILayout.Label("│", GUILayout.Width(8));
            GUI.color = old;
            GUILayout.Space(2);
        }
    }
}
