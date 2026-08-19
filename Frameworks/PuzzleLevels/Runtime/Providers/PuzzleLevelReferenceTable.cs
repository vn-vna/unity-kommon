using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
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
        }

        [SerializeField]
        private List<Entry> _entries = new List<Entry>();

#if UNITY_EDITOR
        [SerializeField]
        private bool _enableEntryAutoId;

        [SerializeField]
        private string _entryAutoIdTemplate;
#endif

        private Dictionary<string, TextAsset> _lookup;

        private void OnEnable()
        {
            BuildLookup();
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<string, TextAsset>();
            if (_entries == null)
            {
                return;
            }

            foreach (Entry entry in _entries)
            {
                if (!string.IsNullOrEmpty(entry.Id)
                    && entry.Asset != null
                    && !_lookup.ContainsKey(entry.Id))
                {
                    _lookup[entry.Id] = entry.Asset;
                }
            }
        }

        public TextAsset RequestResourceById(string id)
        {
            if (_lookup != null
                && _lookup.TryGetValue(id, out TextAsset asset))
            {
                return asset;
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_enableEntryAutoId)
            {
                TriggerRefreshAutoEntryId();
            }
        }

        [ContextMenu("Refresh Ids")]
        private void TriggerRefreshAutoEntryId()
        {
            if (string.IsNullOrEmpty(_entryAutoIdTemplate) || _entries == null)
            {
                return;
            }

            DateTime now = DateTime.Now;

            for (int i = 0; i < _entries.Count; i++)
            {
                UpdateEntryIdAtIndex(i, now);
            }

            BuildLookup();
        }

        private void UpdateEntryIdAtIndex(int index, DateTime dateTime)
        {
            Entry entry = _entries[index];
            if (entry.Asset == null) return;

            string fileName = entry.Asset.name;
            string newId = FormatEntryId(_entryAutoIdTemplate, index, fileName, dateTime);

            if (entry.Id != newId)
            {
                entry.Id = newId;
                _entries[index] = entry; // Reassign struct back to list
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        private static string FormatEntryId(string template, int index, string fileName, DateTime dateTime)
        {
            // Token regex matches single-curly placeholders while ignoring {{ and }}
            var placeholderRegex = new System.Text.RegularExpressions.Regex(@"(?<!\{)\{([^{}]+)\}(?!\})");

            string evaluated = placeholderRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value.Trim();
                return ResolvePlaceholder(tag, index, fileName, dateTime, match.Value);
            });

            return UnescapeBrackets(evaluated);
        }

        private static string ResolvePlaceholder(string tag, int index, string fileName, DateTime dateTime, string defaultValue)
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

            Debug.LogWarning($"[PuzzleLevelReferenceTable] Invalid index offset format: '{rawOffset}'");
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

            return ExtractRegexGroup(fileName, pattern, groupIdentifier);
        }

        private static string ExtractRegexGroup(string input, string pattern, string groupIdentifier)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, pattern);
                if (!match.Success) return string.Empty;

                if (int.TryParse(groupIdentifier, out int groupIdx))
                {
                    return groupIdx < match.Groups.Count ? match.Groups[groupIdx].Value : string.Empty;
                }

                return match.Groups[groupIdentifier].Success ? match.Groups[groupIdentifier].Value : string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PuzzleLevelReferenceTable] Invalid Regex execution for group '{groupIdentifier}': {ex.Message}");
                return string.Empty;
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
                Debug.LogWarning($"[PuzzleLevelReferenceTable] Invalid DateTime format '{format}': {ex.Message}");
                return dateTime.ToString("yyMMdd-HHmmss");
            }
        }

        private static string UnescapeBrackets(string input)
        {
            return input.Replace("{{", "{").Replace("}}", "}");
        }
#endif
    }
}
