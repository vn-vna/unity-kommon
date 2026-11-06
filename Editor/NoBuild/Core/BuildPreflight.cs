using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    internal sealed class BuildPreflightResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public string[] ScenePaths = Array.Empty<string>();
        public string[] DefinesToAdd = Array.Empty<string>();
        public string[] DefinesToRemove = Array.Empty<string>();
        public string OutputPath = "";
        public bool IsRefreshingGit;
        public bool CanBuild => Errors.Count == 0 && DefinesToRemove.Length == 0 && !IsRefreshingGit;
        public string Summary => Errors.FirstOrDefault()
            ?? (IsRefreshingGit ? "Refreshing Git metadata…" : DefinesToRemove.Length > 0
                ? "Apply defines and finish compilation before building." : "Ready to build.");
    }

    internal static class BuildPreflight
    {
        public static BuildPreflightResult Evaluate(BuildProfile profile, NoBuildSettings settings,
            bool checkEnvironment = true)
        {
            var result = new BuildPreflightResult();
            if (profile?.buildConfiguration == null || settings == null)
            {
                result.Errors.Add("Build profile or settings are missing.");
                return result;
            }
            BuildConfiguration config = profile.buildConfiguration;
            if (config.platform == BuildTarget.NoTarget)
            {
                result.Errors.Add("Select a target platform.");
            }
            else if (checkEnvironment && !BuildPipeline.IsBuildTargetSupported(
                         BuildPipeline.GetBuildTargetGroup(config.platform), config.platform))
            {
                result.Errors.Add($"{config.platform} build support is not installed for this Editor.");
            }
            if (checkEnvironment && (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer))
            {
                result.Errors.Add("Wait until Unity is idle and outside Play Mode.");
            }
            if (config.platform == BuildTarget.Android && config.androidExportProject && config.androidBuildAppBundle)
            {
                result.Errors.Add("Export Project and Build App Bundle cannot both be enabled.");
            }
            if (config.platform == BuildTarget.Android && config.androidTargetArchitecture == 0)
            {
                result.Errors.Add("Select at least one Android architecture.");
            }
            if (config.platform == BuildTarget.Android && !string.IsNullOrWhiteSpace(config.bundleIdentifierOverride)
                && !AdbUtility.IsValidPackageName(config.bundleIdentifierOverride.Trim()))
                result.Errors.Add("Android package override is invalid. Use a package such as com.company.game.");
            if (!profile.HasValidSceneSet(settings))
            {
                result.Errors.Add("Select a valid scene set.");
            }
            else
            {
                SceneSet set = settings.sceneSets[profile.sceneSetIndex];
                bool hasOverride = set?.buildOrderOverride?.Count > 0;
                var slots = hasOverride ? set.buildOrderOverride : set?.scenes;
                result.ScenePaths = GetScenePaths(slots);
                if (hasOverride && result.ScenePaths.Length == 0)
                {
                    result.Warnings.Add("Build order override has no valid scenes; the scene set order will be used.");
                    result.ScenePaths = GetScenePaths(set.scenes);
                }
                if (result.ScenePaths.Length == 0) result.Errors.Add("Scene set has no valid, enabled scenes.");
                if (slots?.Any(slot => slot == null || (slot.enabled && !slot.IsValid)) == true)
                    result.Errors.Add("The scene list contains missing scene references. Repair or remove them.");
                if (result.ScenePaths.Distinct().Count() != result.ScenePaths.Length)
                    result.Errors.Add("The build scene list contains duplicate scenes.");
            }
            if (profile.scriptDefinitionSetIndex != -1 && !profile.HasValidDefineSet(settings))
            {
                result.Errors.Add("Define set reference is missing. Select a valid set or explicitly choose None.");
            }
            else if (profile.HasValidDefineSet(settings))
            {
                ScriptDefinitionSet set = settings.scriptDefinitionSets[profile.scriptDefinitionSetIndex];
                string error = ScriptDefinitionSwitcher.ValidateSet(set);
                if (error != null) result.Errors.Add(error);
                else if (checkEnvironment && config.platform != BuildTarget.NoTarget)
                {
                    var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(config.platform));
                    var current = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(target)
                        .Split(';').Select(value => value.Trim()), StringComparer.Ordinal);
                    result.DefinesToAdd = set.slots.Where(slot => slot.enabled && !current.Contains(slot.defineSymbol.Trim()))
                        .Select(slot => slot.defineSymbol.Trim()).Distinct().ToArray();
                    result.DefinesToRemove = set.slots.Where(slot => !slot.enabled && current.Contains(slot.defineSymbol.Trim()))
                        .Select(slot => slot.defineSymbol.Trim()).Distinct().ToArray();
                }
            }
            string[] unknown = BuildNameResolver.GetUnknownPlaceholders(
                (profile.buildFolder?.template ?? "") + "/" + (profile.buildNameTemplate?.template ?? ""), settings);
            if (unknown.Length > 0) result.Errors.Add("Unknown output tokens: " + string.Join(", ", unknown));
            string templates = (profile.buildFolder?.template ?? "") + (profile.buildNameTemplate?.template ?? "");
            foreach (var duplicate in settings.flagDefinitions.Where(flag => flag != null && !string.IsNullOrWhiteSpace(flag.id))
                         .GroupBy(flag => flag.id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                if (templates.IndexOf("{flags}", StringComparison.OrdinalIgnoreCase) >= 0
                    || templates.IndexOf("{flag-" + duplicate.Key + "}", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Errors.Add("Duplicate flag ID: " + duplicate.Key + ". Give each flag a unique ID.");
            }
            try
            {
                result.OutputPath = GetOutputPath(profile, settings);
                if (templates.IndexOf("{git-", StringComparison.OrdinalIgnoreCase) >= 0 && GitUtility.IsRefreshing)
                    result.IsRefreshingGit = true;
                if (File.Exists(result.OutputPath) || Directory.Exists(result.OutputPath))
                    result.Warnings.Add("The output already exists. This build may overwrite it.");
            }
            catch (Exception exception)
            {
                result.Errors.Add("Invalid output path: " + exception.Message);
            }
            return result;
        }

        private static string[] GetScenePaths(IEnumerable<SceneSlot> slots) => slots?
            .Where(slot => slot != null && slot.enabled && slot.IsValid)
            .Select(slot => slot.ScenePath).ToArray() ?? Array.Empty<string>();

        public static string GetOutputPath(BuildProfile profile, NoBuildSettings settings)
        {
            string folder = BuildNameResolver.Resolve(profile.buildFolder?.template ?? "{project-root}/Builds", profile, settings);
            if (!Path.IsPathRooted(folder)) folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), folder);
            string name = BuildNameResolver.ResolveFileName(profile.buildNameTemplate?.template ?? "{app-version}", profile, settings);
            return Path.GetFullPath(Path.Combine(folder, name + GetExtension(profile.buildConfiguration)));
        }

        public static string GetExtension(BuildConfiguration config) => config.platform switch
        {
            BuildTarget.Android => config.androidExportProject ? "" : config.androidBuildAppBundle ? ".aab" : ".apk",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
            BuildTarget.StandaloneOSX => ".app",
            _ => ""
        };
    }
}
