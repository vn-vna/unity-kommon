using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// A named collection of <see cref="ILoadingTask"/> with an execution mode.
    /// The manager runs groups sequentially relative to each other.
    /// </summary>
    public interface ILoadingTaskGroup
    {
        /// <summary>Stable identifier used by the builder and the UI.</summary>
        string GroupId { get; }

        /// <summary>Whether this group's tasks run sequentially or in parallel.</summary>
        LoadingExecutionMode ExecutionMode { get; }

        /// <summary>The tasks tracked by this group.</summary>
        IReadOnlyList<ILoadingTask> Tasks { get; }

        /// <summary>Aggregated progress (simple average of child tasks); 1 if empty.</summary>
        float Progress { get; }

        /// <summary>True only when the group has at least one task and all tasks are done.</summary>
        bool IsDone { get; }
    }
}
