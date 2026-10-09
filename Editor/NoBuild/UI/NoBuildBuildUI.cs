using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    internal static class NoBuildBuildUI
    {
        internal static void ShowConfirmation(Rect rect, BuildProfile profile, NoBuildSettings settings)
        {
            BuildProfile snapshot = JsonUtility.FromJson<BuildProfile>(JsonUtility.ToJson(profile));
            Com.Scheherazade.Common.Editor.CustomPopupDropdown.Show(rect,
                NoBuildDropdowns.CreateBuildConfirmPopup(snapshot, settings,
                    () => BuildExecutor.Build(snapshot),
                    (option, serial) => BuildExecutor.BuildAndRunWithOptions(snapshot, option, serial)));
        }

        internal static void ShowConfirmationFromPopup(Rect rect, BuildProfile profile,
            NoBuildSettings settings, EditorWindow owner)
        {
            Rect screenRect = GUIUtility.GUIToScreenRect(rect);
            owner.Close();
            EditorApplication.delayCall += () => ShowConfirmation(screenRect, profile, settings);
        }

        internal static void DrawReadiness(BuildProfile profile, NoBuildSettings settings, BuildPreflightResult result)
        {
            if (NoBuildDeviceOperations.IsBusy)
                EditorGUILayout.HelpBox(NoBuildDeviceOperations.Status + " Finish or cancel the device operation before starting another build.", MessageType.Info);
            foreach (string error in result.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (string warning in result.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (result.IsRefreshingGit)
                EditorGUILayout.HelpBox("Refreshing Git metadata. The output preview will update shortly.", MessageType.Info);
            if (result.Errors.Count == 0) EditorGUILayout.LabelField(result.Summary, EditorStyles.wordWrappedLabel);
            if (result.DefinesToRemove.Length > 0)
            {
                EditorGUILayout.HelpBox("Remove: " + string.Join(", ", result.DefinesToRemove)
                    + ". Unity must compile before building.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || NoBuildDeviceOperations.IsBusy))
                {
                    if (GUILayout.Button("Apply Defines & Compile"))
                    {
                        NoBuildEditorState.ApplyDefines(settings, profile.scriptDefinitionSetIndex,
                            BuildPipeline.GetBuildTargetGroup(profile.buildConfiguration.platform));
                    }
                }
            }
            if (result.DefinesToAdd.Length > 0)
                EditorGUILayout.LabelField("Build-only additions: " + string.Join(", ", result.DefinesToAdd), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Other existing defines are preserved.", EditorStyles.miniLabel);
        }

        internal static void DrawLastResult()
        {
            if (string.IsNullOrEmpty(NoBuildEditorState.LastResult)) return;
            EditorGUILayout.HelpBox(NoBuildEditorState.LastResult,
                NoBuildEditorState.LastResultFailed ? MessageType.Warning : MessageType.Info);
            string path = NoBuildEditorState.LastOutput;
            if (string.IsNullOrEmpty(path)) return;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!File.Exists(path) && !Directory.Exists(path)))
            {
                if (GUILayout.Button("Reveal Last Build")) EditorUtility.RevealInFinder(path);
            }
            if (GUILayout.Button("Copy Output Path")) EditorGUIUtility.systemCopyBuffer = path;
            EditorGUILayout.EndHorizontal();
        }
    }
}
