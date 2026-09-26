using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    internal static class NoBuildToolbarLabelResolver
    {
        private const string DefaultLabel = "NoBuild";

        public static string Resolve(NoBuildSettings settings)
        {
            if (settings == null)
            {
                return DefaultLabel;
            }

            string template = string.IsNullOrWhiteSpace(
                settings.toolbarLabelTemplate)
                ? DefaultLabel
                : settings.toolbarLabelTemplate.Trim();

            SceneSet sceneSet = settings.ActiveSceneSet;
            ScriptDefinitionSet defineSet =
                settings.ActiveScriptDefinitionSet;

            string projectRoot = Path.GetDirectoryName(
                Application.dataPath);
            string projectName = string.IsNullOrEmpty(projectRoot)
                ? "Unknown"
                : Path.GetFileName(projectRoot);

            return template
                .Replace(
                    "{scene-set}",
                    sceneSet?.setName ?? "No Scene Set")
                .Replace(
                    "{define-set}",
                    defineSet?.setName ?? "No Define Set")
                .Replace(
                    "{platform}",
                    EditorUserBuildSettings.activeBuildTarget
                        .ToString())
                .Replace(
                    "{scene-count}",
                    CountScenes(sceneSet).ToString())
                .Replace(
                    "{define-count}",
                    CountDefines(defineSet).ToString())
                .Replace(
                    "{project-name}",
                    projectName);
        }

        private static int CountScenes(SceneSet sceneSet)
        {
            return sceneSet?.scenes?.Count(
                slot => slot != null
                    && slot.enabled
                    && slot.IsValid) ?? 0;
        }

        private static int CountDefines(
            ScriptDefinitionSet defineSet)
        {
            return defineSet?.slots?.Count(
                slot => slot != null
                    && slot.enabled
                    && !string.IsNullOrWhiteSpace(
                        slot.defineSymbol)) ?? 0;
        }
    }
}
