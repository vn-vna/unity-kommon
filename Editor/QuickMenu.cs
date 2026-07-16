using System.IO;
using UnityEditor;
using UnityEngine;

public static class QuickMenu
{
    [MenuItem("Dev Menu/Tools/Clean Gradle Cache")]
    private static void CleanGradleCache()
    {
        if (!EditorUtility.DisplayDialog(
            "Clean Gradle Cache",
            "This will delete all Gradle caches and Android build artifacts.\n\n" +
            "This includes:\n" +
            "- Global Gradle cache (~/.gradle/caches)\n" +
            "- Gradle wrapper distributions (~/.gradle/wrapper/dists)\n" +
            "- Project Android build artifacts (Library/Bee/Android)\n\n" +
            "A clean Android build will follow. Continue?",
            "Yes, Clean All",
            "Cancel"))
        {
            return;
        }

        string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string projectPath = Directory.GetParent(Application.dataPath).FullName;

        string[] targets = new[]
        {
            Path.Combine(userProfile, ".gradle", "caches"),
            Path.Combine(userProfile, ".gradle", "wrapper", "dists"),
            Path.Combine(projectPath, "Library", "Bee", "Android"),
        };

        int deletedCount = 0;

        try
        {
            for (int i = 0; i < targets.Length; i++)
            {
                string label = Path.GetFileName(targets[i]);
                float progress = (float)i / targets.Length;

                if (EditorUtility.DisplayCancelableProgressBar(
                    "Cleaning Gradle Cache",
                    $"Deleting {label}...",
                    progress))
                {
                    Debug.Log("[GradleCacheCleaner] Cancelled by user.");
                    break;
                }

                deletedCount += DeleteDirectory(targets[i]);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (deletedCount > 0)
        {
            EditorUtility.DisplayDialog(
                "Done",
                $"Cleaned {deletedCount} Gradle cache directories.\n\n" +
                "The next Android build will perform a clean build.",
                "OK");
            Debug.Log($"[GradleCacheCleaner] Cleaned {deletedCount} Gradle cache directories.");
        }
        else
        {
            EditorUtility.DisplayDialog(
                "Done",
                "No Gradle cache directories were found to clean.",
                "OK");
            Debug.Log("[GradleCacheCleaner] No Gradle cache directories found.");
        }

        AssetDatabase.Refresh();
    }

    private static int DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Debug.Log($"[GradleCacheCleaner] Not found, skipping: {path}");
            return 0;
        }

        try
        {
            Directory.Delete(path, true);
            Debug.Log($"[GradleCacheCleaner] Deleted: {path}");
            return 1;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GradleCacheCleaner] Failed to delete {path}: {e.Message}");
            return 0;
        }
    }

    [MenuItem("Dev Menu/Data/Open Saved Folder")]
    public static void OpenSavedFolder()
    {
        string path = Application.persistentDataPath;
        EditorUtility.RevealInFinder(path);
        Debug.Log($"Opened folder: {path}");
    }

    [MenuItem("Dev Menu/Data/Clear Saved Folder")]
    public static void ClearSavedFolder()
    {
        string path = Application.persistentDataPath;

        if (EditorUtility.DisplayDialog(
            "Clear Saved Data",
            "Are you sure you want to delete all saved data and PlayerPrefs? This cannot be undone.",
            "Yes, Clear Data", "Cancel"
        ))
        {
            PlayerPrefs.DeleteAll();

            if (Directory.Exists(path))
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(path);
                foreach (FileInfo file in directoryInfo.GetFiles()) file.Delete();
                foreach (DirectoryInfo dir in directoryInfo.GetDirectories()) dir.Delete(true);
            }

            PlayerPrefs.Save();
            Debug.Log("All saved data and PlayerPrefs have been cleared.");
        }
    }
}
