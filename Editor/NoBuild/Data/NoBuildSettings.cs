// ═══════════════════════════════════════════════════════════
// ── NoBuildSettings (Root ScriptableObject) ───────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    public sealed class NoBuildSettings : ScriptableObject
    {
        public const string ResourcePath = "NoBuild/NoBuildSettings";
        public const string AssetFolder = "Assets/Resources/NoBuild";
        public const string AssetPath = AssetFolder + "/NoBuildSettings.asset";

        [Header("Scene Sets")]
        [Tooltip("All configured scene sets (for full-set switching and builds).")]
        public List<SceneSet> sceneSets = new();

        [Header("Script Definition Sets")]
        [Tooltip("All configured scripting define symbol sets.")]
        public List<ScriptDefinitionSet> scriptDefinitionSets = new();

        [Header("Build Profiles")]
        [Tooltip("All configured build profiles.")]
        public List<BuildProfile> buildProfiles = new();

        [Header("Flags")]
        [Tooltip(
            "Conditional build-name fragments. Each flag checks a " +
            "script define and emits trueFlag or falseFlag into " +
            "the {flags} placeholder."
        )]
        public List<FlagDefinition> flagDefinitions = new();

        [Header("Active State")]
        [Tooltip("Index of the currently active scene set (-1 = none).")]
        public int activeSceneSetIndex = -1;

        [Tooltip("Index of the currently active script definition set (-1 = none).")]
        public int activeScriptDefinitionSetIndex = -1;

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public SceneSet ActiveSceneSet
        {
            get
            {
                if (activeSceneSetIndex < 0 || activeSceneSetIndex >= sceneSets.Count)
                    return null;
                return sceneSets[activeSceneSetIndex];
            }
        }

        public ScriptDefinitionSet ActiveScriptDefinitionSet
        {
            get
            {
                if (activeScriptDefinitionSetIndex < 0
                    || activeScriptDefinitionSetIndex >= scriptDefinitionSets.Count)
                    return null;
                return scriptDefinitionSets[activeScriptDefinitionSetIndex];
            }
        }
    }
}
