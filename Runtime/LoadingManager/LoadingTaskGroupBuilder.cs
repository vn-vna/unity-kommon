using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Child builder for configuring a single task group inside a
    /// <see cref="LoadingBuilder"/> (mirrors DirectCmdCommandBuilder children).
    /// All <see cref="With*"/> methods return <c>this</c>; there is no terminal
    /// method on this class — the root builder's <see cref="LoadingBuilder.Start"/>
    /// is terminal.
    /// </summary>
    public sealed class LoadingTaskGroupBuilder
    {
        #region Internal Properties
        internal string GroupId { get; }
        internal bool ModeExplicit { get; private set; }
        internal LoadingExecutionMode ExecutionMode { get; private set; } = LoadingExecutionMode.Sequential;
        internal IReadOnlyList<ILoadingTask> Tasks => _tasks;
        #endregion

        #region Constructors
        internal LoadingTaskGroupBuilder(string groupId)
        {
            GroupId = groupId;
        }
        #endregion

        #region Public Methods
        public LoadingTaskGroupBuilder WithMode(LoadingExecutionMode mode)
        {
            ExecutionMode = mode;
            ModeExplicit = true;
            return this;
        }

        public LoadingTaskGroupBuilder WithTask(ILoadingTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            AddOrOverwrite(task);
            return this;
        }

        public LoadingTaskGroupBuilder WithCoroutineTask(string taskId, IEnumerator routine)
        {
            return WithTask(new CoroutineLoadingTask(taskId, routine));
        }

        public LoadingTaskGroupBuilder WithAsyncTask(string taskId, Task operation)
        {
            return WithTask(new AsyncLoadingTask(taskId, operation));
        }

        public LoadingTaskGroupBuilder WithDelegateTask(
            string taskId,
            Func<float> progressProvider,
            Func<bool> completionPredicate
        )
        {
            return WithTask(new DelegateLoadingTask(taskId, progressProvider, completionPredicate));
        }
        #endregion

        #region Private Fields
        private readonly List<ILoadingTask> _tasks = new List<ILoadingTask>(8);
        #endregion

        #region Private Methods
        private void AddOrOverwrite(ILoadingTask task)
        {
            for (int i = 0; i < _tasks.Count; i++)
            {
                if (string.Equals(_tasks[i].TaskId, task.TaskId, StringComparison.Ordinal))
                {
                    QuickLog.Warning<LoadingTaskGroupBuilder>(
                        "Task '{0}' in group '{1}' is already registered and will be overwritten.",
                        task.TaskId, GroupId
                    );
                    _tasks[i] = task;
                    return;
                }
            }

            _tasks.Add(task);
        }
        #endregion
    }
}
