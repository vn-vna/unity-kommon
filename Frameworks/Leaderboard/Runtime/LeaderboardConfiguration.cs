using System;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [SingletonScriptableConfig(
        ScriptableLoadSource.Resources,
        "Integration/Managers/LeaderboardConfiguration"
    )]
    public class LeaderboardConfiguration :
        SingletonScriptableObject<LeaderboardConfiguration>
    {
        #region Serialized Fields

        [SerializeField]
        private ScriptableObject _androidProvider;

        [SerializeField]
        private ScriptableObject _iosProvider;

        [SerializeField]
        private LeaderboardDefinition[] _leaderboards;

        #endregion

        #region Properties

        public ILeaderboardProvider Provider
        {
            get
            {
#if UNITY_ANDROID
                return _androidProvider as ILeaderboardProvider;
#elif UNITY_IOS
                return _iosProvider as ILeaderboardProvider;
#else
                return (_androidProvider ?? _iosProvider) as ILeaderboardProvider;
#endif
            }
        }

        public ScriptableObject AndroidProvider
        {
            get => _androidProvider;
            set => _androidProvider = value;
        }

        public ScriptableObject IosProvider
        {
            get => _iosProvider;
            set => _iosProvider = value;
        }

        public bool HasAnyProvider => _androidProvider != null || _iosProvider != null;

        public LeaderboardDefinition[] Leaderboards
        {
            get => _leaderboards ?? System.Array.Empty<LeaderboardDefinition>();
            set => _leaderboards = value ?? System.Array.Empty<LeaderboardDefinition>();
        }

        #endregion
    }
}
