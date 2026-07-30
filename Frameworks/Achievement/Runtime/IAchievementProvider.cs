using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public interface IAchievementProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }
        Task<bool> InitializeAsync();

        Task UnlockAsync(
            string achievementId,
            CancellationToken ct = default);

        Task IncrementProgressAsync(
            string achievementId,
            int steps,
            int maxSteps,
            CancellationToken ct = default);

        Task<AchievementState> GetStateAsync(
            string achievementId,
            CancellationToken ct = default);

        Task<AchievementState[]> GetAllStatesAsync(
            CancellationToken ct = default);
    }
}
