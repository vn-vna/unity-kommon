using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Archetype.Editor
{
    [Serializable]
    internal sealed class ArchetypeCacheEntry
    {
        public string concreteTypeName;
        public string concreteTypeFullName;
        public string interfaceTypeFullName;
        public string archetypeName;
        public string archetypeField;
    }

    [Serializable]
    internal sealed class ArchetypeCache
    {
        public int version = 1;
        public string hash;
        public string generatedAt;
        public string generationStatus;
        public List<string> files;
        public List<ArchetypeCacheEntry> entries;
    }

    internal static class ArchetypeHashCache
    {
        private const string CacheFolder = "Library/ScheherazadeArchetypeCache";
        private const string CacheFile = CacheFolder + "/cache.json";

        public static ArchetypeCache Load()
        {
            try
            {
                if (!File.Exists(CacheFile))
                {
                    return null;
                }

                string json = File.ReadAllText(CacheFile, Encoding.UTF8);
                return JsonUtility.FromJson<ArchetypeCache>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[Archetype] Failed to load cache from '{CacheFile}'. "
                    + $"Cache will be regenerated. Error: {ex.Message}"
                );
                return null;
            }
        }

        public static void Save(ArchetypeCache cache)
        {
            try
            {
                string dir = Path.GetDirectoryName(CacheFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonUtility.ToJson(cache, prettyPrint: true);
                File.WriteAllText(CacheFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Archetype] Failed to write cache to '{CacheFile}'. "
                    + $"Error: {ex.Message}"
                );
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    File.Delete(CacheFile);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[Archetype] Failed to delete cache file. "
                    + $"Error: {ex.Message}"
                );
            }
        }

        public static string ComputeHash(
            List<ArchetypeCacheEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            entries.Sort((a, b) =>
            {
                int nameCompare = string.CompareOrdinal(
                    a.archetypeName, b.archetypeName);
                if (nameCompare != 0)
                {
                    return nameCompare;
                }

                return string.CompareOrdinal(
                    a.concreteTypeName, b.concreteTypeName);
            });

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < entries.Count; i++)
            {
                ArchetypeCacheEntry e = entries[i];
                sb.Append(e.concreteTypeFullName ?? e.concreteTypeName);
                sb.Append('|');
                sb.Append(e.interfaceTypeFullName);
                sb.Append('|');
                sb.Append(e.archetypeName);
                sb.Append('|');
                sb.Append(e.archetypeField);
                sb.Append(';');
            }

            using SHA256 sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(
                Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(hashBytes)
                .Replace("-", "")
                .ToLowerInvariant();
        }
    }
}
