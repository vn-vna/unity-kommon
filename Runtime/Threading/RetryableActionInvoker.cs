using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    [Serializable]
    public class RetryStrategyConfig
    {
        [Tooltip("The retry strategy to use when an action fails.")]
        public RetryStrategy strategy;

        [Tooltip("Base interval between retries in seconds.")]
        public float baseInterval = 5f;

        [Tooltip("Maximum interval cap for exponential strategies (0 = no cap).")]
        public float maxInterval;

        [Tooltip("Jitter factor (0-1). Adds random variation to the delay.")]
        [Range(0f, 1f)]
        public float jitterFactor;

        [Tooltip("Time to wait for an action to complete before assuming failure (0 = no timeout).")]
        public float timeout = 30f;
    }

    public class RetryHandle
    {
        internal string Id { get; }
        internal bool IsDisposed => _isDisposed;

        public bool IsRunning => _isLoading;
        public int AttemptCount => _attemptCount;
        public int Generation => _generation;
        public RetryStrategyConfig Config => _config;

        private readonly Action<RetryHandle> _action;
        private readonly RetryStrategyConfig _config;
        private int _attemptCount;
        private bool _isLoading;
        private float _startTime;
        private int _generation;
        private int _scheduleId;
        private bool _isDisposed;

        internal RetryHandle(Action<RetryHandle> action, RetryStrategyConfig config)
        {
            Id = Guid.NewGuid().ToString();
            _action = action;
            _config = config;
        }

        public void Execute()
        {
            if (_isDisposed) return;
            _scheduleId++;
            _attemptCount = 0;
            InvokeAction();
        }

        public void Complete(int generation)
        {
            if (_isDisposed) return;
            if (generation != _generation) return;

            _scheduleId++;
            _isLoading = false;
            _attemptCount = 0;
        }

        public void Fail(int generation)
        {
            if (_isDisposed) return;
            if (generation != _generation) return;
            if (!_isLoading) return;

            _scheduleId++;
            _isLoading = false;
            ProcessFailure();
        }

        public void Cancel()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isLoading = false;
            _attemptCount = 0;
        }

        internal void CheckTimeout()
        {
            if (_isDisposed || !_isLoading || _config.timeout <= 0f) return;
            if (Time.unscaledTime - _startTime < _config.timeout) return;

            _scheduleId++;
            _isLoading = false;
            ProcessFailure();
        }

        private void InvokeAction()
        {
            _generation++;
            _isLoading = true;
            _startTime = Time.unscaledTime;
            _action?.Invoke(this);
        }

        private void ProcessFailure()
        {
            if (_config.strategy == RetryStrategy.Cancel) return;

            _attemptCount++;
            float delay = CalculateDelay();

            if (delay <= 0f)
            {
                InvokeAction();
            }
            else
            {
                ScheduleDelayedRetry(delay);
            }
        }

        private void ScheduleDelayedRetry(float delay)
        {
            int capturedId = _scheduleId;
            Dispatcher.DispatchCoroutine(DelayedRetryCoroutine(delay, capturedId));
        }

        private IEnumerator DelayedRetryCoroutine(float delay, int capturedScheduleId)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (_isDisposed || capturedScheduleId != _scheduleId) yield break;
            InvokeAction();
        }

        private float CalculateDelay()
        {
            switch (_config.strategy)
            {
                case RetryStrategy.Immediate:
                    return 0f;

                case RetryStrategy.FixedInterval:
                    return _config.baseInterval;

                case RetryStrategy.ExponentialInterval:
                {
                    float delay = _config.baseInterval * Mathf.Pow(2f, _attemptCount - 1);
                    if (_config.maxInterval > 0f && delay > _config.maxInterval)
                        delay = _config.maxInterval;
                    return delay;
                }

                case RetryStrategy.ExponentialIntervalWithJitter:
                {
                    float expo = _config.baseInterval * Mathf.Pow(2f, _attemptCount - 1);
                    if (_config.maxInterval > 0f && expo > _config.maxInterval)
                        expo = _config.maxInterval;
                    float jitter = UnityEngine.Random.Range(-_config.jitterFactor, _config.jitterFactor) * expo;
                    return Mathf.Max(0f, expo + jitter);
                }

                default:
                    return _config.baseInterval;
            }
        }
    }

    [AddComponentMenu("Scheherazade/Common/Threading/RetryableActionInvoker")]
    public class RetryableActionInvoker : SingletonBehavior<RetryableActionInvoker>
    {
        private readonly Dictionary<string, RetryHandle> _handles = new Dictionary<string, RetryHandle>();

        public static RetryHandle Schedule(Action<RetryHandle> action, RetryStrategyConfig config)
        {
            if (Instance == null) return null;

            var handle = new RetryHandle(action, config);
            Instance._handles[handle.Id] = handle;
            return handle;
        }

        private void Update()
        {
            if (_handles.Count == 0) return;

            var disposedIds = new List<string>();
            foreach (var kvp in _handles)
            {
                if (kvp.Value.IsDisposed)
                {
                    disposedIds.Add(kvp.Key);
                    continue;
                }

                kvp.Value.CheckTimeout();
            }

            foreach (var id in disposedIds)
            {
                _handles.Remove(id);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRetryableActionInvoker()
        {
            var go = new GameObject("[Scheherazade RetryableActionInvoker]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<RetryableActionInvoker>();
        }
    }
}
