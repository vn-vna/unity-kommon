// ═══════════════════════════════════════════════════════════
// ── PlaceholderGuide ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Com.Scheherazade.Common.Editor;
using Com.Scheherazade.Common.Editor.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
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
        };

        public static void Show(Rect activatorRect)
        {
            CustomPopupDropdown.Show(
                activatorRect,
                new GuideContent()
            );
        }

        public static void Show(Rect activatorRect,
            Action<string> onPlaceholderClicked, NoBuildSettings settings = null)
        {
            CustomPopupDropdown.Show(
                activatorRect,
                new GuideContent
                {
                    OnPlaceholderClicked = onPlaceholderClicked,
                    Settings = settings
                }
            );
        }

        private sealed class GuideContent : PopupWindowContent
        {
            public Action<string> OnPlaceholderClicked;
            public NoBuildSettings Settings;
            private Vector2 _scroll;
            private string _filter = "";

            public override Vector2 GetWindowSize() => new(380, 360);

            public override void OnGUI(Rect rect)
            {
                EditorGuiLayout.DrawSectionHeader(
                    "Build Name Placeholders",
                    "Use these in build name and folder templates.");
                _filter = EditorGUILayout.TextField("Filter", _filter);
                GUILayout.Space(4);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                IEnumerable<(string key, string description)> entries = BuiltIn;
                if (Settings?.flagDefinitions != null)
                {
                    entries = entries.Concat(Settings.flagDefinitions
                        .Where(flag => flag != null && !string.IsNullOrWhiteSpace(flag.id))
                        .Select(flag => ("{flag-" + flag.id + "}", "Flag ID: " + flag.id + " · " + flag.name)));
                }
                entries = entries.Concat(BuildNameResolver.Resolvers.Keys
                    .Where(key => !BuiltIn.Any(entry => entry.key == "{" + key + "}"))
                    .Select(key => ("{" + key + "}", "Registered placeholder")));
                var filtered = string.IsNullOrEmpty(_filter)
                    ? entries
                    : entries.Where(x =>
                        x.key.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.description.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var (key, desc) in filtered)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    if (GUILayout.Button(key, EditorGuiStyles.HoverBoldLabel,
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
