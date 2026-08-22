using System;
using System.Collections.Generic;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Maps TagDefinition types to their associated ITagData types
    /// via the [TagData] attribute. Also tracks which tags are markers
    /// (no data class needed).
    /// </summary>
    public static class TagDataRegistry
    {
        private static readonly object SyncRoot = new object();

        private static Dictionary<Type, Type> _tagDefToData
            = new Dictionary<Type, Type>();
        private static Dictionary<Type, Type> _dataToTagDef
            = new Dictionary<Type, Type>();
        private static Dictionary<string, Type> _idToTagDef
            = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static Dictionary<Type, string> _tagDefToId
            = new Dictionary<Type, string>();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (SyncRoot)
            {
                if (_initialized) return;
                ScanAssemblies();
                _initialized = true;
            }
        }

        private static void ScanAssemblies()
        {
            var tagDefToData = new Dictionary<Type, Type>();
            var dataToTagDef = new Dictionary<Type, Type>();
            var idToTagDef = new Dictionary<string, Type>(StringComparer.Ordinal);
            var tagDefToId = new Dictionary<Type, string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = GetLoadableTypes(assembly);

                foreach (var type in types)
                {
                    if (type == null) continue;

                    if (!type.IsAbstract
                        && typeof(TagDefinition).IsAssignableFrom(type))
                    {
                        RegisterTagId(
                            type,
                            idToTagDef,
                            tagDefToId,
                            requireExplicitId: false
                        );
                    }

                    var attr = type.GetCustomAttribute<TagDataAttribute>();
                    if (attr == null) continue;

                    ValidateMapping(type, attr.TagDefinitionType);
                    RegisterTagId(
                        attr.TagDefinitionType,
                        idToTagDef,
                        tagDefToId,
                        requireExplicitId: true
                    );
                    if (tagDefToData.ContainsKey(attr.TagDefinitionType))
                    {
                        throw new ItemDatabaseException(
                            $"Tag '{attr.TagDefinitionType.FullName}' has multiple data mappings."
                        );
                    }

                    if (dataToTagDef.ContainsKey(type))
                    {
                        throw new ItemDatabaseException(
                            $"Data type '{type.FullName}' has multiple tag mappings."
                        );
                    }

                    tagDefToData.Add(attr.TagDefinitionType, type);
                    dataToTagDef.Add(type, attr.TagDefinitionType);
                }
            }

            _tagDefToData = tagDefToData;
            _dataToTagDef = dataToTagDef;
            _idToTagDef = idToTagDef;
            _tagDefToId = tagDefToId;

            QuickLog.Log(
                "Registered {0} tag data types",
                typeof(TagDataRegistry).Name,
                LogLevel.Info,
                new object[] { _tagDefToData.Count }
            );
        }

        /// <summary>Get the data type for a TagDefinition, or null if marker.</summary>
        public static Type GetDataType(Type tagDefType)
        {
            EnsureInitialized();
            _tagDefToData.TryGetValue(tagDefType, out var dataType);
            return dataType;
        }

        /// <summary>True if this TagDefinition has no associated data (marker).</summary>
        public static bool IsMarkerTag(Type tagDefType)
        {
            EnsureInitialized();
            return !_tagDefToData.ContainsKey(tagDefType);
        }

        /// <summary>Get the TagDefinition type for a data type.</summary>
        public static Type GetTagDefType(Type dataType)
        {
            EnsureInitialized();
            _dataToTagDef.TryGetValue(dataType, out Type tagDefinitionType);
            return tagDefinitionType;
        }

        public static string GetPersistentId(Type tagDefinitionType)
        {
            EnsureInitialized();
            _tagDefToId.TryGetValue(tagDefinitionType, out string id);
            return id;
        }

        public static Type ResolveTagDefinitionType(
            string tagId,
            string legacyTypeName = null)
        {
            EnsureInitialized();
            if (!string.IsNullOrEmpty(tagId))
            {
                if (!_idToTagDef.TryGetValue(tagId, out Type resolved))
                {
                    return null;
                }

                Type legacyType = ResolveLegacyTagType(legacyTypeName);
                if (legacyType != null && legacyType != resolved) return null;
                return resolved;
            }

            return ResolveLegacyTagType(legacyTypeName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            lock (SyncRoot)
            {
                _tagDefToData = new Dictionary<Type, Type>();
                _dataToTagDef = new Dictionary<Type, Type>();
                _idToTagDef = new Dictionary<string, Type>(StringComparer.Ordinal);
                _tagDefToId = new Dictionary<Type, string>();
                _initialized = false;
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                QuickLog.Log(
                    "Partial type load for assembly '{0}': {1}",
                    nameof(TagDataRegistry),
                    LogLevel.Warning,
                    new object[]
                    {
                        assembly.GetName().Name,
                        exception.Message
                    }
                );
                return exception.Types ?? Array.Empty<Type>();
            }
            catch (Exception exception)
            {
                QuickLog.Log(
                    "Could not scan assembly '{0}': {1}",
                    nameof(TagDataRegistry),
                    LogLevel.Warning,
                    new object[]
                    {
                        assembly.GetName().Name,
                        exception.Message
                    }
                );
                return Array.Empty<Type>();
            }
        }

        private static void RegisterTagId(
            Type tagDefinitionType,
            Dictionary<string, Type> idToTagDef,
            Dictionary<Type, string> tagDefToId,
            bool requireExplicitId)
        {
            ItemTagIdAttribute attribute = tagDefinitionType
                .GetCustomAttribute<ItemTagIdAttribute>();
            string id = attribute?.Id;

            if (string.IsNullOrWhiteSpace(id))
            {
                if (!requireExplicitId) return;
                throw new ItemDatabaseException(
                    $"Tag '{tagDefinitionType.Name}' has no persistent identifier."
                );
            }

            if (idToTagDef.TryGetValue(id, out Type existing)
                && existing != tagDefinitionType)
            {
                throw new ItemDatabaseException(
                    $"Persistent tag ID '{id}' is used by both "
                    + $"'{existing.FullName}' and '{tagDefinitionType.FullName}'."
                );
            }

            idToTagDef[id] = tagDefinitionType;
            tagDefToId[tagDefinitionType] = id;
        }

        private static Type ResolveLegacyTagType(string legacyTypeName)
        {
            if (string.IsNullOrEmpty(legacyTypeName)) return null;

            Type legacyType = Type.GetType(legacyTypeName);
            return legacyType != null
                && typeof(TagDefinition).IsAssignableFrom(legacyType)
                ? legacyType
                : null;
        }

        private static void ValidateMapping(Type dataType, Type tagDefinitionType)
        {
            if (tagDefinitionType == null
                || !typeof(TagDefinition).IsAssignableFrom(tagDefinitionType)
                || tagDefinitionType.IsAbstract)
            {
                throw new ItemDatabaseException(
                    $"Tag data '{dataType.FullName}' targets an invalid TagDefinition."
                );
            }

            if (!typeof(ITagData).IsAssignableFrom(dataType)
                || dataType.IsAbstract
                || !dataType.IsSerializable)
            {
                throw new ItemDatabaseException(
                    $"Tag data '{dataType.FullName}' must be a concrete [Serializable] ITagData type."
                );
            }
        }
    }
}
