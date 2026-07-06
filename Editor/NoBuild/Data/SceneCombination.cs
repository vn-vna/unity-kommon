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

        [Tooltip("Scenes opened together when this combination is activated.")]
        public List<SceneSlot> scenes = new();

        [Tooltip("When disabled, this combination is hidden from shortcuts.")]
        public bool enabled = true;

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(name) ? name : "(Unnamed)";

        public bool IsValid =>
            enabled && scenes != null && scenes.Count > 0
            && scenes.Any(s => s != null && s.IsValid);
    }
}
