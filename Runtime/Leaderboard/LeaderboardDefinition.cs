using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class LeaderboardDefinition : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Unique ID matching the provider's leaderboard key")]
#endif
        [SerializeField]
        private string _id;

#if UNITY_EDITOR
        [Tooltip("Human-readable display name")]
#endif
        [SerializeField]
        private string _displayName;

#if UNITY_EDITOR
        [Tooltip("Sort direction for ranking")]
#endif
        [SerializeField]
        private SortOrder _sortOrder = SortOrder.Descending;

#if UNITY_EDITOR
        [Tooltip("How often leaderboard scores reset")]
#endif
        [SerializeField]
        private ResetCadence _resetCadence = ResetCadence.None;

        public string Id
        {
            get => _id;
            set => _id = value;
        }

        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        public SortOrder SortOrder
        {
            get => _sortOrder;
            set => _sortOrder = value;
        }

        public ResetCadence ResetCadence
        {
            get => _resetCadence;
            set => _resetCadence = value;
        }
    }
}
