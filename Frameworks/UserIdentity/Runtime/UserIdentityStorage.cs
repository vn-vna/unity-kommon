using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// JSON persistence for the current user profile. Local-only storage:
    /// platform ids are never written anywhere else (Apple/Google policy).
    /// </summary>
    public static class UserIdentityStorage
    {
        #region Constants

        private const string FolderName = "UserIdentity";
        private const string FileName = "user.json";

        #endregion

        #region Public Methods

        public static UserProfile Load()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<UserProfile>(json);
            }
            catch (System.Exception ex)
            {
                QuickLog.SWarning(
                    "Failed to load user profile from '{0}': {1}. "
                    + "A new anonymous profile will be created.",
                    path, ex.Message);
                return null;
            }
        }

        public static void Save(UserProfile profile)
        {
            if (profile == null) return;

            try
            {
                EnsureDirectory();
                string json = JsonUtility.ToJson(profile, true);
                File.WriteAllText(GetFilePath(), json);
            }
            catch (System.Exception ex)
            {
                QuickLog.SError(
                    "Failed to save user profile: {0}", ex.Message);
            }
        }

        public static void Delete()
        {
            string path = GetFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        #endregion

        #region Private Methods

        private static string GetFilePath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                FolderName,
                FileName);
        }

        private static void EnsureDirectory()
        {
            string directory = Path.GetDirectoryName(GetFilePath());
            if (!string.IsNullOrEmpty(directory)
                && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        #endregion
    }
}
