using System.Collections;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Wraps an <see cref="IEnumerator"/> loading routine. The manager drives it through
    /// its own wrapper runner (so it can be stopped cleanly). The routine yields a
    /// <see cref="float"/> to report progress (e.g. <c>yield return 0.42f;</c>),
    /// <c>null</c> to continue, and completes when enumeration ends.
    /// Progress may also be pushed externally (main thread only).
    /// </summary>
    public sealed class CoroutineLoadingTask :
        ILoadingTask,
        IProgressReportableTask
    {
        #region Properties
        public string TaskId { get; }
        public float Progress { get; private set; }
        public bool IsDone { get; private set; }
        public IEnumerator Routine { get; }
        #endregion

        #region Constructors
        public CoroutineLoadingTask(string taskId, IEnumerator routine)
        {
            TaskId = taskId;
            Routine = routine;
        }
        #endregion

        #region IProgressReportableTask
        /// <summary>Main-thread only. Called by the manager's runner (float yields)
        /// and by <see cref="LoadingManager.UpdateTaskProgress"/> / <see cref="LoadingManager.CompleteTask"/>.</summary>
        public void ReportProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
        }

        public void Complete()
        {
            IsDone = true;
        }
        #endregion
    }
}
