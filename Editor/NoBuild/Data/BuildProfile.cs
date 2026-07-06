// ═══════════════════════════════════════════════════════════
// ── BuildConfiguration / BuildNameTemplate / BuildProfile ─
// ═══════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Platform-agnostic build settings bundle.
    /// When applied by <see cref="BuildExecutor"/>, these override
    /// <c>PlayerSettings</c> and <c>EditorUserBuildSettings</c> for the build duration.
    /// </summary>
    [Serializable]
    public sealed class BuildConfiguration
    {
        [Tooltip("Target platform for this build configuration.")]
        public BuildTarget platform = BuildTarget.Android;

        [Tooltip("Enable Development Build flag.")]
        public bool developmentBuild;

        [Tooltip("Allow script debugging in development builds.")]
        public bool allowDebugging;

        [Tooltip("Connect the profiler automatically.")]
        public bool connectWithProfiler;

        [Tooltip("Scripting backend (Mono or IL2CPP).")]
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;

        [Tooltip("IL2CPP code generation mode.")]
        public Il2CppCodeGeneration il2CppCodeGeneration = Il2CppCodeGeneration.OptimizeSize;

        [Tooltip("Target Android architectures (Android only).")]
        public AndroidArchitecture targetArchitecture = AndroidArchitecture.ARM64;

        [Tooltip("Managed code stripping level.")]
        public ManagedStrippingLevel strippingLevel = ManagedStrippingLevel.Minimal;

        [Tooltip("Strip engine code (IL2CPP only).")]
        public bool stripEngineCode;

        [Tooltip("Override bundle identifier (leave empty to keep current).")]
        public string bundleIdentifierOverride;

        [Tooltip("Override product name (leave empty to keep current).")]
        public string productNameOverride;
    }

    /// <summary>
    /// Template string for build output filenames with {placeholder} resolution.
    /// Supported placeholders are resolved by <see cref="BuildNameResolver"/>.
    /// </summary>
    [Serializable]
    public sealed class BuildNameTemplate
    {
        [Tooltip(
            "Template for the build output file/folder name. Supported placeholders:\n" +
            "  {git-commit}       → 7-char short hash\n" +
            "  {git-commit-full}  → full 40-char hash\n" +
            "  {git-branch}       → current branch name\n" +
            "  {app-version}      → Application.version\n" +
            "  {app-bundle}       → Android bundleVersionCode or iOS buildNumber\n" +
            "  {platform}         → BuildTarget name (e.g., 'Android')\n" +
            "  {profile-name}     → Name of this BuildProfile\n" +
            "  {date}             → yyyy-MM-dd\n" +
            "  {time}             → HHmmss\n" +
            "  {datetime}         → yyyy-MM-dd_HHmmss\n" +
            "  {script-defines}   → Enabled defines, comma-separated\n" +
            "  {scene-set}        → Name of the associated scene set"
        )]
        public string template = "{app-version}_{platform}_{date}";

        public bool HasCustomTemplate =>
            !string.IsNullOrEmpty(template) &&
            template != "{app-version}_{platform}_{date}";
    }

    /// <summary>
    /// Ties together a <see cref="SceneSet"/>, a <see cref="ScriptDefinitionSet"/>,
    /// a <see cref="BuildConfiguration"/>, and a <see cref="BuildNameTemplate"/>
    /// into a single named, one-click build target.
    /// </summary>
    [Serializable]
    public sealed class BuildProfile
    {
        [Tooltip("Display name for this build profile (also used as {profile-name} placeholder).")]
        public string profileName = "New Build Profile";

        [Tooltip("Index into NoBuildSettings.sceneSets. -1 means none.")]
        public int sceneSetIndex = -1;

        [Tooltip("Index into NoBuildSettings.scriptDefinitionSets. -1 means none.")]
        public int scriptDefinitionSetIndex = -1;

        [Tooltip("Build configuration parameters (platform, backend, stripping, etc.).")]
        public BuildConfiguration buildConfiguration = new();

        [Tooltip("Template for the build output file/folder name.")]
        public BuildNameTemplate buildNameTemplate = new();

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public bool HasValidSceneSet(NoBuildSettings settings)
        {
            return settings != null
                   && sceneSetIndex >= 0
                   && sceneSetIndex < settings.sceneSets.Count;
        }

        public bool HasValidDefineSet(NoBuildSettings settings)
        {
            return settings != null
                   && scriptDefinitionSetIndex >= 0
                   && scriptDefinitionSetIndex < settings.scriptDefinitionSets.Count;
        }
    }
}
