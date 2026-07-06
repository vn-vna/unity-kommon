// ═══════════════════════════════════════════════════════════
// ── SceneSlot ─────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Atomic unit of scene configuration. Pairs a <see cref="SceneAsset"/> reference
    /// with an enabled toggle and an optional display-name override.
    /// </summary>
    [Serializable]
    public sealed class SceneSlot
    {
        [Tooltip("The scene asset this slot references.")]
        public SceneAsset scene;

        [Tooltip("When false, this slot is excluded from set operations and builds.")]
        public bool enabled = true;

        [Tooltip("Optional override name displayed in UI instead of the scene file name.")]
        public string displayName;

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : scene != null
                    ? scene.name
                    : "<Missing Scene>";

        public string ScenePath =>
            scene != null
                ? AssetDatabase.GetAssetPath(scene)
                : null;

        public bool IsValid =>
            scene != null && !string.IsNullOrEmpty(ScenePath);

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        public SceneSlot Clone()
        {
            return new SceneSlot
            {
                scene = scene,
                enabled = enabled,
                displayName = displayName
            };
        }
    }
}
