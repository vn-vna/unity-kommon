// ═══════════════════════════════════════════════════════════
// ── SceneCombination ──────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// A configurable shortcut target that maps a keyboard key [1]..[9]
    /// (via toolbar buttons or Shift+Ctrl+N) to a specific scene.
    ///
    /// Combinations appear in the dropdown as "Open scenes" and
    /// each gets a sequential shortcut index based on its
    /// position in <see cref="NoBuildSettings.combinations"/>.
    /// </summary>
    [Serializable]
    public sealed class SceneCombination
    {
        [Tooltip("Display name for this combination (e.g., 'Main Menu', 'Game Level 1').")]
        public string name = "New Combination";

        [Tooltip(
            "Scenes referenced by index from the parent SceneSet. " +
            "Select via dropdown in the settings UI."
        )]
        public List<SceneReference> sceneReferences = new();

        [Tooltip("When disabled, this combination is hidden from shortcuts.")]
        public bool enabled = true;

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(name) ? name : "(Unnamed)";

        public bool IsValid =>
            enabled && sceneReferences != null && sceneReferences.Count > 0
            && sceneReferences.Any(r => r.enabled && r.IsValid);

        /// <summary>
        /// Resolves <see cref="sceneReferences"/> indices to actual
        /// <see cref="SceneSlot"/> objects from the parent <see cref="SceneSet"/>.
        /// </summary>
        public List<SceneSlot> ResolveScenes(SceneSet parentSet)
        {
            List<SceneSlot> result = new();
            if (parentSet == null || parentSet.scenes == null
                || sceneReferences == null)
                return result;

            foreach (SceneReference r in sceneReferences)
            {
                if (r.enabled
                    && r.sceneIndex >= 0
                    && r.sceneIndex < parentSet.scenes.Count)
                {
                    SceneSlot slot = parentSet.scenes[r.sceneIndex];
                    if (slot != null && slot.IsValid)
                        result.Add(slot);
                }
            }

            return result;
        }
    }
}
