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
    /// Build settings that mirror Unity's Build Settings window.
    /// Platform-specific sections appear based on the selected <see cref="platform"/>.
    /// </summary>
    [Serializable]
    public sealed class BuildConfiguration
    {
        // ── General ─────────────────────────────────
        [Tooltip("Target platform.")]
        public BuildTarget platform = BuildTarget.Android;

        [Header("General")]
        [Tooltip("Development Build.")]
        public bool developmentBuild;

        [Tooltip("Allow script debugging.")]
        public bool allowDebugging;

        [Tooltip("Connect profiler.")]
        public bool connectWithProfiler;

        [Tooltip("Scripting backend.")]
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;

        [Tooltip("IL2CPP code generation.")]
        public Il2CppCodeGeneration il2CppCodeGeneration = Il2CppCodeGeneration.OptimizeSize;

        [Tooltip("Managed stripping level.")]
        public ManagedStrippingLevel strippingLevel = ManagedStrippingLevel.Minimal;

        [Tooltip("Strip engine code (IL2CPP).")]
        public bool stripEngineCode;

        [Tooltip("Override bundle identifier.")]
        public string bundleIdentifierOverride;

        [Tooltip("Override product name.")]
        public string productNameOverride;

        // ── Windows ────────────────────────────────
        [Header("Windows")]
        [Tooltip("Create Visual Studio solution.")]
        public bool windowsCreateVSProject;

        [Tooltip("Copy PDB files.")]
        public bool windowsCopyPDB;

        [Tooltip("Copy references.")]
        public bool windowsCopyReferences;

        // ── Android ────────────────────────────────
        [Header("Android")]
        [Tooltip("Export as Android project.")]
        public bool androidExportProject;

        [Tooltip("Build Android App Bundle (AAB).")]
        public bool androidBuildAppBundle;

        [Tooltip("Split application binary.")]
        public bool androidSplitBinary;

#if UNITY_ANDROID
        [Tooltip("Debug symbol level: None, SymbolTable (public), or Full (debugging).")]
        public Unity.Android.Types.DebugSymbolLevel debugSymbolLevel =
            Unity.Android.Types.DebugSymbolLevel.None;

        [Tooltip(
            "Symbol output format (flags):\n" +
            "  Zip = separate symbols.zip\n" +
            "  IncludeInBundle = embed in bundle\n" +
            "  LegacyExtensions = .so.dbg extension style"
        )]
        public Unity.Android.Types.DebugSymbolFormat debugSymbolFormat =
            Unity.Android.Types.DebugSymbolFormat.Zip |
            Unity.Android.Types.DebugSymbolFormat.IncludeInBundle;
#endif

        [Tooltip("Target architectures.")]
        public AndroidArchitecture androidTargetArchitecture = AndroidArchitecture.ARM64;

        // ── iOS ────────────────────────────────────
        [Header("iOS")]
        [Tooltip("Symlink Unity Framework.")]
        public bool iosSymlinkFramework;

        [Tooltip("Run in Xcode after build.")]
        public bool iosRunInXcode;

        [Tooltip("Apple Developer Team ID (leave empty for auto).")]
        public string iosTeamId;

        [Tooltip("Automatic signing.")]
        public bool iosAutomaticSigning = true;
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
            "  {git-commit}        → 7-char short hash\n" +
            "  {git-commit-full}   → full 40-char hash\n" +
            "  {git-branch}        → current branch name\n" +
            "  {app-version}       → Application.version\n" +
            "  {app-bundle}        → Android bundleVersionCode or iOS buildNumber\n" +
            "  {platform}          → BuildTarget name (e.g., 'Android')\n" +
            "  {profile-name}      → Name of this BuildProfile\n" +
            "  {project-name}      → Project folder name\n" +
            "  {project-root}      → Raw project root path\n" +
            "  {project-root-norm} → Project root with slashes as underscores\n" +
            "  {date}              → yyyy-MM-dd\n" +
            "  {time}              → HHmmss\n" +
            "  {datetime}          → yyyy-MM-dd_HHmmss\n" +
            "  {script-defines}    → Enabled defines, comma-separated\n" +
            "  {scene-set}         → Name of the associated scene set"
        )]
        public string template =
            "{platform}-{project-name}-{app-version}@{app-bundle}-{profile-name}";

        public bool HasCustomTemplate =>
            !string.IsNullOrEmpty(template) &&
            template != "{platform}-{project-name}-{app-version}@{app-bundle}-{profile-name}";
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

        [Tooltip(
            "Output folder for builds. Supports placeholders.\n" +
            "e.g., '{project-root}/Build/{platform}/{app-version}'")]
        public BuildNameTemplate buildFolder = new()
        {
            template = "{project-root}/Build/{platform}"
        };

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
