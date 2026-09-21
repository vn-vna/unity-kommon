using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
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
            _cancel = cancel ?? awaitable.Cancel;
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

        public Awaitable.Awaiter GetAwaiter()
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
            _cancel.Invoke();
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
