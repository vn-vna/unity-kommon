using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{

    [CreateAssetMenu(
        fileName = "AchievementConfiguration",
        menuName = "Scheherazade/Achievement/Configuration")]
    public class AchievementConfiguration : ScriptableObject
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
