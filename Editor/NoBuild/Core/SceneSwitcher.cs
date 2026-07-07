// ═══════════════════════════════════════════════════════════
// ── SceneSwitcher ─────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Stateless orchestrator for scene switching operations.
    /// </summary>
    internal static class SceneSwitcher
    {
        public static event Action<SceneSet> SceneSetSwitched;
        public static event Action<SceneCombination> CombinationSwitched;
        public static event Action<string> SwitchFailed;

        /// <summary>
        /// Closes all current scenes, then loads all enabled, valid scenes from the set.
        /// </summary>
        public static void SwitchToSet(SceneSet set)
        {
            if (set == null) { FireFailed("Scene set is null."); return; }

            List<SceneSlot> valid = set.scenes
                .Where(s => s.enabled && s.IsValid)
                .ToList();

            if (valid.Count == 0)
            {
                FireFailed($"Scene set '{set.setName}' has no valid, enabled scenes.");
                return;
            }

            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

                Scene firstScene = EditorSceneManager.OpenScene(
                    valid[0].ScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(firstScene);

                for (int i = 1; i < valid.Count; i++)
                    EditorSceneManager.OpenScene(valid[i].ScenePath, OpenSceneMode.Additive);

                SceneSetSwitched?.Invoke(set);
            }
            catch (Exception ex)
            {
                FireFailed($"Failed to switch to scene set '{set.setName}': {ex.Message}");
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Opens all scenes in the combination, resolving <see cref="SceneReference"/>
        /// indices against the given parent <see cref="SceneSet"/>.
        /// First valid scene is opened as single (closing others),
        /// remaining are loaded additively. The first scene is set active.
        /// </summary>
        public static void SwitchToCombination(
            SceneCombination combination,
            SceneSet parentSet)
        {
            if (combination == null) { FireFailed("Combination is null."); return; }
            if (parentSet == null) { FireFailed("Parent SceneSet is null."); return; }

            var valid = combination.ResolveScenes(parentSet);

            if (valid.Count == 0)
            {
                FireFailed($"Combination '{combination.DisplayName}' has no valid scenes.");
                return;
            }

            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

                Scene first = EditorSceneManager.OpenScene(
                    valid[0].ScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(first);

                for (int i = 1; i < valid.Count; i++)
                    EditorSceneManager.OpenScene(
                        valid[i].ScenePath, OpenSceneMode.Additive);

                CombinationSwitched?.Invoke(combination);
            }
            catch (Exception ex)
            {
                FireFailed($"Failed to switch to '{combination.DisplayName}': {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private static void FireFailed(string message)
        {
            Debug.LogError($"[NoBuild] {message}");
            SwitchFailed?.Invoke(message);
            EditorUtility.DisplayDialog("NoBuild — Scene Switch Failed", message, "OK");
        }
    }
}
