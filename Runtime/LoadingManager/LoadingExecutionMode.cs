namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Controls how the tasks inside a single group are executed.
    /// Groups themselves always run sequentially relative to each other.
    /// </summary>
    public enum LoadingExecutionMode
    {
        /// <summary>Tasks run one after another; the next starts when the previous is done.</summary>
        Sequential,

        /// <summary>All tasks in the group start at once and run concurrently.</summary>
        Parallel
    }
}
