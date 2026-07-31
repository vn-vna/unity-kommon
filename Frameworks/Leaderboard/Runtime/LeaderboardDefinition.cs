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
        [Tooltip("Leaderboard type determines default sort direction and display format")]
#endif
        [SerializeField]
        private LeaderboardType _type = LeaderboardType.Point;

#if UNITY_EDITOR
        [Tooltip("Sort direction for ranking")]
#endif
        [SerializeField]
        private LeaderboardSortingOrder _sortOrder = LeaderboardSortingOrder.Descending;

#if UNITY_EDITOR
        [Tooltip("How often leaderboard scores reset")]
#endif
        [SerializeField]
        private LeaderboardCadence _resetCadence = LeaderboardCadence.None;

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

        public LeaderboardType Type
        {
            get => _type;
            set => _type = value;
        }

        public LeaderboardSortingOrder SortOrder
        {
            get => _sortOrder;
            set => _sortOrder = value;
        }

        public LeaderboardCadence ResetCadence
        {
            get => _resetCadence;
            set => _resetCadence = value;
        }
    }
}
