using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    [AddComponentMenu("Scheherazade/Loading Manager")]
    public class LoadingManager : SingletonBehavior<LoadingManager>
    {
        #region Events
        public event LoadingTextUpdateHandler LoadingTextUpdate;
        public event LoadingStartedHandler LoadingStarted;
        public event LoadingFinishedHandler LoadingFinished;
        public event LoadingProgressUpdateHandler LoadingProgressUpdate;
        #endregion

        #region Interfaces
        public bool IsLoading => _loading;
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

        public delegate void LoadingTextUpdateHandler(string text);
        public delegate void LoadingStartedHandler(string[] operations);
        public delegate void LoadingProgressUpdateHandler(string operation, float progress, float totalProgress);
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

    public class LoadingOperation
    {
        public string Operation;
        public float Progress;
    }

}
