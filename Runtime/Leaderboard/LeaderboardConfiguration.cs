using System;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{

    [CreateAssetMenu(
        fileName = "LeaderboardConfiguration",
        menuName = "Scheherazade/Leaderboard/Configuration")]
    public class LeaderboardConfiguration : ScriptableObject
    {
        #region Serialized Fields

        [SerializeField]
        private ScriptableObject _provider;

        [SerializeField]
        private LeaderboardDefinition[] _leaderboards;

        #endregion

        #region Properties

        public ILeaderboardProvider Provider
            => _provider as ILeaderboardProvider;

        public ScriptableObject ProviderAsset
        {
            get => _provider;
            set => _provider = value;
        }

        public LeaderboardDefinition[] Leaderboards
        {
            get => _leaderboards ?? System.Array.Empty<LeaderboardDefinition>();
            set => _leaderboards = value ?? System.Array.Empty<LeaderboardDefinition>();
        }

        #endregion
    }
}
