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

        [Header("Scene Combinations (Shortcuts)")]
        [Tooltip(
            "Named shortcut targets. Toolbar buttons [1]..[9] map to these in order. " +
            "Each combination switches to a single scene."
        )]
        public List<SceneCombination> combinations = new();

        [Header("Script Definition Sets")]
        [Tooltip("All configured scripting define symbol sets.")]
        public List<ScriptDefinitionSet> scriptDefinitionSets = new();

        [Header("Build Profiles")]
        [Tooltip("All configured build profiles.")]
        public List<BuildProfile> buildProfiles = new();

        [Header("Active State")]
        [Tooltip("Index of the currently active scene set (-1 = none).")]
        public int activeSceneSetIndex = -1;

        [Tooltip("Index of the currently active script definition set (-1 = none).")]
        public int activeScriptDefinitionSetIndex = -1;

        [Header("Shortcuts")]
        [Tooltip("When disabled, all NoBuild keyboard shortcuts are suppressed.")]
        public bool shortcutsEnabled = true;

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

        /// <summary>
        /// Returns enabled combinations, indexed for shortcut mapping.
        /// </summary>
        public List<SceneCombination> GetEnabledCombinations()
        {
            List<SceneCombination> result = new();
            if (combinations == null) return result;
            foreach (SceneCombination c in combinations)
            {
                if (c != null && c.IsValid) result.Add(c);
            }

            return result;
        }
    }
}
