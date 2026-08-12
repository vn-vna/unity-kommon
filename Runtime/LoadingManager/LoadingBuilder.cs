using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Root fluent builder for composing a loading session (mirrors
    /// <c>DirectCmdCommandBuilder</c>). Created via
    /// <see cref="LoadingManager.CreateLoading(string)"/>; safe to call before the
    /// manager instance exists (deferred registration). <see cref="Start"/> is terminal.
    /// </summary>
    public sealed class LoadingBuilder
    {
        #region Internal Properties
        internal string ContextId { get; }
        internal IReadOnlyList<LoadingTaskGroupBuilder> GroupBuilders => _groupBuilders;
        internal LoadingSessionListeners Listeners => _listeners;
        #endregion

        #region Constructors
        internal LoadingBuilder(string contextId)
        {
            ContextId = contextId;
        }
        #endregion

        #region Public Methods
        /// <summary>Opens a child group builder and invokes <paramref name="configure"/> on it.
        /// Mirrors <c>DirectCmdCommandBuilder.WithSubcommand</c>.</summary>
        public LoadingBuilder WithGroup(string groupId, Action<LoadingTaskGroupBuilder> configure)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("Group id cannot be empty or whitespace.", nameof(groupId));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            LoadingTaskGroupBuilder groupBuilder = new LoadingTaskGroupBuilder(groupId);
            configure(groupBuilder);
            _groupBuilders.Add(groupBuilder);
            return this;
        }

        /// <summary>Per-loading start listener; invoked with the composed group list.</summary>
        public LoadingBuilder WithStartListener(Action<IReadOnlyList<ILoadingTaskGroup>> listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            _listeners.OnStart += listener;
            return this;
        }

        /// <summary>Per-loading progress listener; invoked with
        /// <c>(changedGroupOrTaskId, progress, totalProgress)</c>.</summary>
        public LoadingBuilder WithProgressListener(Action<string, float, float> listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            _listeners.OnProgress += listener;
            return this;
        }

        /// <summary>Per-loading completion listener.</summary>
        public LoadingBuilder WithCompleteListener(Action listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            _listeners.OnComplete += listener;
            return this;
        }

        /// <summary>TERMINAL: converts the composed groups to immutable registrations and
        /// starts the loading. If the manager instance is not yet alive, the terminal
        /// parameters are stored on the pending entry and applied when flushed.</summary>
        public void Start(float minimumLoadingTime = -1f, Action onFinished = null)
        {
            LoadingManager instance = LoadingManager.Instance;
            if (instance != null)
            {
                LoadingManager.ConsumePending(this);
                instance.StartLoading(
                    ToImmutableGroups(),
                    minimumLoadingTime,
                    onFinished,
                    _listeners
                );
                return;
            }

            QuickLog.Warning<LoadingBuilder>(
                "LoadingManager instance is not available yet. Loading will start after Awake flush."
            );
            LoadingManager.RegisterDeferred(this, minimumLoadingTime, onFinished, _listeners);
        }
        #endregion

        #region Internal Methods
        internal ILoadingTaskGroup[] ToImmutableGroups()
        {
            LoadingExecutionMode defaultMode = LoadingManager.Instance?.Configuration?.DefaultExecutionMode
                ?? LoadingExecutionMode.Sequential;

            List<ILoadingTaskGroup> groups = new List<ILoadingTaskGroup>(_groupBuilders.Count);
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _groupBuilders.Count; i++)
            {
                LoadingTaskGroupBuilder builder = _groupBuilders[i];
                LoadingExecutionMode mode = builder.ModeExplicit
                    ? builder.ExecutionMode
                    : defaultMode;

                if (!seenIds.Add(builder.GroupId))
                {
                    QuickLog.Warning<LoadingBuilder>(
                        "Group '{0}' is already registered and will be overwritten.",
                        builder.GroupId
                    );
                }

                LoadingTaskGroup group = new LoadingTaskGroup(builder.GroupId, mode);
                IReadOnlyList<ILoadingTask> tasks = builder.Tasks;
                for (int j = 0; j < tasks.Count; j++)
                {
                    group.AddTask(tasks[j]);
                }

                groups.Add(group);
            }

            return groups.ToArray();
        }
        #endregion

        #region Private Fields
        private readonly List<LoadingTaskGroupBuilder> _groupBuilders = new List<LoadingTaskGroupBuilder>(4);
        private readonly LoadingSessionListeners _listeners = new LoadingSessionListeners();
        #endregion
    }
}
