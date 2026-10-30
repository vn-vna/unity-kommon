#if PLA_CREATIVE_BUILD
using Awaitable = System.Threading.Tasks.Task;
using AwaitableCompletionSource = Com.Scheherazade.Common.Threading.CreativeCompletionSource;
#endif
using System;
using UnityEngine;

namespace Com.Scheherazade.Common.UserInterface
{
    /// <summary>
    /// Represents one animation without exposing its implementation library.
    /// Unity Awaitables are single-consumer, so an animation handle can only be awaited once.
    /// </summary>
    public sealed class AnimationHandle
    {
        private readonly AwaitableCompletionSource _completionSource = new();
        private readonly Awaitable _completionAwaitable;
        private readonly Action _play;
        private readonly Action _complete;
        private readonly Action _cancel;
        private AnimationStatus _status;
        private bool _hasStarted;
        private bool _stopRequested;
        private bool _awaiterRequested;

        public bool IsCompleted => _status != AnimationStatus.Pending;

        public AnimationHandle(
            Awaitable awaitable,
            Action play = null,
            Action complete = null,
            Action cancel = null
        )
        {
            if (awaitable == null)
            {
                throw new ArgumentNullException(nameof(awaitable));
            }

            _completionAwaitable = _completionSource.Awaitable;
            _play = play;
            _complete = complete;
#if PLA_CREATIVE_BUILD
            if (cancel == null && !awaitable.IsCompleted)
                throw new ArgumentException("A pending creative animation requires its real cancellation delegate.", nameof(cancel));
            _cancel = cancel;
#else
            _cancel = cancel ?? awaitable.Cancel;
#endif
            _ = ObserveCompletionAsync(awaitable);
        }

        public void Play()
        {
            if (_hasStarted || _stopRequested || IsCompleted)
            {
                return;
            }

            _hasStarted = true;
            _play?.Invoke();
        }

        public void Complete()
        {
            if (_stopRequested || IsCompleted)
            {
                return;
            }

            _stopRequested = true;
            if (_complete != null)
            {
                _complete.Invoke();
                return;
            }

            CancelCore();
        }

        public void Cancel()
        {
            if (_stopRequested || IsCompleted)
            {
                return;
            }

            _stopRequested = true;
            CancelCore();
        }

#if PLA_CREATIVE_BUILD
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter()
#else
        public Awaitable.Awaiter GetAwaiter()
#endif
        {
            if (_awaiterRequested)
            {
                throw new InvalidOperationException(
                    "An AnimationHandle can only be awaited once."
                );
            }

            _awaiterRequested = true;
            Play();
            return _completionAwaitable.GetAwaiter();
        }

        public static AnimationHandle CreateCompleted()
        {
            var completionSource = new AwaitableCompletionSource();
            Awaitable awaitable = completionSource.Awaitable;
            completionSource.SetResult();
            return new AnimationHandle(awaitable);
        }

        private async Awaitable ObserveCompletionAsync(Awaitable awaitable)
        {
            try
            {
                await awaitable;
                _status = AnimationStatus.Succeeded;
                _completionSource.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                _status = AnimationStatus.Canceled;
                _completionSource.TrySetCanceled();
            }
            catch (Exception exception)
            {
                _status = AnimationStatus.Faulted;
                _completionSource.TrySetException(exception);
            }
        }

        private void CancelCore()
        {
#if PLA_CREATIVE_BUILD
            _cancel?.Invoke(); // Null is allowed only for an already-completed task.
#else
            _cancel.Invoke();
#endif
        }

        private enum AnimationStatus
        {
            Pending,
            Succeeded,
            Canceled,
            Faulted
        }
    }
}
