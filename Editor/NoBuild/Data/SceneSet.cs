// ═══════════════════════════════════════════════════════════
// ── SceneSet ──────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    [Serializable]
    public sealed class SceneSet
    {
        [Tooltip("Unique display name for this scene set.")]
        public string setName = "New Scene Set";

        [Tooltip("All scenes in this set.")]
        public List<SceneSlot> scenes = new();

        [Tooltip(
            "Optional override for build ordering. " +
            "If empty, scenes are built in their natural order."
        )]
        public List<SceneSlot> buildOrderOverride = new();

        [Header("Shortcut Combinations")]
        [Tooltip(
            "Per-set shortcut targets. Toolbar buttons [1]..[9] map to " +
            "enabled combinations in order. Switching to this set also " +
            "switches the active combinations context."
        )]
        public List<SceneCombination> combinations = new();

        public bool HasContent => scenes != null && scenes.Count > 0;

        public List<SceneCombination> GetEnabledCombinations()
        {
            List<SceneCombination> r = new();
            if (combinations == null) return r;
            foreach (SceneCombination combination in combinations)
            {
                if (combination != null
                    && combination.enabled
                    && combination.ResolveScenes(this).Count > 0)
                {
                    r.Add(combination);
                }
            }
            return r;
        }
    }
}
