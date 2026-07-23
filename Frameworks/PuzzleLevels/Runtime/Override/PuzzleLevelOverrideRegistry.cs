using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    public class PuzzleLevelOverrideRegistry : MonoBehaviour
    {
        private readonly ConcurrentDictionary<string, IPuzzleLevelData> _overrides
            = new ConcurrentDictionary<string, IPuzzleLevelData>();

        public int Count => _overrides.Count;

        public void SetOverride(string levelId, IPuzzleLevelData levelData)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                QuickLog.Warning<PuzzleLevelOverrideRegistry>(
                    "Cannot set override with null or empty level ID.");
                return;
            }

            _overrides[levelId] = levelData;

            QuickLog.Info<PuzzleLevelOverrideRegistry>(
                "Override set for level '{0}'. Total overrides: {1}",
                levelId, _overrides.Count
            );
        }

        public IPuzzleLevelData TryGet(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                return null;
            }

            _overrides.TryGetValue(levelId, out IPuzzleLevelData data);
            return data;
        }

        public bool RemoveOverride(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                return false;
            }

            bool removed = _overrides.TryRemove(levelId, out _);
            if (removed)
            {
                QuickLog.Info<PuzzleLevelOverrideRegistry>(
                    "Override removed for level '{0}'.", levelId
                );
            }

            return removed;
        }

        public void Clear()
        {
            _overrides.Clear();
            QuickLog.Info<PuzzleLevelOverrideRegistry>(
                "All overrides cleared."
            );
        }

        public IReadOnlyCollection<string> GetOverriddenIds()
        {
            return _overrides.Keys.ToList();
        }
    }
}
