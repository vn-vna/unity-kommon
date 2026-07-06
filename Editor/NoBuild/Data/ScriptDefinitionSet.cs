// ═══════════════════════════════════════════════════════════
// ── ScriptDefinitionSlot / ScriptDefinitionSet ────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Holds a single scripting define symbol and its desired enabled state.
    /// </summary>
    [Serializable]
    public sealed class ScriptDefinitionSlot
    {
        [Tooltip(
            "The scripting define symbol (e.g., 'ENABLE_CHEATS'). " +
            "Must be a valid C# identifier."
        )]
        public string defineSymbol;

        [Tooltip("When enabled, this symbol will be added to the project's script defines.")]
        public bool enabled;
    }

    /// <summary>
    /// A named collection of <see cref="ScriptDefinitionSlot"/> entries.
    /// Applying a set toggles the project's scripting define symbols to match.
    /// </summary>
    [Serializable]
    public sealed class ScriptDefinitionSet
    {
        [Tooltip("Unique display name for this define set.")]
        public string setName = "New Define Set";

        [Tooltip("The list of define symbols and their desired states.")]
        public List<ScriptDefinitionSlot> slots = new();

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public bool HasContent => slots != null && slots.Count > 0;
    }
}
