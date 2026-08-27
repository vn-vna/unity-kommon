using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelReferenceTable",
        menuName = "Scheherazade/Puzzle Levels/Providers/Reference Table Asset"
    )]
    public class PuzzleLevelReferenceTable :
        ScriptableObject,
        IAsyncResourceReferenceTable<TextAsset>
    {
        [Serializable]
        public struct Entry
        {
            public string Id;
            public TextAsset Asset;
            public DataType DataType;
        }

        [SerializeField]
        private List<Entry> _entries = new List<Entry>();

#if UNITY_EDITOR
        [SerializeField]
        private bool _enableEntryAutoId;

        [SerializeField]
        private string _entryAutoIdTemplate;

        [SerializeField]
        private bool _enableEntryAutoIdRegex;

        [SerializeField]
        private string _entryAutoIdRegexPattern;
#endif

        private Dictionary<string, TextAsset> _lookup;
        private Dictionary<string, DataType> _typeLookup;
        private string[] _catalogedIds = Array.Empty<string>();

        private static readonly Regex PlaceholderRegex = new Regex(
            @"(?<!\{)\{([^{}]+)\}(?!\})",
            RegexOptions.Compiled);

        public IReadOnlyCollection<string> CatalogedIds => _catalogedIds;

        private void OnEnable()
        {
            BuildLookup();
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<string, TextAsset>();
            _typeLookup = new Dictionary<string, DataType>();
            List<string> catalogedIds = new List<string>();
            if (_entries == null)
            {
                _catalogedIds = Array.Empty<string>();
                return;
            }

            foreach (Entry entry in _entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)
                    || entry.Asset == null
                    || _lookup.ContainsKey(entry.Id))
                {
                    continue;
                }

                _lookup[entry.Id] = entry.Asset;
                _typeLookup[entry.Id] = entry.DataType == DataType.Unknown
                    ? DataType.Text
                    : entry.DataType;
                catalogedIds.Add(entry.Id);
            }

            _catalogedIds = catalogedIds.ToArray();
        }

        public TextAsset RequestResourceById(string id)
        {
            _lookup ??= new Dictionary<string, TextAsset>();
            if (_lookup != null
                && _lookup.TryGetValue(id, out TextAsset asset))
            {
                return asset;
            }

            return null;
        }

        public bool HasResource(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                && _lookup != null
                && _lookup.ContainsKey(id);
        }

        public DataType GetDataType(string id)
        {
            return _typeLookup != null
                && !string.IsNullOrWhiteSpace(id)
                && _typeLookup.TryGetValue(id, out DataType dataType)
                    ? dataType
                    : DataType.Unknown;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            BuildLookup();
        }

        [ContextMenu("Refresh Ids")]
        public void RefreshEntryIds()
        {
            if (!_enableEntryAutoId
                || string.IsNullOrEmpty(_entryAutoIdTemplate)
                || _entries == null)
            {
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Refresh Puzzle Level IDs");
            DateTime now = DateTime.Now;

            for (int i = 0; i < _entries.Count; i++)
            {
                UpdateEntryIdAtIndex(i, now);
            }

            BuildLookup();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void UpdateEntryIdAtIndex(int index, DateTime dateTime)
        {
            Entry entry = _entries[index];
            if (entry.Asset == null) return;

            string fileName = entry.Asset.name;
            string newId = FormatEntryId(
                _entryAutoIdTemplate,
                index,
                fileName,
                dateTime,
                _enableEntryAutoIdRegex,
                _entryAutoIdRegexPattern
            );

            if (entry.Id != newId)
            {
                entry.Id = newId;
                _entries[index] = entry;
            }
        }

        private static string FormatEntryId(
            string template,
            int index,
            string fileName,
            DateTime dateTime,
            bool isRegexEnabled,
            string regexPattern)
        {
            string evaluated = PlaceholderRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value.Trim();
                return ResolvePlaceholder(
                    tag,
                    index,
                    fileName,
                    dateTime,
                    isRegexEnabled,
                    regexPattern,
                    match.Value
                );
            });

            return UnescapeBrackets(evaluated);
        }

        private static string ResolvePlaceholder(
            string tag,
            int index,
            string fileName,
            DateTime dateTime,
            bool isRegexEnabled,
            string regexPattern,
            string defaultValue)
        {
            if (tag.StartsWith("index", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveIndexTag(tag, index);
            }

            if (tag.Equals("filename", StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            if (tag.StartsWith("filename:", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveFilenameRegexTag(tag, fileName);
            }

            if (tag.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveConfiguredRegexTag(
                    tag,
                    fileName,
                    isRegexEnabled,
                    regexPattern,
                    defaultValue
                );
            }

            if (tag.StartsWith("datetime", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDateTimeTag(tag, dateTime);
            }

            return defaultValue; // Return unhandled tag as-is
        }

        private static string ResolveIndexTag(string tag, int baseIndex)
        {
            if (!tag.Contains(":"))
            {
                return baseIndex.ToString();
            }

            string offsetRaw = tag.Substring(tag.IndexOf(':') + 1).Trim();
            int offset = ParseIndexOffset(offsetRaw);

            return (baseIndex + offset).ToString();
        }

        private static int ParseIndexOffset(string rawOffset)
        {
            // Handles "+1", "-5", or "1"
            string cleanOffset = rawOffset.Replace("+", "");

            if (int.TryParse(cleanOffset, out int offset))
            {
                return offset;
            }

            QuickLog.Warning<PuzzleLevelReferenceTable>(
                "Invalid index offset format: '{0}'",
                rawOffset);
            return 0;
        }

        private static string ResolveFilenameRegexTag(string tag, string fileName)
        {
            string[] parts = tag.Split(new[] { ':' }, 3);
            if (parts.Length < 3)
            {
                return string.Empty;
            }

            string pattern = parts[1];
            string groupIdentifier = parts[2];

            return ExtractRegexGroup(
                fileName,
                pattern,
                groupIdentifier,
                string.Empty
            );
        }

        private static string ResolveConfiguredRegexTag(
            string tag,
            string fileName,
            bool isRegexEnabled,
            string regexPattern,
            string defaultValue)
        {
            string groupDefinition = tag.Substring("regex:".Length);
            int fallbackSeparatorIndex = groupDefinition.IndexOf('|');
            string groupIdentifier = fallbackSeparatorIndex < 0
                ? groupDefinition.Trim()
                : groupDefinition.Substring(0, fallbackSeparatorIndex).Trim();
            string emptyValueFallback = fallbackSeparatorIndex < 0
                ? defaultValue
                : groupDefinition.Substring(fallbackSeparatorIndex + 1);
            if (!isRegexEnabled || string.IsNullOrWhiteSpace(regexPattern))
            {
                return emptyValueFallback;
            }

            return string.IsNullOrWhiteSpace(groupIdentifier)
                ? emptyValueFallback
                : ExtractRegexGroup(
                    fileName,
                    regexPattern,
                    groupIdentifier,
                    emptyValueFallback
                );
        }

        private static string ExtractRegexGroup(
            string input,
            string pattern,
            string groupIdentifier,
            string fallback)
        {
            try
            {
                Match match = Regex.Match(input, pattern);
                if (!match.Success) return fallback;

                if (int.TryParse(groupIdentifier, out int groupIdx))
                {
                    if (groupIdx >= match.Groups.Count)
                    {
                        return fallback;
                    }

                    string groupValue = match.Groups[groupIdx].Value;
                    return string.IsNullOrEmpty(groupValue)
                        ? fallback
                        : groupValue;
                }

                Group group = match.Groups[groupIdentifier];
                return group.Success && !string.IsNullOrEmpty(group.Value)
                    ? group.Value
                    : fallback;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<PuzzleLevelReferenceTable>(
                    "Invalid regex for group '{0}': {1}",
                    groupIdentifier,
                    ex.Message);
                return fallback;
            }
        }

        private static string ResolveDateTimeTag(string tag, DateTime dateTime)
        {
            string format = tag.Contains(":")
                ? tag.Substring(tag.IndexOf(':') + 1)
                : "yyMMdd-HHmmss";

            try
            {
                return dateTime.ToString(format);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<PuzzleLevelReferenceTable>(
                    "Invalid DateTime format '{0}': {1}",
                    format,
                    ex.Message);
                return dateTime.ToString("yyMMdd-HHmmss");
            }
        }

        private static string UnescapeBrackets(string input)
        {
            return input.Replace("{{", "{").Replace("}}", "}");
        }

        private void ValidateEntries()
        {
            if (_entries == null)
            {
                return;
            }

            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    QuickLog.Warning<PuzzleLevelReferenceTable>(
                        "Entry {0} has an empty level ID.",
                        i);
                    continue;
                }

                if (!ids.Add(entry.Id))
                {
                    QuickLog.Error<PuzzleLevelReferenceTable>(
                        "Duplicate level ID '{0}' at entry {1}.",
                        entry.Id,
                        i);
                }

                if (entry.Asset == null)
                {
                    QuickLog.Warning<PuzzleLevelReferenceTable>(
                        "Entry '{0}' has no TextAsset.",
                        entry.Id);
                }

                if (!Enum.IsDefined(typeof(DataType), entry.DataType))
                {
                    QuickLog.Error<PuzzleLevelReferenceTable>(
                        "Entry '{0}' has invalid data type value {1}.",
                        entry.Id,
                        (int)entry.DataType);
                }
                else if (entry.DataType == DataType.Unknown)
                {
                    QuickLog.Warning<PuzzleLevelReferenceTable>(
                        "Entry '{0}' has no explicit data type and will "
                        + "default to Text.",
                        entry.Id);
                }
            }
        }
#endif
    }
}
