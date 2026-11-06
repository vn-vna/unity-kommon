using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    [InitializeOnLoad]
    internal static class NoBuildEditorState
    {
        private static string Key => "NoBuild." + Application.dataPath + ".";

        static NoBuildEditorState()
        {
            EditorApplication.hierarchyChanged += NoBuildToolbarState.RequestRepaint;
            EditorApplication.projectChanged += NoBuildToolbarState.RequestRepaint;
            Undo.undoRedoPerformed += NoBuildToolbarState.RequestRepaint;
            BuildExecutor.BuildCompleted += (profile, report) =>
                SetResult($"Build succeeded: {profile.profileName} · {report.summary.totalTime:g} · {report.summary.totalSize / (1024 * 1024)} MB", report.summary.outputPath);
            BuildExecutor.BuildFailed += (profile, message) => SetResult(message, failed: true);
        }

        public static string LastResult => SessionState.GetString(Key + "LastResult", "");
        public static string LastOutput => SessionState.GetString(Key + "LastOutput", "");
        public static bool LastResultFailed => SessionState.GetBool(Key + "LastResultFailed", false);

        public static void SetResult(string message, string output = null, bool failed = false)
        {
            SessionState.SetString(Key + "LastResult", message ?? "");
            SessionState.SetBool(Key + "LastResultFailed", failed);
            if (output != null) SessionState.SetString(Key + "LastOutput", output);
            NoBuildToolbarState.RequestRepaint();
        }

        public static int GetIndex(NoBuildSettings settings, string context, int legacyValue)
        {
            string path = AssetDatabase.GetAssetPath(settings);
            return string.IsNullOrEmpty(path) ? legacyValue
                : SessionState.GetInt(Key + path + "." + context, legacyValue);
        }

        public static void SetIndex(NoBuildSettings settings, string context, int value)
        {
            string path = AssetDatabase.GetAssetPath(settings);
            if (!string.IsNullOrEmpty(path))
            {
                SessionState.SetInt(Key + path + "." + context, value);
            }
            NoBuildToolbarState.RequestRepaint();
        }

        public static bool OpenSet(NoBuildSettings settings, int index, bool play = false)
        {
            if (settings == null || index < 0 || index >= settings.sceneSets.Count) return false;
            SceneSet set = settings.sceneSets[index];
            bool succeeded = play ? SceneSwitcher.PlayFromSet(set) : SceneSwitcher.SwitchToSet(set);
            if (!succeeded) return false;
            settings.SelectedSceneSetIndex = index;
            SetResult($"Opened scene set: {set.setName}");
            return true;
        }

        public static bool ApplyDefines(NoBuildSettings settings, int index,
            BuildTargetGroup? targetGroup = null)
        {
            if (settings == null || index < 0 || index >= settings.scriptDefinitionSets.Count) return false;
            BuildTargetGroup target = targetGroup ?? EditorUserBuildSettings.selectedBuildTargetGroup;
            ScriptDefinitionSet set = settings.scriptDefinitionSets[index];
            if (!ScriptDefinitionSwitcher.ApplySet(set, target)) return false;
            SetIndex(settings, "Defines." + target, index);
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
            {
                settings.activeScriptDefinitionSetIndex = index;
            }
            SetResult($"Applied defines: {set.setName} → {target}");
            return true;
        }

        public static bool ScenesMatch(SceneSet set)
        {
            if (set?.scenes == null) return false;
            if (set.scenes.Any(slot => slot == null || (slot.enabled && !slot.IsValid))) return false;
            string[] expected = set.scenes.Where(slot => slot != null && slot.enabled && slot.IsValid)
                .Select(slot => slot.ScenePath).ToArray();
            if (expected.Length == 0 || expected.Length != SceneManager.sceneCount) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path != expected[i]) return false;
            }
            return SceneManager.GetActiveScene().path == expected[0];
        }

        public static bool DefinesMatch(ScriptDefinitionSet set)
        {
            if (set?.slots == null || ScriptDefinitionSwitcher.ValidateSet(set) != null) return false;
            var current = ScriptDefinitionSwitcher.GetCurrentDefines();
            return set.slots.All(slot => current.Contains(slot.defineSymbol.Trim()) == slot.enabled);
        }

        internal static void RemapContexts(NoBuildSettings settings, int changedIndex, bool insertion, bool defines)
        {
            int Remap(int index) => insertion ? (index >= changedIndex ? index + 1 : index)
                : index == changedIndex ? -1 : index > changedIndex ? index - 1 : index;
            if (defines)
            {
                int legacyIndex = settings.activeScriptDefinitionSetIndex;
                foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)).Cast<BuildTargetGroup>().Distinct())
                {
                    string context = "Defines." + group;
                    SetIndex(settings, context, Remap(GetIndex(settings, context, legacyIndex)));
                }
                settings.activeScriptDefinitionSetIndex = Remap(legacyIndex);
            }
            else
            {
                int legacyIndex = settings.activeSceneSetIndex;
                SetIndex(settings, "Scenes", Remap(GetIndex(settings, "Scenes", legacyIndex)));
                settings.activeSceneSetIndex = Remap(legacyIndex);
            }
        }
    }
}
