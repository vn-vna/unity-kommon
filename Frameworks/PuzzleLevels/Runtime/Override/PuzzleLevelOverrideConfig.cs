using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [Serializable]
    public struct PuzzleLevelOverrideEntry
    {
#if UNITY_EDITOR
        [Tooltip("The level identifier to override.")]
#endif
        public string LevelId;

#if UNITY_EDITOR
        [Tooltip("The replacement asset for this level.")]
#endif
        public TextAsset OverrideAsset;

#if UNITY_EDITOR
        [Tooltip("How the asset data should be interpreted.")]
#endif
        public DataType DataType;
    }

    [CreateAssetMenu(
        fileName = "PuzzleLevelOverrideConfig",
        menuName = "Scheherazade/Puzzle Levels/Override Config"
    )]
    public class PuzzleLevelOverrideConfig : ScriptableObject
    {
        #region Interfaces & Properties

        public IReadOnlyList<PuzzleLevelOverrideEntry> Entries =>
            _entries
            ?? (IReadOnlyList<PuzzleLevelOverrideEntry>)Array.Empty<
                PuzzleLevelOverrideEntry>();
        public int Count => _entries?.Count ?? 0;

        #endregion

        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Pre-configured level overrides applied at startup.")]
#endif
        [SerializeField]
        private List<PuzzleLevelOverrideEntry> _entries
            = new List<PuzzleLevelOverrideEntry>();

        #endregion

        #region Unity Callbacks

#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateEntries();
        }
#endif

        #endregion

        #region Public Methods (Editor)

        public void AddEntry(PuzzleLevelOverrideEntry entry)
        {
            _entries ??= new List<PuzzleLevelOverrideEntry>();
            _entries.Add(entry);
        }

        public void RemoveEntry(int index)
        {
            if (_entries != null && index >= 0 && index < _entries.Count)
            {
                _entries.RemoveAt(index);
            }
        }

        public void ClearEntries()
        {
            _entries?.Clear();
        }

        #endregion

#if UNITY_EDITOR
        #region Private Methods

        private void ValidateEntries()
        {
            if (_entries == null)
            {
                return;
            }

            HashSet<string> levelIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int i = 0; i < _entries.Count; i++)
            {
                PuzzleLevelOverrideEntry entry = _entries[i];
                if (string.IsNullOrWhiteSpace(entry.LevelId))
                {
                    QuickLog.Error<PuzzleLevelOverrideConfig>(
                        "Override entry {0} has an empty level ID.",
                        i);
                    continue;
                }

                if (!levelIds.Add(entry.LevelId))
                {
                    QuickLog.Error<PuzzleLevelOverrideConfig>(
                        "Duplicate override level ID '{0}' at entry {1}.",
                        entry.LevelId,
                        i);
                }

                if (entry.OverrideAsset == null)
                {
                    QuickLog.Error<PuzzleLevelOverrideConfig>(
                        "Override entry '{0}' has no TextAsset.",
                        entry.LevelId);
                }

                if (!Enum.IsDefined(typeof(DataType), entry.DataType))
                {
                    QuickLog.Error<PuzzleLevelOverrideConfig>(
                        "Override entry '{0}' has invalid data type value {1}.",
                        entry.LevelId,
                        (int)entry.DataType);
                }
                else if (entry.DataType == DataType.Unknown)
                {
                    QuickLog.Warning<PuzzleLevelOverrideConfig>(
                        "Override entry '{0}' has no explicit data type and "
                        + "will default to Text.",
                        entry.LevelId);
                }
            }
        }

        #endregion
#endif
    }
}
