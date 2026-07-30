using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public class AchievementDefinition : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Unique ID matching the provider's achievement key")]
#endif
        [SerializeField]
        private string _id;

#if UNITY_EDITOR
        [Tooltip("Human-readable display name")]
#endif
        [SerializeField]
        private string _displayName;

#if UNITY_EDITOR
        [Tooltip("Description of the achievement")]
#endif
        [SerializeField]
        private string _description;

#if UNITY_EDITOR
        [Tooltip("OneTime = unlock once; Upgradable = multi-level progress")]
#endif
        [SerializeField]
        private AchievementType _type;

#if UNITY_EDITOR
        [Tooltip("Total number of steps (Upgradable only)")]
#endif
        [SerializeField]
        private int _maxSteps = 1;

#if UNITY_EDITOR
        [Tooltip("Points/units needed per step (Upgradable only)")]
#endif
        [SerializeField]
        private long _incrementValue = 1;

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

        public string Description
        {
            get => _description;
            set => _description = value;
        }

        public AchievementType Type
        {
            get => _type;
            set => _type = value;
        }

        public int MaxSteps
        {
            get => _maxSteps;
            set => _maxSteps = value;
        }

        public long IncrementValue
        {
            get => _incrementValue;
            set => _incrementValue = value;
        }

        public long TotalTarget => _maxSteps * _incrementValue;
    }
}
