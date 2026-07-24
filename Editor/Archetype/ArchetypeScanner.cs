using System;
using System.Collections.Generic;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Archetype;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Archetype.Editor
{
    internal static class ArchetypeScanner
    {
        private static List<ArchetypeCacheEntry> _cachedEntries;

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            _cachedEntries = null;

            TryAutoGenerate();
        }

        internal static void ForceRegenerate()
        {
            _cachedEntries = null;
            ArchetypeHashCache.Delete();
            TryAutoGenerate();
        }

        private static Assembly _runtimeAssembly;

        internal static List<ArchetypeCacheEntry> ScanCurrentEntries()
        {
            if (_cachedEntries != null)
            {
                return _cachedEntries;
            }

            if (_runtimeAssembly == null)
            {
                _runtimeAssembly = typeof(ArchetypeAttribute).Assembly;
            }

            List<ArchetypeCacheEntry> entries
                = new List<ArchetypeCacheEntry>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                // Only scan the Runtime assembly itself — generated .g.cs
                // lives in the same assembly context and can only directly
                // reference types from the Runtime assembly.
                Assembly assembly = assemblies[i];
                if (assembly != _runtimeAssembly)
                {
                    continue;
                }

                ScanAssembly(assembly, entries);
            }

            // Sort by archetype name then concrete type name
            entries.Sort((a, b) =>
            {
                int cmp = string.CompareOrdinal(
                    a.archetypeName, b.archetypeName);
                if (cmp != 0)
                {
                    return cmp;
                }

                return string.CompareOrdinal(
                    a.concreteTypeFullName, a.concreteTypeFullName);
            });

            _cachedEntries = entries;
            return entries;
        }

        private static void ScanAssembly(
            Assembly assembly,
            List<ArchetypeCacheEntry> entries)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                return;
            }

            if (types == null)
            {
                return;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                ArchetypeAttribute[] attributes;

                try
                {
                    attributes = (ArchetypeAttribute[])type.GetCustomAttributes(
                        typeof(ArchetypeAttribute), inherit: false);
                }
                catch
                {
                    continue;
                }

                if (attributes == null || attributes.Length == 0)
                {
                    continue;
                }

                for (int j = 0; j < attributes.Length; j++)
                {
                    ArchetypeAttribute attr = attributes[j];
                    if (attr == null)
                    {
                        continue;
                    }

                    entries.Add(new ArchetypeCacheEntry
                    {
                        concreteTypeName = type.Name,
                        concreteTypeFullName = type.FullName,
                        interfaceTypeFullName = attr.InterfaceType?.FullName,
                        archetypeName = attr.ArchetypeName,
                        archetypeField = attr.ArchetypeField
                    });
                }
            }
        }

        private static void TryAutoGenerate()
        {
            ArchetypeSettings settings
                = ArchetypeSettingsProvider.GetOrCreateSettings();

            if (settings == null || !settings.autoGenerateOnReload)
            {
                return;
            }

            List<ArchetypeCacheEntry> entries = ScanCurrentEntries();
            string hash = ArchetypeHashCache.ComputeHash(entries);
            ArchetypeCache cache = ArchetypeHashCache.Load();

            bool needsGeneration = cache == null
                || cache.hash != hash
                || !AllFilesExist(cache.files);

            if (!needsGeneration)
            {
                return;
            }

            // Generate
            List<ArchetypeCodeGenerator.GenerationResult> results
                = ArchetypeCodeGenerator.GenerateAll(entries, settings);

            // Remove stale files
            if (cache != null && cache.files != null)
            {
                RemoveStaleFiles(
                    cache.files,
                    results,
                    settings.FullGeneratedFolder);
            }

            // Update cache
            List<string> newFiles = new List<string>();
            bool hasError = false;

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Failed)
                {
                    hasError = true;
                    Debug.LogError(
                        $"[Archetype] Failed to generate "
                        + $"'{results[i].ArchetypeName}': "
                        + results[i].ErrorMessage);
                }
                else if (results[i].Written)
                {
                    newFiles.Add(results[i].FilePath);
                }
            }

            ArchetypeCache newCache = new ArchetypeCache
            {
                version = 1,
                hash = hash,
                generatedAt = DateTime.UtcNow.ToString("s"),
                generationStatus = hasError ? "completed_with_errors"
                    : "succeeded",
                files = newFiles,
                entries = entries
            };

            ArchetypeHashCache.Save(newCache);

            int writtenCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Written)
                {
                    writtenCount++;
                }
            }

            Debug.Log(
                $"[Archetype] Generation complete: "
                + $"{writtenCount} files written, "
                + $"{entries.Count} archetype entries");

            if (writtenCount > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private static bool AllFilesExist(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < files.Count; i++)
            {
                if (string.IsNullOrEmpty(files[i]))
                {
                    continue;
                }

                if (!System.IO.File.Exists(files[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RemoveStaleFiles(
            List<string> oldFiles,
            List<ArchetypeCodeGenerator.GenerationResult> results,
            string folder)
        {
            HashSet<string> currentFiles
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < results.Count; i++)
            {
                if (!string.IsNullOrEmpty(results[i].FilePath))
                {
                    currentFiles.Add(results[i].FilePath);
                }
            }

            for (int i = 0; i < oldFiles.Count; i++)
            {
                string oldFile = oldFiles[i];
                if (string.IsNullOrEmpty(oldFile))
                {
                    continue;
                }

                if (currentFiles.Contains(oldFile))
                {
                    continue;
                }

                try
                {
                    if (System.IO.File.Exists(oldFile))
                    {
                        System.IO.File.Delete(oldFile);
                        string metaFile = oldFile + ".meta";
                        if (System.IO.File.Exists(metaFile))
                        {
                            System.IO.File.Delete(metaFile);
                        }

                        Debug.Log(
                            $"[Archetype] Removed stale generated file: "
                            + oldFile);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[Archetype] Failed to remove stale file "
                        + $"'{oldFile}'. Error: {ex.Message}");
                }
            }
        }
    }
}
