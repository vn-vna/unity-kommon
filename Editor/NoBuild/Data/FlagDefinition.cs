// ═══════════════════════════════════════════════════════════
// ── FlagDefinition / FlagDefinitionType ─────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    public enum FlagDefinitionType
    {
        [Tooltip("Reference an existing ScriptDefinitionSet by index.")]
        Template,
        [Tooltip("Enter a raw scripting define symbol.")]
        Custom
    }

    /// <summary>
    /// A conditional build-name fragment. At build time the associated
    /// script define is checked against the current PlayerSettings
    /// defines, and either <see cref="trueFlag"/> or
    /// <see cref="falseFlag"/> is emitted into the {flags} placeholder.
    /// </summary>
    [Serializable]
    public sealed class FlagDefinition
    {
        [Tooltip("Unique identifier used in {flag-&lt;id&gt;} placeholders.")]
        public string id = "";

        [Tooltip("Display name for this flag rule.")]
        public string name = "New Flag";

        [Tooltip("How the target script define is specified.")]
        public FlagDefinitionType type = FlagDefinitionType.Template;

        [Tooltip(
            "Index into NoBuildSettings.scriptDefinitionSets. " +
            "Used when Type = Template to select which set " +
            "contains the target symbol."
        )]
        public int scriptDefinitionSetIndex = -1;

        [Tooltip(
            "Index into the selected ScriptDefinitionSet.slots. " +
            "Used when Type = Template. -1 = none."
        )]
        public int scriptDefinitionSlotIndex = -1;

        [Tooltip(
            "Raw define symbol to check. Used when Type = Custom."
        )]
        public string customDefineSymbol;

        [Tooltip(
            "Text emitted into {flags} when the associated " +
            "define IS active."
        )]
        public string trueFlag;

        [Tooltip(
            "Text emitted into {flags} when the associated " +
            "define is NOT active."
        )]
        public string falseFlag;

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public bool IsValid => type switch
        {
            FlagDefinitionType.Template =>
                scriptDefinitionSetIndex >= 0
                && scriptDefinitionSlotIndex >= 0,
            FlagDefinitionType.Custom =>
                !string.IsNullOrWhiteSpace(customDefineSymbol),
            _ => false
        };
    }
}
