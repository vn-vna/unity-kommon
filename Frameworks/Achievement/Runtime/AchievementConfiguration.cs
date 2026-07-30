using System;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public class AchievementConfiguration : SingletonScriptableObject<AchievementConfiguration>
    {
        #region Serialized Fields

        [SerializeField]
        private ScriptableObject _provider;

        [SerializeField]
        private AchievementDefinition[] _achievements;

        #endregion

        #region Properties

        public IAchievementProvider Provider
            => _provider as IAchievementProvider;

        public ScriptableObject ProviderAsset
        {
            get => _provider;
            set => _provider = value;
        }

        public AchievementDefinition[] Achievements
        {
            get => _achievements ?? Array.Empty<AchievementDefinition>();
            set => _achievements = value ?? Array.Empty<AchievementDefinition>();
        }

        #endregion
    }
}
