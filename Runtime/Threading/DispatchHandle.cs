using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    /// <summary>
    /// Represents the lifecycle of work queued for the Unity main thread.
    /// </summary>
    public enum DispatchStatus
    {
        Queued,
        Running,
        Succeeded,
        Faulted
    }

    /// <summary>
    /// Tracks a dispatched action. <see cref="Task"/> may be awaited more than once;
    /// Unity <see cref="Awaitable"/> follows Unity's normal single-await semantics.
    /// </summary>
    public class DispatchHandle
    {
        private readonly object _stateLock = new object();
        private readonly TaskCompletionSource<bool> _taskCompletion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AwaitableCompletionSource _awaitableCompletion = new AwaitableCompletionSource();

        private DispatchStatus _status = DispatchStatus.Queued;
        private float _progress;
        private Exception _error;

        internal event Action<DispatchHandle> Completed;

        /// <summary>The current execution state.</summary>
        public DispatchStatus Status
        {
            get
            {
                lock (_stateLock)
                {
                    return _status;
                }
            }
        }

        /// <summary>Progress reported by the dispatched callback, in the range [0, 1].</summary>
        public float Progress
        {
            get
            {
                lock (_stateLock)
                {
                    return _progress;
                }
            }
        }

        /// <summary>The error that faulted this dispatch, or <c>null</c> when it has not faulted.</summary>
        public Exception Error
        {
            get
            {
                lock (_stateLock)
                {
                    return _error;
                }
            }
        }

        public bool IsCompleted
        {
            get
            {
                DispatchStatus status = Status;
                return status == DispatchStatus.Succeeded || status == DispatchStatus.Faulted;
            }
        }

        public bool IsFaulted => Status == DispatchStatus.Faulted;

        public bool IsQueued => Status == DispatchStatus.Queued;

        public bool IsRunning => Status == DispatchStatus.Running;

        public bool IsSucceeded => Status == DispatchStatus.Succeeded;

        /// <summary>A repeatable .NET completion task for this dispatch.</summary>
        public Task Task => _taskCompletion.Task;

        /// <summary>A Unity awaitable completion signal for this dispatch.</summary>
        public Awaitable Awaitable => _awaitableCompletion.Awaitable;

        /// <summary>
        /// Reports progress from the dispatched callback. Reports after completion are ignored.
        /// </summary>
        public void ReportProgress(float progress)
        {
            lock (_stateLock)
            {
                if (_status != DispatchStatus.Running)
                {
                    return;
                }

                _progress = Mathf.Clamp01(progress);
            }
        }

        internal bool TryStart()
        {
            lock (_stateLock)
            {
                if (_status != DispatchStatus.Queued)
                {
                    return false;
                }

                _status = DispatchStatus.Running;
                return true;
            }
        }

        internal bool TrySetSucceeded()
        {
            return TrySetSucceededCore(null, null);
        }

        internal virtual bool TrySetException(Exception error)
        {
            return TrySetExceptionCore(error, null);
        }

        protected bool TrySetSucceededCore(Action setResult, Action signalTypedCompletion)
        {
            lock (_stateLock)
            {
                if (_status != DispatchStatus.Running)
                {
                    return false;
                }

                setResult?.Invoke();
                _progress = 1f;
                _status = DispatchStatus.Succeeded;
            }

            _taskCompletion.TrySetResult(true);
            _awaitableCompletion.TrySetResult();
            signalTypedCompletion?.Invoke();
            Completed?.Invoke(this);
            return true;
        }

        protected bool TrySetExceptionCore(Exception error, Action signalTypedCompletion)
        {
            if (error == null)
            {
                error = new InvalidOperationException("A dispatch failed without an exception.");
            }

            lock (_stateLock)
            {
                if (_status == DispatchStatus.Succeeded || _status == DispatchStatus.Faulted)
                {
                    return false;
                }

                _error = error;
                _status = DispatchStatus.Faulted;
            }

            _taskCompletion.TrySetException(error);
            _awaitableCompletion.TrySetException(error);
            signalTypedCompletion?.Invoke();
            Completed?.Invoke(this);
            return true;
        }
    }

    /// <summary>
    /// Tracks a dispatched action that produces a value.
    /// </summary>
    public sealed class DispatchHandle<T> : DispatchHandle
    {
        private readonly TaskCompletionSource<T> _taskCompletion =
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AwaitableCompletionSource<T> _awaitableCompletion = new AwaitableCompletionSource<T>();

        private T _result;

        /// <summary>The result of a successfully completed dispatch.</summary>
        public T Result => _result;

        /// <summary>A repeatable .NET completion task that returns the dispatched result.</summary>
        public new Task<T> Task => _taskCompletion.Task;

        /// <summary>A Unity awaitable completion signal that returns the dispatched result.</summary>
        public new Awaitable<T> Awaitable => _awaitableCompletion.Awaitable;

        internal bool TrySetResult(T result)
        {
            return TrySetSucceededCore(
                () => _result = result,
                () =>
                {
                    _taskCompletion.TrySetResult(result);
                    _awaitableCompletion.TrySetResult(result);
                });
        }

        internal override bool TrySetException(Exception error)
        {
            if (error == null)
            {
                error = new InvalidOperationException("A dispatch failed without an exception.");
            }

            return TrySetExceptionCore(
                error,
                () =>
                {
                    _taskCompletion.TrySetException(error);
                    _awaitableCompletion.TrySetException(error);
                });
        }
    }
}
