// ═══════════════════════════════════════════════════════════
// ── PlaceholderGuide ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Reusable popup that displays all registered build name placeholders
    /// with descriptions. Call <c>Show()</c> from any Editor GUI context.
    /// </summary>
    public static class PlaceholderGuide
    {
        private static readonly (string key, string description)[] BuiltIn =
        {
            ("{git-commit}",       "7-char short git commit hash"),
            ("{git-commit-full}",  "Full 40-char git commit hash"),
            ("{git-branch}",       "Current git branch name"),
            ("{app-version}",      "Application.version"),
            ("{app-bundle}",       "Android bundleVersionCode / iOS buildNumber"),
            ("{platform}",         "Build target (e.g., 'Android')"),
            ("{profile-name}",     "Name of this BuildProfile"),
            ("{scene-set}",        "Name of the associated SceneSet"),
            ("{script-defines}",   "Enabled defines, comma-separated"),
            ("{project-root}",     "Raw project root folder path"),
            ("{project-root-norm}","Project root path (slashes \u2192 underscores)"),
            ("{asset-folder}",     "Assets/ folder path"),
            ("{project-name}",     "Project folder name"),
            ("{date}",             "yyyy-MM-dd"),
            ("{time}",             "HHmmss"),
            ("{datetime}",         "yyyy-MM-dd_HHmmss"),
            ("{flags}",           "All flag definitions concatenated"),
            ("{flag-&lt;name&gt;}",   "Single flag by name (e.g., {flag-Cheats})"),
            ("{flag-&lt;N&gt;}",      "Single flag by index (e.g., {flag-0})"),
        };

        public static void Show(Rect activatorRect)
        {
            PopupWindow.Show(activatorRect, new GuideContent());
        }

        public static void Show(Rect activatorRect,
            Action<string> onPlaceholderClicked)
        {
            PopupWindow.Show(activatorRect,
                new GuideContent { OnPlaceholderClicked =
                    onPlaceholderClicked });
        }

        private sealed class GuideContent : PopupWindowContent
        {
            public Action<string> OnPlaceholderClicked;
            private Vector2 _scroll;
            private string _filter = "";
            private GUIStyle _hoverStyle;

            private GUIStyle HoverButtonStyle
            {
                get
                {
                    if (_hoverStyle == null)
                    {
                        _hoverStyle = new GUIStyle(
                            EditorStyles.boldLabel)
                        {
                            padding = new RectOffset(
                                4, 4, 3, 3),
                            hover = new GUIStyleState
                            {
                                textColor = Color.white,
                                background = MakeTex(
                                    1, 1,
                                    new Color(0.25f, 0.5f,
                                        0.85f, 0.5f))
                            }
                        };
                    }
                    return _hoverStyle;
                }
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

            public override Vector2 GetWindowSize() => new(380, 360);

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Build Name Placeholders", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Use these in build name and folder templates.", EditorStyles.miniLabel);
                GUILayout.Space(4);
                _filter = EditorGUILayout.TextField("Filter", _filter);
                GUILayout.Space(4);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                var filtered = string.IsNullOrEmpty(_filter)
                    ? BuiltIn
                    : BuiltIn.Where(x =>
                        x.key.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.description.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var (key, desc) in filtered)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    if (GUILayout.Button(key, HoverButtonStyle,
                            GUILayout.Width(150)))
                    {
                        OnPlaceholderClicked?.Invoke(key);
                        editorWindow.Close();
                    }
                    EditorGUILayout.LabelField(desc, EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
        }
    }
}
