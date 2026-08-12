using System;
using System.Collections.Generic;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Maps TagDefinition types to their associated ITagData types
    /// via the [TagData] attribute. Also tracks which tags are markers
    /// (no data class needed).
    /// </summary>
    public static class TagDataRegistry
    {
        private static readonly Dictionary<Type, Type> _tagDefToData
            = new Dictionary<Type, Type>();

        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            ScanAssemblies();
        }

        private static void ScanAssemblies()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    var attr = type.GetCustomAttribute<TagDataAttribute>();
                    if (attr == null) continue;
                    _tagDefToData[attr.TagDefinitionType] = type;
                }
            }

            QuickLog.Log(
                "Registered {0} tag data types",
                typeof(TagDataRegistry).Name,
                LogLevel.Info,
                new object[] { _tagDefToData.Count });
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
            => !_tagDefToData.ContainsKey(tagDefType);

        /// <summary>Get the TagDefinition type for a data type.</summary>
        public static Type GetTagDefType(Type dataType)
        {
            EnsureInitialized();
            foreach (var kvp in _tagDefToData)
            {
                if (kvp.Value == dataType) return kvp.Key;
            }

            return null;
        }
    }
}
