using System;
using System.IO;
using Com.Hapiga.Scheherazade.Common.LocalSave;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LocalSave
{
    public class LocalFileHandler
    {
        public static string FolderPath => Path.Combine(Application.persistentDataPath, "SaveData");
        public static string GetFilePath(string fileName) => Path.Combine(FolderPath, fileName + ".json");

        public static void Save<T>(T data, string fileName) where T : IVersionedData
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                    Directory.CreateDirectory(FolderPath);

                bool prettyPrint = true;
#if PRODUCTION_BUILD
                prettyPrint = false;
#endif
                string json = VersionedData<T>.Serialize(data, prettyPrint);
                File.WriteAllText(GetFilePath(fileName), json);
                QuickLog.Info<LocalFileHandler>(
                    "Saved '{0}' to: {1}",
                    fileName, GetFilePath(fileName)
                );
            }
            catch (Exception e)
            {
                QuickLog.Error<LocalFileHandler>(
                    "Failed to save {0}: {1}",
                    fileName, e
                );
            }
        }

        public static T Load<T>(string fileName) where T : IVersionedData, new()
        {
            string path = GetFilePath(fileName);

            if (!File.Exists(path))
            {
                QuickLog.Warning<LocalFileHandler>(
                    "File not found: {0}. Returning new default instance.",
                    path
                );
                return new T();
            }

            try
            {
                string json = File.ReadAllText(path);
                return VersionedData<T>.Load(json);
            }
            catch (Exception e)
            {
                QuickLog.Error<LocalFileHandler>(
                    "Failed to load {0}: {1}",
                    fileName, e
                );
                return new T();
            }
        }

        public static bool Delete(string fileName)
        {
            string path = GetFilePath(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }

        public static bool Exists(string fileName)
        {
            return File.Exists(GetFilePath(fileName));
        }

        public static void Reset(string fileName)
        {
            string path = GetFilePath(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                QuickLog.Info<LocalFileHandler>(
                    "Reset (deleted) save file: {0}",
                    path
                );
            }
            else
            {
                QuickLog.Warning<LocalFileHandler>(
                    "Reset (deleted) save file: {0}",
                    path
                );
            }
        }

    }
}
