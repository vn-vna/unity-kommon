using System;
using System.Collections;
using System.Collections.Generic;
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
        private Queue<Action> _actions;

        protected override void Awake()
        {
            _actions = new Queue<Action>();
            base.Awake();
        }

        private void Update()
        {
            if (_actions.Count == 0)
            {
                return;
            }

            QuickLog.SDebug(
                "Dispatching {0} action(s) on main thread.",
                _actions.Count
            );

            lock (this)
            {
                while (_actions.Count > 0)
                {
                    Action action;
                    action = _actions.Dequeue();
                    action?.Invoke();
                }
            }
        }

        public void QueueAction(Action action)
        {
            lock (this)
            {
                _actions.Enqueue(action);
            }
        }

        public static void DispatchOnMainThread(Action action)
        {
            if (Instance == null)
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Action cannot be dispatched on main thread."
                );
                return;
            }

            Instance.QueueAction(action);
        }

        public static void DispatchDelayedOnMainThread(Action action, float delaySeconds)
        {
            if (Instance == null)
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Delayed action cannot be dispatched on main thread."
                );
                return;
            }

            Instance.StartCoroutine(DispatchDelayedInternal(action, delaySeconds));
        }

        public static async Task DispatchActionAsync(Action action)
        {
            action?.Invoke();
        }

        private static IEnumerator DispatchDelayedInternal(Action action, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            action?.Invoke();
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
    }
}