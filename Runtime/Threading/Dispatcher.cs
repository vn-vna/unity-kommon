using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    /// <summary>
    /// Manages the execution of actions and coroutines on Unity's main thread.
    /// </summary>
    /// <remarks>
    /// This singleton component allows background threads to queue actions for execution on the main Unity thread,
    /// which is necessary for Unity API calls that must run on the main thread. It processes queued actions
    /// in each Update cycle and provides both immediate and delayed execution options.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Queue an action from a background thread
    /// Task.Run(() => {
    ///     // Do background work
    ///     Dispatcher.DispatchOnMainThread(() => {
    ///         // This runs on the main thread
    ///         transform.position = new Vector3(1, 2, 3);
    ///     });
    /// });
    /// 
    /// // Dispatch with a delay
    /// Dispatcher.DispatchDelayedOnMainThread(() => {
    ///     Debug.Log("Executed after delay");
    /// }, 2.0f);
    /// 
    /// // Dispatch a coroutine
    /// Dispatcher.DispatchCoroutine(MyCoroutine());
    /// </code>
    /// </example>
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

                    TryDispatchAction(action);
                }
            }
        }

        /// <summary>
        /// Clears all queued actions that are waiting to be executed.
        /// </summary>
        public void ClearActions()
        {
            lock (this)
            {
                _actions.Clear();
            }
        }

        /// <summary>
        /// Queues an action for execution on the main thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public void QueueAction(Action action)
        {
            lock (this)
            {
                _actions ??= new Queue<Action>();
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

        /// <summary>
        /// Dispatches an action to be executed on the main thread.
        /// </summary>
        /// <param name="action">The action to execute on the main thread.</param>
        /// <remarks>
        /// This is a static helper method that queues the action for execution in the next Update cycle.
        /// Safe to call from background threads.
        /// </remarks>
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

        /// <summary>
        /// Dispatches an action to be executed on the main thread after a delay.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="delaySeconds">The delay in seconds before executing the action.</param>
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

        /// <summary>
        /// Executes an action asynchronously.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task DispatchActionAsync(Action action)
        {
            action?.Invoke();
        }

        /// <summary>
        /// Dispatches a coroutine to be executed on the main thread.
        /// </summary>
        /// <param name="coroutine">The coroutine to execute.</param>
        /// <returns>The Coroutine object, or null if the Dispatcher instance is not available.</returns>
        public static Coroutine DispatchCoroutine(IEnumerator coroutine)
        {
            if (Instance == null)
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Coroutine cannot be dispatched on main thread."
                );
                return null;
            }

            return Instance.StartCoroutine(coroutine);
        }

        private static IEnumerator DispatchDelayedInternal(Action action, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            action?.Invoke();
        }

    }

    /// <summary>
    /// Provides extension methods for convenient dispatcher usage with actions, tasks, and coroutines.
    /// </summary>
    /// <remarks>
    /// These extension methods allow for more fluent syntax when working with the Dispatcher.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Extension method usage
    /// Action myAction = () => Debug.Log("Hello");
    /// myAction.DispatchOnMainThread();
    /// 
    /// // Task continuation on main thread
    /// Task.Run(() => DoWork())
    ///     .ContinueTaskOnMainThread(t => Debug.Log("Work completed"));
    /// 
    /// // Coroutine dispatch
    /// MyCoroutine().DispatchOnDispatcher();
    /// </code>
    /// </example>
    public static class DispatcherExtensions
    {
        /// <summary>
        /// Dispatches this action to be executed on the main thread.
        /// </summary>
        /// <param name="action">The action to dispatch.</param>
        public static void DispatchOnMainThread(this Action action)
            => Dispatcher.DispatchOnMainThread(action);

        /// <summary>
        /// Dispatches this action to be executed on the main thread after a delay.
        /// </summary>
        /// <param name="action">The action to dispatch.</param>
        /// <param name="delaySeconds">The delay in seconds.</param>
        public static void DispatchDelayedOnMainThread(this Action action, float delaySeconds)
            => Dispatcher.DispatchDelayedOnMainThread(action, delaySeconds);

        /// <summary>
        /// Executes this action asynchronously.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task DispatchActionAsync(this Action action)
            => Dispatcher.DispatchActionAsync(action);

        /// <summary>
        /// Continues this task with an action executed on the main thread.
        /// </summary>
        /// <param name="task">The task to continue.</param>
        /// <param name="continuationAction">The continuation action to execute on the main thread.</param>
        public static void ContinueTaskOnMainThread(this Task task, Action<Task> continuationAction)
            => task.ContinueWith(t => Dispatcher.DispatchOnMainThread(() => continuationAction(t)));

        /// <summary>
        /// Continues this task with an action executed on the main thread.
        /// </summary>
        /// <typeparam name="T">The type of the task result.</typeparam>
        /// <param name="task">The task to continue.</param>
        /// <param name="continuationAction">The continuation action to execute on the main thread.</param>
        public static void ContinueTaskOnMainThread<T>(this Task<T> task, Action<Task<T>> continuationAction)
            => task.ContinueWith(t => Dispatcher.DispatchOnMainThread(() => continuationAction(t)));

        /// <summary>
        /// Continues this task with an action executed on the main thread after a delay.
        /// </summary>
        /// <param name="task">The task to continue.</param>
        /// <param name="continuationAction">The continuation action to execute.</param>
        /// <param name="delaySeconds">The delay in seconds before executing the continuation.</param>
        public static void ContinueTaskOnMainThreadAfterDelay(this Task task, Action<Task> continuationAction, float delaySeconds)
            => task.ContinueWith(
                t => Dispatcher.DispatchDelayedOnMainThread(
                    () => continuationAction(t),
                    delaySeconds
                )
            );
        
        /// <summary>
        /// Dispatches this coroutine to be executed on the Dispatcher.
        /// </summary>
        /// <param name="coroutine">The coroutine to dispatch.</param>
        /// <returns>The Coroutine object, or null if the Dispatcher instance is not available.</returns>
        public static Coroutine DispatchOnDispatcher(this IEnumerator coroutine)
            => Dispatcher.DispatchCoroutine(coroutine);
    }
}