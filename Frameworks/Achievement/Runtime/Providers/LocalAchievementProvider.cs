using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public class LocalAchievementProvider : ScriptableObject, IAchievementProvider
    {
        #region Serialized Fields

        [SerializeField]
        private string _providerId = "local";

        [SerializeField]
        private string _storagePath = "AchievementData";

        #endregion

        #region Constants

        private const double FullProgress = 1.0;

        #endregion

        #region IAchievementProvider

        public string ProviderId => _providerId;
        public bool IsAvailable => true;

        public Task<bool> InitializeAsync()
        {
            EnsureDirectory();
            return Task.FromResult(true);
        }

        public Task UnlockAsync(
            string achievementId,
            CancellationToken ct = default)
        {
            var state = LoadState(achievementId);
            state = new AchievementState(
                achievementId,
                true,
                FullProgress,
                state.CurrentStep,
                DateTime.UtcNow,
                DateTime.UtcNow
            );
            SaveState(state);
            return Task.CompletedTask;
        }

        public Task IncrementProgressAsync(
            string achievementId,
            int steps,
            int maxSteps,
            CancellationToken ct = default
        )
        {
            var state = LoadState(achievementId);
            int newStep = Math.Min(state.CurrentStep + steps, maxSteps);
            double progress = maxSteps > 0 ? (double)newStep / maxSteps : FullProgress;
            bool isUnlocked = newStep >= maxSteps;

            state = new AchievementState(
                achievementId,
                isUnlocked,
                progress,
                newStep,
                isUnlocked && !state.IsUnlocked ? DateTime.UtcNow : state.UnlockedAt,
                DateTime.UtcNow
            );
            SaveState(state);
            return Task.CompletedTask;
        }

        public Task<AchievementState> GetStateAsync(
            string achievementId,
            CancellationToken ct = default
        )
        {
            return Task.FromResult(LoadState(achievementId));
        }

        public Task<AchievementState[]> GetAllStatesAsync(
            CancellationToken ct = default)
        {
            string dir = GetStorageDirectory();
            if (!Directory.Exists(dir))
            {
                return Task.FromResult(Array.Empty<AchievementState>());
            }

            var states = new List<AchievementState>();
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<AchievementSaveData>(json);
                    if (data != null)
                    {
                        states.Add(new AchievementState(
                            data.Id,
                            data.IsUnlocked,
                            data.Progress,
                            data.CurrentStep,
                            data.UnlockedAt,
                            data.LastUpdatedAt
                        ));
                    }
                }
                catch
                {
                    // Skip corrupt files
                }
            }

            return Task.FromResult(states.ToArray());
        }

        #endregion

        #region Private Methods

        private string GetStorageDirectory()
        {
            return Path.Combine(
                Application.persistentDataPath,
                _storagePath
            );
        }

        private string GetFilePath(string achievementId)
        {
            return Path.Combine(
                GetStorageDirectory(),
                $"{achievementId}.json"
            );
        }

        private void EnsureDirectory()
        {
            string dir = GetStorageDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private AchievementState LoadState(string achievementId)
        {
            string path = GetFilePath(achievementId);
            if (!File.Exists(path))
            {
                return new AchievementState(achievementId, false, 0.0, 0, null, null);
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<AchievementSaveData>(json);
                if (data != null)
                {
                    return new AchievementState(
                        data.Id,
                        data.IsUnlocked,
                        data.Progress,
                        data.CurrentStep,
                        data.UnlockedAt,
                        data.LastUpdatedAt
                    );
                }
            }
            catch
            {
                // Corrupt file, return default
            }

            return new AchievementState(achievementId, false, 0.0, 0, null, null);
        }

        private void SaveState(AchievementState state)
        {
            EnsureDirectory();
            string path = GetFilePath(state.Id);

            var data = new AchievementSaveData
            {
                Id = state.Id,
                IsUnlocked = state.IsUnlocked,
                Progress = state.Progress,
                CurrentStep = state.CurrentStep,
                UnlockedAt = state.UnlockedAt,
                LastUpdatedAt = state.LastUpdatedAt
            };

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
        }

        #endregion

        #region Nested Types

        [Serializable]
        private class AchievementSaveData
        {
            public string Id;
            public bool IsUnlocked;
            public double Progress;
            public int CurrentStep;
            public DateTime? UnlockedAt;
            public DateTime? LastUpdatedAt;
        }

        #endregion
    }
}
