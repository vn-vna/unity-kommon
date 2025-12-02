using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LocalSave
{

    public static class VersionedData<T> 
    {
        private static VersionMigrator<T>[] _migrators = null;

        public static VersionMigrator<T>[] Migrators
        {
            get
            {
                _migrators ??= GetMigrators();
                return _migrators;
            }
        }

        public static VersionMigrator<T>[] GetMigrators()
        {
            var list = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(asm => asm.GetTypes())
                .Where(t =>
                    t.IsSubclassOf(typeof(VersionMigrator<T>)) &&
                    !t.IsAbstract &&
                    t.GetCustomAttributes<MigratorVersionAttribute>().Any()
                )
                .Select(t => (VersionMigrator<T>)Activator.CreateInstance(t))
                .OrderBy(m => m.GetType().GetCustomAttribute<MigratorVersionAttribute>().Version)
                .ToArray();

            return list;
        }

        public static VersionTag GetVersion(string serializedData)
        {
            // The version string is at the beginning of the file and is separated by a newline
            // which is following the format "Version: x.y.z"
            // If no version tag is found, return a default version of 0.0.0

            var firstLine = serializedData.Split(new[] { '\n' }, 2).FirstOrDefault();
            if (firstLine != null && firstLine.StartsWith("Version: "))
            {
                var versionString = firstLine.Substring("Version: ".Length).Trim();
                return VersionTag.Parse(versionString);
            }

            // If no version tag is found, return a default version of 0.0.0
            return new VersionTag(0, 0, 0);
        }

        public static T Load(string text)
        {
            text = Parse(text);
            T data = JsonUtility.FromJson<T>(text);
            if (data == null)
            {
                Debug.LogWarning($"Failed to deserialize data of type {typeof(T).Name} from text: {text}");
            }

            return data;
        }

        public static string Serialize(T data, bool pretty = false)
        {
            string serializedData = JsonUtility.ToJson(data, pretty);
            if (serializedData == null)
            {
                throw new InvalidOperationException($"Failed to serialize data of type {typeof(T).Name}");
            }

            return BindTag(serializedData);
        }

        private static string Parse(string serializedData)
        {
            serializedData = serializedData.Trim();
            VersionTag currentVersion = GetVersion(serializedData);

            // Remove the version tag from the serialized data if needed
            if (serializedData.StartsWith("Version: "))
            {
                StringReader sr = new StringReader(serializedData);
                string firstLine = sr.ReadLine();
                if (firstLine != null && firstLine.StartsWith("Version: "))
                {
                    serializedData = serializedData.Substring(firstLine.Length).TrimStart();
                }
            }

            foreach (var migrator in Migrators)
            {
                MigratorVersionAttribute versionAttribute = migrator.GetType().GetCustomAttribute<MigratorVersionAttribute>();
                if (versionAttribute.Version != currentVersion) continue;
                migrator.Migrate(serializedData, out serializedData);
                currentVersion = versionAttribute.TargetVersion;
            }

            return serializedData;
        }

        public static string BindTag(string serializedData)
        {
            CurrentDataVersionAttribute currentVersionAttribute = typeof(T).GetCustomAttribute<CurrentDataVersionAttribute>()
                ?? throw new InvalidOperationException($"No CurrentDataVersionAttribute found on {typeof(T).Name}");

            VersionTag currentVersion = currentVersionAttribute.Version;

            // Add the version tag to the beginning of the serialized data
            string versionTag = $"Version: {currentVersion}\n";
            return versionTag + serializedData;
        }
    }


}