using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Immutable runtime registration for a group of loading tasks.
    /// Created from the ephemeral <see cref="LoadingTaskGroupBuilder"/> by the manager
    /// (mirrors DirectCmdForwarding's builder → registration conversion).
    /// </summary>
    [Serializable]
    public sealed class LoadingTaskGroup : ILoadingTaskGroup
    {
        #region Properties
        public string GroupId { get; }
        public LoadingExecutionMode ExecutionMode { get; }
        public IReadOnlyList<ILoadingTask> Tasks => _tasks;
        public float Progress => _tasks.Count == 0 ? 1f : SumProgress() / _tasks.Count;
        public bool IsDone => _tasks.Count > 0 && AreAllTasksDone();
        #endregion

        #region Constructors
        public LoadingTaskGroup(
            string groupId,
            LoadingExecutionMode executionMode = LoadingExecutionMode.Sequential
        )
        {
            GroupId = groupId;
            ExecutionMode = executionMode;
        }
        #endregion

        #region Public Methods
        public void AddTask(ILoadingTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            _tasks.Add(task);
        }
        #endregion

        #region Private Fields
        private readonly List<ILoadingTask> _tasks = new List<ILoadingTask>(8);
        #endregion

        #region Private Methods
        private float SumProgress()
        {
            float sum = 0f;
            for (int i = 0; i < _tasks.Count; i++)
            {
                sum += Mathf.Clamp01(_tasks[i].Progress);
            }

            return sum;
        }

        private bool AreAllTasksDone()
        {
            for (int i = 0; i < _tasks.Count; i++)
            {
                if (!_tasks[i].IsDone)
                {
                    return false;
                }
            }

            return true;
        }
        #endregion
    }
}
