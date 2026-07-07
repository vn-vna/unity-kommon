// ═══════════════════════════════════════════════════════════
// ── SceneReference ─────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// References a <see cref="SceneSlot"/> within a parent <see cref="SceneSet"/>
    /// by index, enabling dropdown selection from the set's scenes.
    /// </summary>
    [Serializable]
    public struct SceneReference
    {
        [Tooltip("Index into the parent SceneSet.scenes list.")]
        public int sceneIndex;

        [Tooltip("When false, this reference is excluded from operations.")]
        public bool enabled;

        public bool IsValid => sceneIndex >= 0;
    }
}
