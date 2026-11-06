// ═══════════════════════════════════════════════════════════
// ── SceneReference ─────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// References a <see cref="SceneSlot"/> within a parent
    /// <see cref="SceneSet"/> by stable scene asset. The index is retained
    /// only to migrate existing serialized settings.
    /// </summary>
    [Serializable]
    public struct SceneReference
    {
        [Tooltip(
            "Stable scene reference. The legacy index is kept "
            + "for automatic migration.")]
        public SceneAsset scene;

        [Tooltip(
            "Legacy index into the parent SceneSet.scenes list. "
            + "Used only when the stable scene reference is empty.")]
        public int sceneIndex;

        [Tooltip("When false, this reference is excluded from operations.")]
        public bool enabled;

        public bool IsValid => scene != null || sceneIndex >= 0;
    }
}
