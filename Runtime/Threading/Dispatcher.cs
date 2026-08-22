using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    [AddComponentMenu("Scheherazade/Common/Threading/Dispatcher")]
    public class Dispatcher :
        SingletonBehavior<Dispatcher>
    {
        private readonly object _actionsLock = new object();
        private Queue<Action> _actions = new Queue<Action>();
        private Queue<Action> _dispatchingActions = new Queue<Action>();

        private static int _mainThreadId;
        private static volatile bool _isAvailable;

        public static bool IsAvailable => _isAvailable;
        public static bool IsMainThread =>
            _isAvailable
            && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        protected override void Awake()
        {
            base.Awake();
            if (ReferenceEquals(Instance, this))
            {
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                _isAvailable = true;
            }
        }

        private void Update()
        {
            lock (_actionsLock)
            {
                if (_actions.Count == 0)
                {
                    return;
                }

                Queue<Action> queuedActions = _dispatchingActions;
                _dispatchingActions = _actions;
                _actions = queuedActions;
            }

            QuickLog.SDebug(
                "Dispatching {0} action(s) on main thread.",
                _dispatchingActions.Count
            );

            while (_dispatchingActions.Count > 0)
            {
                TryDispatchAction(_dispatchingActions.Dequeue());
            }
        }

        public void ClearActions()
        {
            lock (_actionsLock)
            {
                _actions.Clear();
            }
        }

        public void QueueAction(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (_actionsLock)
            {
                _actions.Enqueue(action);
            }
        }

        private void TryDispatchAction(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                QuickLog.SError(
                    "Exception occurred while dispatching action on main thread: {0}",
                    ex
                );
            }
        }

        public static void DispatchOnMainThread(Action action)
        {
            TryDispatchOnMainThread(action);
        }

        public static bool TryDispatchOnMainThread(Action action)
        {
            Dispatcher dispatcher = Instance;
            if (!_isAvailable || ReferenceEquals(dispatcher, null))
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Action cannot be dispatched on main thread."
                );
                return false;
            }

            dispatcher.QueueAction(action);
            return true;
        }

        public static void DispatchDelayedOnMainThread(Action action, float delaySeconds)
        {
            bool dispatched = TryDispatchOnMainThread(() =>
            {
                Dispatcher dispatcher = Instance;
                if (dispatcher != null)
                {
                    dispatcher.StartCoroutine(
                        DispatchDelayedInternal(action, delaySeconds));
                }
            });

            if (!dispatched)
            {
                QuickLog.SCritical(
                    "Delayed action cannot be dispatched on main thread.");
            }
        }

        public static async Task DispatchActionAsync(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (IsMainThread)
            {
                action.Invoke();
                return;
            }

            TaskCompletionSource<bool> completion
                = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            bool dispatched = TryDispatchOnMainThread(() =>
            {
                try
                {
                    action.Invoke();
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });

            if (!dispatched)
            {
                throw new InvalidOperationException(
                    "No Dispatcher instance is available.");
            }

            await completion.Task;
        }

        public static Coroutine DispatchCoroutine(IEnumerator coroutine)
        {
            TryDispatchCoroutine(coroutine, out Coroutine handle);
            return handle;
        }

        public static bool TryDispatchCoroutine(
            IEnumerator coroutine,
            out Coroutine handle)
        {
            handle = null;
            if (coroutine == null)
            {
                QuickLog.SError("Cannot dispatch a null coroutine.");
                return false;
            }

            Dispatcher dispatcher = Instance;
            if (!_isAvailable || ReferenceEquals(dispatcher, null))
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Coroutine cannot be dispatched on main thread."
                );
                return false;
            }

            if (!IsMainThread)
            {
                QuickLog.SError(
                    "Coroutines must be dispatched from the Unity main thread.");
                return false;
            }

            try
            {
                handle = dispatcher.StartCoroutine(coroutine);
                return true;
            }
            catch (Exception exception)
            {
                QuickLog.SError(
                    "Failed to dispatch coroutine: {0}",
                    exception);
                return false;
            }
        }

        private static IEnumerator DispatchDelayedInternal(Action action, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            action?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateDispatcher()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject dispatcher = new GameObject("[Scheherazade Action Dispatcher]");
            dispatcher.hideFlags = HideFlags.HideInHierarchy;
            dispatcher.AddComponent<KeepAliveComponent>();
            dispatcher.AddComponent<Dispatcher>();
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                _isAvailable = false;
                _mainThreadId = 0;
            }

            ClearActions();
            _dispatchingActions.Clear();
            base.OnDestroy();
        }

    }

    public static class DispatcherExtensions
    {
        public static void DispatchOnMainThread(this Action action)
            => Dispatcher.DispatchOnMainThread(action);

        public static void DispatchDelayedOnMainThread(this Action action, float delaySeconds)
            => Dispatcher.DispatchDelayedOnMainThread(action, delaySeconds);

        public static Task DispatchActionAsync(this Action action)
            => Dispatcher.DispatchActionAsync(action);

        public static void ContinueTaskOnMainThread(this Task task, Action<Task> continuationAction)
            => task.ContinueWith(t => Dispatcher.DispatchOnMainThread(() => continuationAction(t)));

        public static void ContinueTaskOnMainThread<T>(this Task<T> task, Action<Task<T>> continuationAction)
            => task.ContinueWith(t => Dispatcher.DispatchOnMainThread(() => continuationAction(t)));

        public static void ContinueTaskOnMainThreadAfterDelay(this Task task, Action<Task> continuationAction, float delaySeconds)
            => task.ContinueWith(
                t => Dispatcher.DispatchDelayedOnMainThread(
                    () => continuationAction(t),
                    delaySeconds
                )
            );
        
        public static Coroutine DispatchOnDispatcher(this IEnumerator coroutine)
            => Dispatcher.DispatchCoroutine(coroutine);
    }
}
