using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Wraps a <see cref="Task"/> (or <see cref="Task"/>-compatible operation).
    /// <see cref="IsDone"/> is driven by the wrapped operation's completion;
    /// progress is fed via an <c>IProgress&lt;float&gt;</c> reporter the operation uses.
    /// Progress writes are thread-safe (<see cref="Interlocked.Exchange"/>).
    /// </summary>
    public sealed class AsyncLoadingTask :
        ILoadingTask,
        IProgressReportableTask
    {
        #region Properties
        public string TaskId { get; }
        public Task Operation { get; }
        public float Progress => _progress;
        public bool IsDone => Operation != null && Operation.IsCompleted;
        #endregion

        #region Constructors
        public AsyncLoadingTask(string taskId, Task operation)
        {
            TaskId = taskId;
            Operation = operation;
        }
        #endregion

        #region IProgressReportableTask
        /// <summary>Thread-safe; may be called from the thread-pool (via the wrapped
        /// operation) or the main thread.</summary>
        public void ReportProgress(float progress)
        {
            Interlocked.Exchange(ref _progress, Mathf.Clamp01(progress));
        }

        /// <summary>No-op by design — <see cref="IsDone"/> derives from <see cref="Operation"/>.</summary>
        public void Complete() { }
        #endregion

        #region Private Fields
        private float _progress;
        #endregion
    }
}
