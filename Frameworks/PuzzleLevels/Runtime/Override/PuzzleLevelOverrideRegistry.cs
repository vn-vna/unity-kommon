using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    public class PuzzleLevelOverrideRegistry : MonoBehaviour
    {
        #region Interfaces & Properties

        public int Count => _overrides.Count;

        #endregion

        #region Private Fields

        private readonly ConcurrentDictionary<string, IPuzzleLevelData> _overrides
            = new ConcurrentDictionary<string, IPuzzleLevelData>();
        private readonly object _snapshotLock = new object();
        private IReadOnlyCollection<string> _cachedIdSnapshot
            = Array.AsReadOnly(Array.Empty<string>());
        private int _version;
        private int _snapshotVersion = -1;

        #endregion

        #region Public Methods

        public bool SetOverride(string levelId, IPuzzleLevelData levelData)
        {
            if (string.IsNullOrWhiteSpace(levelId))
            {
                QuickLog.Warning<PuzzleLevelOverrideRegistry>(
                    "Cannot set override with null or empty level ID.");
                return false;
            }

            if (levelData == null)
            {
                QuickLog.Warning<PuzzleLevelOverrideRegistry>(
                    "Cannot set null override data for level '{0}'.",
                    levelId);
                return false;
            }

            if (!string.Equals(
                    levelId,
                    levelData.LevelId,
                    System.StringComparison.Ordinal))
            {
                QuickLog.Warning<PuzzleLevelOverrideRegistry>(
                    "Override key '{0}' does not match data level ID '{1}'.",
                    levelId,
                    levelData.LevelId);
                return false;
            }

            _overrides[levelId] = levelData;
            Interlocked.Increment(ref _version);

            QuickLog.Info<PuzzleLevelOverrideRegistry>(
                "Override set for level '{0}'. Total overrides: {1}",
                levelId, _overrides.Count
            );
            return true;
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
                Interlocked.Increment(ref _version);
                QuickLog.Info<PuzzleLevelOverrideRegistry>(
                    "Override removed for level '{0}'.", levelId
                );
            }

            return removed;
        }

        public void Clear()
        {
            if (_overrides.IsEmpty)
            {
                return;
            }

            _overrides.Clear();
            Interlocked.Increment(ref _version);
            QuickLog.Info<PuzzleLevelOverrideRegistry>(
                "All overrides cleared."
            );
        }

        public IReadOnlyCollection<string> GetOverriddenIds()
        {
            int version = Volatile.Read(ref _version);
            if (_snapshotVersion == version)
            {
                return _cachedIdSnapshot;
            }

            lock (_snapshotLock)
            {
                version = Volatile.Read(ref _version);
                if (_snapshotVersion == version)
                {
                    return _cachedIdSnapshot;
                }

                List<string> ids = new List<string>(_overrides.Keys);
                ids.Sort(StringComparer.Ordinal);
                _cachedIdSnapshot = new ReadOnlyCollection<string>(ids);
                _snapshotVersion = version;
                return _cachedIdSnapshot;
            }
        }

        #endregion
    }
}
