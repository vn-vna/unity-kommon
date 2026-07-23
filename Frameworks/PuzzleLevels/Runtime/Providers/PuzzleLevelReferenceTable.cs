using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelReferenceTable",
        menuName = "Scheherazade/Puzzle Levels/Providers/Reference Table Asset"
    )]
    public sealed class PuzzleLevelReferenceTable :
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
    }
}
