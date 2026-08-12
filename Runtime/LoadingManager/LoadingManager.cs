using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Task-group based loading manager. Composes loading sessions from named groups
    /// of self-reporting tasks via the fluent <see cref="LoadingBuilder"/> API
    /// (modeled on DirectCmdForwarding's builder pattern). Groups run sequentially
    /// by default; tasks within a group run sequentially or in parallel.
    /// </summary>
    [AddComponentMenu("Scheherazade/Loading Manager")]
    [DontDestroyOnLoad]
    public class LoadingManager : SingletonBehavior<LoadingManager>
    {
        #region Constants
        private const string ConfigPath = "Integration/Managers/LoadingConfiguration";
        private const string ManagerGameObjectName = "[Scheherazade Loading Manager]";
        #endregion

        #region Events & Delegates
        public event Action<string> LoadingTextUpdate;
        public event Action<IReadOnlyList<ILoadingTaskGroup>> LoadingStarted;
        public event Action<string, float, float> LoadingProgressUpdate;
        public event Action LoadingFinished;
        #endregion

        #region Interfaces & Properties
        public bool IsLoading => _loading;
        public float Progress => _progress;
        public LoadingConfiguration Configuration => _configuration;
        public IReadOnlyList<ILoadingTaskGroup> ActiveGroups => _groups;
        #endregion

        #region Serialized Fields
        [SerializeField]
        [Tooltip("Optional scene override. Falls back to the Resources asset when null.")]
        private LoadingConfiguration configuration;
        #endregion

        #region Private Fields
        private static readonly List<PendingLoading> _pendingLoadings = new List<PendingLoading>(4);

        private LoadingConfiguration _configuration;
        private readonly List<ILoadingTaskGroup> _groups = new List<ILoadingTaskGroup>(8);
        private int _activeGroupIndex;
        private int _activeTaskIndex;
        private readonly List<CoroutineTaskRunner> _runningCoroutineTasks = new List<CoroutineTaskRunner>(8);
        private int _textIndex;
        private float _textTimer;
        private float _loadingTimer;
        private float _minimumLoadingTime;
        private bool _loading;
        private Action _loadingCallback;
        private LoadingSessionListeners _sessionListeners;
        private float _progress;
        #endregion

        #region Unity Callbacks
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                return;
            }

            try
            {
                _configuration = configuration != null
                    ? configuration
                    : Resources.Load<LoadingConfiguration>(ConfigPath);
            }
            catch (Exception e)
            {
                QuickLog.Error<LoadingManager>(
                    "Failed to load LoadingConfiguration from '{0}'. {1}",
                    ConfigPath, e.Message
                );
            }

            if (_configuration == null)
            {
                QuickLog.Warning<LoadingManager>(
                    "LoadingConfiguration not found at Resources path '{0}'. Using in-memory defaults.",
                    ConfigPath
                );
                _configuration = ScriptableObject.CreateInstance<LoadingConfiguration>();
            }

            FlushPendingLoadings();
        }

        private void Update()
        {
            if (!_loading)
            {
                return;
            }

            ManualUpdate();
        }
        #endregion

        #region Bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject go = new GameObject(ManagerGameObjectName);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<LoadingManager>();
        }
        #endregion

        #region Public Methods

        /// <summary>Static fluent entry (mirrors DirectCmdForwarding.RegisterCommand).
        /// Safe to call before the instance exists — the builder is deferred until Awake flush.</summary>
        public static LoadingBuilder CreateLoading(string contextId = null)
        {
            if (contextId != null && string.IsNullOrWhiteSpace(contextId))
            {
                throw new ArgumentException("contextId cannot be whitespace.", nameof(contextId));
            }

            EnsureInstance();

            LoadingBuilder builder = new LoadingBuilder(contextId);
            _pendingLoadings.Add(new PendingLoading(builder));
            return builder;
        }

        public void StartLoading(
            IEnumerable<ILoadingTaskGroup> groups,
            float minimumLoadingTime = -1f,
            Action callback = null,
            LoadingSessionListeners listeners = null
        )
        {
            if (_loading)
            {
                QuickLog.Warning<LoadingManager>(
                    "StartLoading ignored: a loading session is already in progress."
                );
                return;
            }

            ResetLoadingState();

            if (groups != null)
            {
                _groups.AddRange(groups);
            }

            _minimumLoadingTime = minimumLoadingTime >= 0f
                ? minimumLoadingTime
                : _configuration.MinimumLoadingTime;

            _loadingCallback = callback;
            _sessionListeners = listeners;

            if (_groups.Count > 0)
            {
                _activeGroupIndex = 0;
                _activeTaskIndex = 0;
                StartGroupCoroutines(_groups[0], 0);
            }

            _loading = true;

            QuickLog.Info<LoadingManager>(
                "Loading started with {0} group(s), minimum time {1:0.00}s.",
                _groups.Count, _minimumLoadingTime
            );

            LoadingStarted?.Invoke(_groups);
            _sessionListeners?.OnStart?.Invoke(_groups);
        }

        public void StartLoading(
            ILoadingTaskGroup group,
            float minimumLoadingTime = -1f,
            Action callback = null,
            LoadingSessionListeners listeners = null
        )
        {
            StartLoading(
                group != null ? new[] { group } : null,
                minimumLoadingTime,
                callback,
                listeners
            );
        }

        /// <summary>Uniform progress push — forwards to the matching task's
        /// <see cref="IProgressReportableTask"/> channel. Main-thread API.</summary>
        public bool UpdateTaskProgress(string taskId, float progress)
        {
            IProgressReportableTask reportable = FindTask(taskId);
            if (reportable == null)
            {
                QuickLog.Warning<LoadingManager>(
                    "UpdateTaskProgress('{0}') ignored: task not found in active groups.",
                    taskId
                );
                return false;
            }

            reportable.ReportProgress(progress);
            return true;
        }

        /// <summary>Marks a task done. Main-thread API.</summary>
        public bool CompleteTask(string taskId)
        {
            IProgressReportableTask reportable = FindTask(taskId);
            if (reportable == null)
            {
                QuickLog.Warning<LoadingManager>(
                    "CompleteTask('{0}') ignored: task not found in active groups.",
                    taskId
                );
                return false;
            }

            reportable.Complete();
            return true;
        }

        public void StopLoading()
        {
            if (!_loading)
            {
                return;
            }

            FinishLoading();
        }

        public void ManualUpdate()
        {
            if (!_loading)
            {
                return;
            }

            _loadingTimer += Time.deltaTime;
            HandleProgressTimer();
            DriveActiveGroups();
            UpdateProgress();
        }

        #endregion

        #region Builder Plumbing (mirrors DirectCmdForwarding)

        internal static void RegisterDeferred(
            LoadingBuilder builder,
            float minimumLoadingTime,
            Action onFinished,
            LoadingSessionListeners listeners
        )
        {
            for (int i = 0; i < _pendingLoadings.Count; i++)
            {
                if (_pendingLoadings[i].Builder == builder)
                {
                    PendingLoading pending = _pendingLoadings[i];
                    pending.Started = true;
                    pending.StoredMinimumTime = minimumLoadingTime;
                    pending.StoredOnFinished = onFinished;
                    pending.Listeners = listeners;
                    return;
                }
            }

            _pendingLoadings.Add(new PendingLoading(builder)
            {
                Started = true,
                StoredMinimumTime = minimumLoadingTime,
                StoredOnFinished = onFinished,
                Listeners = listeners
            });
        }

        internal static void ConsumePending(LoadingBuilder builder)
        {
            for (int i = 0; i < _pendingLoadings.Count; i++)
            {
                if (_pendingLoadings[i].Builder == builder)
                {
                    _pendingLoadings.RemoveAt(i);
                    return;
                }
            }
        }

        private void FlushPendingLoadings()
        {
            if (_pendingLoadings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _pendingLoadings.Count; i++)
            {
                PendingLoading pending = _pendingLoadings[i];
                if (pending.Started)
                {
                    Register(pending);
                }
            }

            _pendingLoadings.Clear();
        }

        private static void Register(PendingLoading pending)
        {
            LoadingManager instance = Instance;
            if (instance == null)
            {
                QuickLog.Error<LoadingManager>(
                    "LoadingManager instance is not available. Cannot register pending loading '{0}'.",
                    pending.Builder.ContextId
                );
                return;
            }

            ILoadingTaskGroup[] groups = ConvertToGroups(pending.Builder);
            instance.StartLoading(
                groups,
                pending.StoredMinimumTime ?? -1f,
                pending.StoredOnFinished,
                pending.Listeners
            );
        }

        private static ILoadingTaskGroup[] ConvertToGroups(LoadingBuilder builder)
        {
            return builder.ToImmutableGroups();
        }

        private static void EnsureInstance()
        {
            if (Instance != null)
            {
                return;
            }

            LoadingManager existing = FindFirstObjectByType<LoadingManager>();
            if (existing != null)
            {
                return;
            }

            GameObject go = new GameObject(ManagerGameObjectName);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<LoadingManager>();
        }

        #endregion

        #region Private Methods

        private void DriveActiveGroups()
        {
            if (_groups.Count == 0)
            {
                return;
            }

            while (_activeGroupIndex < _groups.Count)
            {
                ILoadingTaskGroup group = _groups[_activeGroupIndex];

                // Parallel: run all tasks; the group is done when all are done.
                if (group.ExecutionMode == LoadingExecutionMode.Parallel)
                {
                    if (!group.IsDone)
                    {
                        return;
                    }

                    _activeGroupIndex++;
                    continue;
                }

                // Sequential: empty groups are instantly done — no infinite loop.
                if (group.Tasks.Count == 0)
                {
                    _activeGroupIndex++;
                    continue;
                }

                // All tasks in the current group done → advance to the next group.
                if (_activeTaskIndex >= group.Tasks.Count)
                {
                    _activeGroupIndex++;
                    _activeTaskIndex = 0;

                    if (_activeGroupIndex < _groups.Count)
                    {
                        StartGroupCoroutines(_groups[_activeGroupIndex], 0);
                    }
                    continue;
                }

                // Current task done → start the next task in this group.
                if (group.Tasks[_activeTaskIndex].IsDone)
                {
                    _activeTaskIndex++;
                    if (_activeTaskIndex < group.Tasks.Count)
                    {
                        StartGroupCoroutines(group, _activeTaskIndex);
                    }
                    continue;
                }

                // Current task still running.
                return;
            }
        }

        private void StartGroupCoroutines(ILoadingTaskGroup group, int startIndex)
        {
            bool isParallel = group.ExecutionMode == LoadingExecutionMode.Parallel;

            for (int i = startIndex; i < group.Tasks.Count; i++)
            {
                if (group.Tasks[i] is CoroutineLoadingTask coroutineTask)
                {
                    CoroutineTaskRunner runner = new CoroutineTaskRunner(this, coroutineTask);
                    _runningCoroutineTasks.Add(runner);
                    runner.Start();

                    if (!isParallel)
                    {
                        return; // sequential group: only the requested task runs until done
                    }
                }
            }
        }

        private void UpdateProgress()
        {
            _textTimer += Time.deltaTime;
            float interval = _configuration != null ? _configuration.LoadingTextInterval : 0.3f;
            if (_textTimer >= interval)
            {
                _textTimer = 0f;

                IReadOnlyList<string> texts = _configuration != null ? _configuration.LoadingTexts : null;
                if (texts != null && texts.Count > 0)
                {
                    _textIndex = (_textIndex + 1) % texts.Count;
                    LoadingTextUpdate?.Invoke(texts[_textIndex]);
                }
            }

            float timeProgress = _minimumLoadingTime > 0f
                ? Mathf.Clamp01(_loadingTimer / _minimumLoadingTime)
                : 1f;

            float groupSum = 0f;
            for (int i = 0; i < _groups.Count; i++)
            {
                groupSum += _groups[i].Progress;
            }

            float targetProgress = (timeProgress + groupSum) / (_groups.Count + 1);

            float smoothing = _configuration != null ? _configuration.ProgressSmoothening : 0.05f;
            float smoothed = Mathf.MoveTowards(_progress, targetProgress, smoothing);
            _progress = Mathf.Max(_progress, smoothed); // monotonic non-decreasing

            LoadingProgressUpdate?.Invoke(null, 0f, _progress);
            _sessionListeners?.OnProgress?.Invoke(null, 0f, _progress);
        }

        private void HandleProgressTimer()
        {
            bool allGroupsDone = _activeGroupIndex >= _groups.Count;
            bool minTimeElapsed = _minimumLoadingTime <= 0f || _loadingTimer >= _minimumLoadingTime;
            if (allGroupsDone && minTimeElapsed)
            {
                FinishLoading();
            }
        }

        private void FinishLoading()
        {
            for (int i = 0; i < _runningCoroutineTasks.Count; i++)
            {
                _runningCoroutineTasks[i].Stop();
            }
            _runningCoroutineTasks.Clear();
            _groups.Clear();
            _loading = false;

            // The smoothing may not have caught up yet; a loading screen must end at 100%.
            // This keeps progress monotonic non-decreasing (matches the plan's smoothing rule).
            _progress = 1f;

            Action callback = _loadingCallback;
            LoadingSessionListeners listeners = _sessionListeners;
            _loadingCallback = null;
            _sessionListeners = null;

            QuickLog.Info<LoadingManager>("Loading finished.");

            listeners?.OnComplete?.Invoke();
            callback?.Invoke();
            LoadingFinished?.Invoke();
        }

        private void ResetLoadingState()
        {
            _groups.Clear();
            _activeGroupIndex = 0;
            _activeTaskIndex = 0;
            _loadingTimer = 0f;
            _progress = 0f;
            _textIndex = 0;
            _textTimer = 0f;
            _loadingCallback = null;
            _sessionListeners = null;
        }

        /// <summary>Finds a task by TaskId across all active groups; returns its reportable channel.</summary>
        private IProgressReportableTask FindTask(string taskId)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                IReadOnlyList<ILoadingTask> tasks = _groups[i].Tasks;
                for (int j = 0; j < tasks.Count; j++)
                {
                    if (tasks[j] is IProgressReportableTask reportable
                        && string.Equals(tasks[j].TaskId, taskId, StringComparison.Ordinal))
                    {
                        return reportable;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Nested Types

        /// <summary>Deferred-registered builder + stored terminal params, mirrors
        /// DirectCmdForwarding._pendingRoots. Flushed on Awake.</summary>
        private sealed class PendingLoading
        {
            public LoadingBuilder Builder { get; }
            public bool Started { get; set; }
            public float? StoredMinimumTime { get; set; }
            public Action StoredOnFinished { get; set; }
            public LoadingSessionListeners Listeners { get; set; }

            public PendingLoading(LoadingBuilder builder)
            {
                Builder = builder;
            }
        }

        /// <summary>Wraps each CoroutineLoadingTask so the manager can stop it cleanly.
        /// Interprets float yields as progress reports, null as continue; calls Complete() on end.</summary>
        private sealed class CoroutineTaskRunner
        {
            private readonly LoadingManager _manager;
            private readonly CoroutineLoadingTask _task;
            private Coroutine _handle;

            public CoroutineTaskRunner(LoadingManager manager, CoroutineLoadingTask task)
            {
                _manager = manager;
                _task = task;
            }

            public void Start()
            {
                _handle = _manager.StartCoroutine(Run());
            }

            public void Stop()
            {
                if (_handle != null)
                {
                    _manager.StopCoroutine(_handle);
                    _handle = null;
                }
            }

            private IEnumerator Run()
            {
                IEnumerator routine = _task.Routine;
                while (routine != null && routine.MoveNext())
                {
                    if (routine.Current is float progress)
                    {
                        _task.ReportProgress(progress);
                    }

                    yield return null;
                }

                _task.Complete();
            }
        }

        #endregion
    }
}
