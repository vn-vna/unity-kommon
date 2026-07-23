using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
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
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Pre-configured level overrides applied at startup.")]
#endif
        [SerializeField]
        private List<PuzzleLevelOverrideEntry> _entries
            = new List<PuzzleLevelOverrideEntry>();

        #endregion

        #region Properties

        public IReadOnlyList<PuzzleLevelOverrideEntry> Entries => _entries;
        public int Count => _entries.Count;

        #endregion

        #region Public Methods (Editor)

        public void AddEntry(PuzzleLevelOverrideEntry entry)
        {
            _entries.Add(entry);
        }

        public void RemoveEntry(int index)
        {
            if (index >= 0 && index < _entries.Count)
            {
                _entries.RemoveAt(index);
            }
        }

        public void ClearEntries()
        {
            _entries.Clear();
        }

        #endregion
    }
}
