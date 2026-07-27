using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;
using UnityEngine.Events;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    [AddComponentMenu("Scheherazade/Achievement Director")]
    [DontDestroyOnLoad]
    public class AchievementDirector : SingletonBehavior<AchievementDirector>
    {
        #region Constants

        private const string ConfigPath =
            "Integration/Managers/AchievementConfiguration";

        private const double FullProgress = 1.0;

        #endregion

        #region Events & Delegates

        public event Action<string, AchievementState> AchievementUnlocked;
        public event Action<string, double, int> ProgressUpdated;
        public event Action<string, int, int> Upgraded;
        public event Action<string, string> Error;

        #endregion

        #region Inspector Events

        [SerializeField]
        private UnityEvent<string, double, int> _onProgressUpdated =
            new UnityEvent<string, double, int>();

        [SerializeField]
        private UnityEvent<string, int, int> _onUpgraded =
            new UnityEvent<string, int, int>();

        [SerializeField]
        private UnityEvent<string, bool, double> _onAchievementUnlocked =
            new UnityEvent<string, bool, double>();

        [SerializeField]
        private UnityEvent<string, string> _onError =
            new UnityEvent<string, string>();

        #endregion

        #region Static Init

        private static readonly TaskCompletionSource<bool> _readySource =
            new TaskCompletionSource<bool>();

        public static Task ReadyTask => _readySource.Task;

        #endregion

        #region Private Fields

        private AchievementConfiguration _config;
        private IAchievementProvider _activeProvider;

        #endregion

        #region Unity Callbacks

        protected override async void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<AchievementConfiguration>(ConfigPath);

                if (_config != null)
                {
                    _activeProvider = _config.Provider;
                    if (_activeProvider != null)
                    {
                        await _activeProvider.InitializeAsync();
                    }
                }

                if (_activeProvider == null)
                {
                    _activeProvider =
                        ScriptableObject.CreateInstance<LocalAchievementProvider>();
                    await _activeProvider.InitializeAsync();
                }
            }
            finally
            {
                _readySource.TrySetResult(true);
            }
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[Scheherazade Achievement Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<AchievementDirector>();
        }

        #endregion

        #region Public Methods

        public async Task UnlockAsync(
            string achievementId,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            GuardProvider("Unlock");

            try
            {
                await _activeProvider.UnlockAsync(achievementId, ct);
                var state = await _activeProvider.GetStateAsync(
                    achievementId, ct);
                FireAchievementUnlocked(achievementId, state);
            }
            catch (Exception ex)
            {
                QuickLog.Error<AchievementDirector>(
                    "Unlock failed for '{0}': {1}",
                    achievementId, ex.Message);
                FireError("Unlock", ex.Message);
                throw;
            }
        }

        public async Task ReportProgressAsync(
            string achievementId,
            double progress,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            GuardProvider("ReportProgress");

            try
            {
                var previousState = await _activeProvider.GetStateAsync(
                    achievementId, ct);

                await _activeProvider.ReportProgressAsync(
                    achievementId, progress, ct);

                var newState = await _activeProvider.GetStateAsync(
                    achievementId, ct);

                ProgressUpdated?.Invoke(
                    achievementId, newState.Progress, newState.CurrentStep);
                _onProgressUpdated?.Invoke(
                    achievementId, newState.Progress, newState.CurrentStep);

                // Check for level-up (Upgradable achievements)
                if (newState.CurrentStep > previousState.CurrentStep
                    && newState.CurrentStep < GetMaxSteps(achievementId))
                {
                    Upgraded?.Invoke(
                        achievementId,
                        newState.CurrentStep,
                        GetMaxSteps(achievementId));
                    _onUpgraded?.Invoke(
                        achievementId,
                        newState.CurrentStep,
                        GetMaxSteps(achievementId));
                }

                // Check for full unlock
                if (newState.IsUnlocked && !previousState.IsUnlocked)
                {
                    FireAchievementUnlocked(achievementId, newState);
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<AchievementDirector>(
                    "ReportProgress failed for '{0}': {1}",
                    achievementId, ex.Message);
                FireError("ReportProgress", ex.Message);
                throw;
            }
        }

        public async Task<AchievementState> GetStateAsync(
            string achievementId,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            GuardProvider("GetState");

            try
            {
                return await _activeProvider.GetStateAsync(
                    achievementId, ct);
            }
            catch (Exception ex)
            {
                QuickLog.Error<AchievementDirector>(
                    "GetState failed for '{0}': {1}",
                    achievementId, ex.Message);
                FireError("GetState", ex.Message);
                throw;
            }
        }

        public async Task<AchievementState[]> GetAllStatesAsync(
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            GuardProvider("GetAllStates");

            try
            {
                return await _activeProvider.GetAllStatesAsync(ct);
            }
            catch (Exception ex)
            {
                QuickLog.Error<AchievementDirector>(
                    "GetAllStates failed: {0}", ex.Message);
                FireError("GetAllStates", ex.Message);
                throw;
            }
        }

        #endregion

        #region Private Methods

        private async Task EnsureReadyAsync()
        {
            await ReadyTask;
        }

        private void GuardProvider(string operation)
        {
            if (_activeProvider == null)
            {
                var message = "No achievement provider available";
                FireError(operation, message);
                throw new InvalidOperationException(message);
            }
        }

        private void FireAchievementUnlocked(
            string achievementId,
            AchievementState state)
        {
            AchievementUnlocked?.Invoke(achievementId, state);
            _onAchievementUnlocked?.Invoke(
                achievementId, state.IsUnlocked, state.Progress);
        }

        private void FireError(string operation, string message)
        {
            Error?.Invoke(operation, message);
            _onError?.Invoke(operation, message);
        }

        private int GetMaxSteps(string achievementId)
        {
            if (_config == null) return 1;

            var def = _config.Achievements
                .FirstOrDefault(a => a.Id == achievementId);
            return def?.MaxSteps ?? 1;
        }

        #endregion
    }
}
