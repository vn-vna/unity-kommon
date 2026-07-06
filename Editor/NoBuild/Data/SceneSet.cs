// ═══════════════════════════════════════════════════════════
// ── SceneSet ──────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// A named collection of scene slots. Used for:
    ///   - Switching ALL scenes at once (the entire set)
    ///   - Providing the scene list for build profiles
    ///
    /// <c>buildOrderOverride</c> is an optional ordered list that
    /// overrides the default scene order when building.
    /// </summary>
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

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public bool HasContent => scenes != null && scenes.Count > 0;
    }
}
