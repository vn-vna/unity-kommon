namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// A self-reporting unit of tracked work during a loading session.
    /// The task owns its progress/completion; the manager only reads these values.
    /// </summary>
    public interface ILoadingTask
    {
        /// <summary>Stable identifier used by the builder, the UI and <see cref="LoadingManager"/>.</summary>
        string TaskId { get; }

        /// <summary>Progress in 0..1, self-reported by the task.</summary>
        float Progress { get; }

        /// <summary>True when the task has completed its work.</summary>
        bool IsDone { get; }
    }

    /// <summary>
    /// Uniform PUSH channel implemented by every concrete task. Lets the manager
    /// (and external systems via <see cref="LoadingManager.UpdateTaskProgress"/> /
    /// <see cref="LoadingManager.CompleteTask"/>) drive progress regardless of the
    /// underlying task kind (coroutine, async, delegate).
    /// </summary>
    public interface IProgressReportableTask
    {
        /// <summary>Sets progress, clamped to 0..1.</summary>
        void ReportProgress(float progress);

        /// <summary>Marks the task as done.</summary>
        void Complete();
    }
}
