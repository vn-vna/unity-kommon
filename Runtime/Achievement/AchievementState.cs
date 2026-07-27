using System;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    [Serializable]
    public struct AchievementState
    {
        public string Id;
        public bool IsUnlocked;
        public double Progress;
        public int CurrentStep;
        public DateTime? UnlockedAt;
        public DateTime? LastUpdatedAt;

        public AchievementState(
            string id,
            bool isUnlocked,
            double progress,
            int currentStep,
            DateTime? unlockedAt,
            DateTime? lastUpdatedAt)
        {
            Id = id;
            IsUnlocked = isUnlocked;
            Progress = progress;
            CurrentStep = currentStep;
            UnlockedAt = unlockedAt;
            LastUpdatedAt = lastUpdatedAt;
        }
    }
}
