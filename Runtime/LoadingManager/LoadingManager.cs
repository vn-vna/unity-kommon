using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Manages loading operations with progress tracking and event notifications.
    /// </summary>
    /// <remarks>
    /// This singleton component handles multiple concurrent loading operations, tracks overall progress,
    /// displays loading text updates, and ensures minimum loading times are respected.
    /// It provides events for loading lifecycle and progress updates.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Start a loading operation
    /// LoadingManager.Instance.StartLoading(
    ///     new[] { "Loading assets", "Initializing game" },
    ///     minimumLoadingTime: 2.0f,
    ///     callback: () => Debug.Log("Loading complete")
    /// );
    /// 
    /// // Update progress for an operation
    /// LoadingManager.Instance.SetProgress("Loading assets", 0.5f);
    /// LoadingManager.Instance.SetProgress("Loading assets", 1.0f);
    /// </code>
    /// </example>
    [AddComponentMenu("Scheherazade/Loading Manager")]
    public class LoadingManager : SingletonBehavior<LoadingManager>
    {
        #region Events
        /// <summary>
        /// Event raised when the loading text is updated.
        /// </summary>
        public event LoadingTextUpdateHandler LoadingTextUpdate;
        
        /// <summary>
        /// Event raised when loading starts.
        /// </summary>
        public event LoadingStartedHandler LoadingStarted;
        
        /// <summary>
        /// Event raised when loading finishes.
        /// </summary>
        public event LoadingFinishedHandler LoadingFinished;
        
        /// <summary>
        /// Event raised when loading progress is updated.
        /// </summary>
        public event LoadingProgressUpdateHandler LoadingProgressUpdate;
        #endregion

        #region Interfaces
        /// <summary>
        /// Gets whether a loading operation is currently in progress.
        /// </summary>
        public bool IsLoading => _loading;
        
        /// <summary>
        /// Gets the current overall loading progress (0.0 to 1.0).
        /// </summary>
        public float Progress => _progress;
        #endregion

        #region Serialized Fields
        [SerializeField]
        private float minimumGameLoadingTime;

        [SerializeField]
        private float minimumInGameLoadingTime;

        [SerializeField]
        private string[] loadingTexts =
        {
            "Loading",
            "Loading.",
            "Loading..",
            "Loading...",
        };

        [SerializeField]
        private bool showLog;

        #endregion

        #region Private Fields
        private List<LoadingOperation> _operations;

        private int _textIndex;
        private float _loadingTextInterval = 0.3f;
        private float _textTimer;
        private float _loadingTimer;
        private float _minimumLoadingTime = 1f;
        private bool _loading;
        private Action _loadingCallback;
        private float _progress;
        #endregion

        #region Unity Events
        protected override void Awake()
        {
            base.Awake();
            _operations = new List<LoadingOperation>();
        }

        private void Update()
        {
            ManualUpdate();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Manually updates the loading manager state.
        /// </summary>
        /// <remarks>
        /// This is called automatically in Update, but can be called manually if needed.
        /// </remarks>
        public void ManualUpdate()
        {
            if (!_loading) return;

            if (showLog)
            {
                if (_operations != null && _operations.Count > 0)
                {
                    string ops = _operations
                        .Select(x => $"{x.Operation}: {x.Progress * 100}%")
                        .Aggregate((x, y) => $"{x}, {y}");
                    Debug.Log($"Loading: {ops}");
                }
            }

            _progress = UpdateProgress();

            HandleProgressTimer();
        }

        /// <summary>
        /// Starts a loading operation with the specified operations.
        /// </summary>
        /// <param name="operations">Array of operation names to track.</param>
        /// <param name="minimumLoadingTime">Minimum time in seconds the loading should take.</param>
        /// <param name="callback">Callback to invoke when loading completes.</param>
        /// <param name="coroutine">Optional coroutine to run during loading.</param>
        public void StartLoading(
            string[] operations,
            float minimumLoadingTime = 0,
            Action callback = null,
            IEnumerator coroutine = null
        )
        {
            _operations.Clear();
            _loadingTimer = 0;
            _loading = true;
            _minimumLoadingTime = minimumLoadingTime;
            _loadingCallback = callback;

            for (int i = 0; i < operations.Length; i++)
            {
                _operations.Add(new LoadingOperation
                {
                    Operation = operations[i],
                    Progress = 0
                });
            }

            _progress = 0;

            gameObject.SetActive(true);
            StartCoroutine(coroutine);

            LoadingStarted?.Invoke(operations);
        }

        /// <summary>
        /// Sets the progress for a specific loading operation.
        /// </summary>
        /// <param name="operation">The name of the operation.</param>
        /// <param name="progress">The progress value (0.0 to 1.0).</param>
        public void SetProgress(string operation, float progress)
        {
            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Operation == operation)
                {
                    _operations[i].Progress = progress;
                    break;
                }
            }

            LoadingProgressUpdate?.Invoke(operation, progress, Progress);
        }

        /// <summary>
        /// Delegate for loading text update events.
        /// </summary>
        /// <param name="text">The updated loading text.</param>
        public delegate void LoadingTextUpdateHandler(string text);
        
        /// <summary>
        /// Delegate for loading started events.
        /// </summary>
        /// <param name="operations">Array of operation names being loaded.</param>
        public delegate void LoadingStartedHandler(string[] operations);
        
        /// <summary>
        /// Delegate for loading progress update events.
        /// </summary>
        /// <param name="operation">The operation being updated (can be null for overall progress).</param>
        /// <param name="progress">The progress of the specific operation.</param>
        /// <param name="totalProgress">The overall loading progress.</param>
        public delegate void LoadingProgressUpdateHandler(string operation, float progress, float totalProgress);
        
        /// <summary>
        /// Delegate for loading finished events.
        /// </summary>
        public delegate void LoadingFinishedHandler();
        #endregion

        #region Private Methods

        private float UpdateProgress()
        {
            _textTimer += Time.deltaTime;
            if (_textTimer >= _loadingTextInterval)
            {
                _textIndex++;
                if (_textIndex >= loadingTexts.Length)
                {
                    _textIndex = 0;
                }

                LoadingTextUpdate?.Invoke(loadingTexts[_textIndex]);
                _textTimer = 0;
            }

            var timeProgress = Mathf.Clamp01(_loadingTimer / _minimumLoadingTime);
            var loadingProgress = (_operations.Sum(x => x.Progress) + timeProgress) / (_operations.Count + 1);
            var progress = Mathf.Clamp(_progress + 0.05f, 0, loadingProgress);
            LoadingProgressUpdate?.Invoke(null, 0, progress);
            return progress;
        }

        private void HandleProgressTimer()
        {
            if (_progress >= 1)
            {
                HandleProgressCompleted();
            }
            else
            {
                _loadingTimer += Time.deltaTime;
            }
        }

        private void HandleProgressCompleted()
        {
            _loadingTimer += Time.deltaTime;
            if (_loadingTimer < _minimumLoadingTime) return;

            gameObject.SetActive(false);
            _loading = false;
            _loadingCallback?.Invoke();
            _loadingCallback = null;
            _operations.Clear();
            LoadingFinished?.Invoke();
        }
        #endregion

    }

    /// <summary>
    /// Represents a single loading operation with its name and progress.
    /// </summary>
    public class LoadingOperation
    {
        /// <summary>
        /// Gets or sets the name of the operation.
        /// </summary>
        public string Operation;
        
        /// <summary>
        /// Gets or sets the progress of the operation (0.0 to 1.0).
        /// </summary>
        public float Progress;
    }

}
