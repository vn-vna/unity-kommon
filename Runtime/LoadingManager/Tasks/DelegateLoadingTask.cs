using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Polling delegate-based task for cheap/uncoupled work. Progress is normally
    /// pulled from the provider each frame; because it implements
    /// <see cref="IProgressReportableTask"/>, external systems can also push progress.
    /// A push (<see cref="ReportProgress"/> / <see cref="Complete"/>) sets a pushed
    /// override that wins over the provider until cleared.
    /// </summary>
    public sealed class DelegateLoadingTask :
        ILoadingTask,
        IProgressReportableTask
    {
        #region Properties
        public string TaskId { get; }
        public float Progress => _hasPushedProgress ? _pushedProgress : Mathf.Clamp01(_progressProvider());
        public bool IsDone => _hasPushedDone || _completionPredicate();
        #endregion

        #region Constructors
        public DelegateLoadingTask(
            string taskId,
            Func<float> progressProvider,
            Func<bool> completionPredicate
        )
        {
            if (progressProvider == null)
            {
                throw new ArgumentNullException(nameof(progressProvider));
            }

            if (completionPredicate == null)
            {
                throw new ArgumentNullException(nameof(completionPredicate));
            }

            TaskId = taskId;
            _progressProvider = progressProvider;
            _completionPredicate = completionPredicate;
        }
        #endregion

        #region IProgressReportableTask
        /// <summary>Main-thread only. Push overrides the polled value.</summary>
        public void ReportProgress(float progress)
        {
            _pushedProgress = Mathf.Clamp01(progress);
            _hasPushedProgress = true;
        }

        public void Complete()
        {
            _pushedProgress = 1f;
            _hasPushedProgress = true;
            _hasPushedDone = true;
        }
        #endregion

        #region Private Fields
        private readonly Func<float> _progressProvider;
        private readonly Func<bool> _completionPredicate;
        private float _pushedProgress;
        private bool _hasPushedProgress;
        private bool _hasPushedDone;
        #endregion
    }
}
