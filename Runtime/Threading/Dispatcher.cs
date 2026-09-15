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
        private Queue<IDispatchWorkItem> _actions = new Queue<IDispatchWorkItem>();
        private Queue<IDispatchWorkItem> _dispatchingActions = new Queue<IDispatchWorkItem>();
        private readonly HashSet<DispatchHandle> _activeHandles = new HashSet<DispatchHandle>();

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

                Queue<IDispatchWorkItem> queuedActions = _dispatchingActions;
                _dispatchingActions = _actions;
                _actions = queuedActions;
            }

            QuickLog.SDebug(
                "Dispatching {0} action(s) on main thread.",
                _dispatchingActions.Count
            );

            while (_dispatchingActions.Count > 0)
            {
                _dispatchingActions.Dequeue().Execute();
            }
        }

        public void ClearActions()
        {
            List<IDispatchWorkItem> clearedActions;
            lock (_actionsLock)
            {
                clearedActions = new List<IDispatchWorkItem>(_actions);
                _actions.Clear();
            }

            FailActions(clearedActions, "Queued actions were cleared before they could be dispatched.");
        }

        public void QueueAction(Action action)
        {
            if (action == null)
            {
                return;
            }

            QueueWorkItem(new LegacyActionDispatchWorkItem(action));
        }

        private void QueueWorkItem(IDispatchWorkItem workItem)
        {
            lock (_actionsLock)
            {
                if (workItem.Handle != null)
                {
                    TrackHandle(workItem.Handle);
                }

                _actions.Enqueue(workItem);
            }
        }

        private void TrackHandle(DispatchHandle handle)
        {
            _activeHandles.Add(handle);
            handle.Completed += OnHandleCompleted;
        }

        private void OnHandleCompleted(DispatchHandle handle)
        {
            lock (_actionsLock)
            {
                handle.Completed -= OnHandleCompleted;
                _activeHandles.Remove(handle);
            }
        }

        private void FailActiveHandles(string message)
        {
            List<DispatchHandle> activeHandles;
            lock (_actionsLock)
            {
                activeHandles = new List<DispatchHandle>(_activeHandles);
                _activeHandles.Clear();
                foreach (DispatchHandle handle in activeHandles)
                {
                    handle.Completed -= OnHandleCompleted;
                }
            }

            foreach (DispatchHandle handle in activeHandles)
            {
                handle.TrySetException(new OperationCanceledException(message));
            }
        }

        private static void FailActions(IEnumerable<IDispatchWorkItem> actions, string message)
        {
            foreach (IDispatchWorkItem action in actions)
            {
                action.Fail(new OperationCanceledException(message));
            }
        }

        /// <summary>Queues a main-thread action and returns a handle for its lifecycle.</summary>
        public static DispatchHandle Dispatch(Action action)
        {
            TryDispatch(action, out DispatchHandle handle);
            return handle;
        }

        /// <summary>Queues a main-thread action that can report progress.</summary>
        public static DispatchHandle Dispatch(Action<DispatchHandle> action)
        {
            TryDispatch(action, out DispatchHandle handle);
            return handle;
        }

        /// <summary>Queues a main-thread function and returns a handle for its result.</summary>
        public static DispatchHandle<T> Dispatch<T>(Func<T> action)
        {
            TryDispatch(action, out DispatchHandle<T> handle);
            return handle;
        }

        /// <summary>Queues a main-thread function that can report progress.</summary>
        public static DispatchHandle<T> Dispatch<T>(Func<DispatchHandle<T>, T> action)
        {
            TryDispatch(action, out DispatchHandle<T> handle);
            return handle;
        }

        /// <summary>Queues an awaitable main-thread action.</summary>
        public static DispatchHandle DispatchAwaitable(Func<Awaitable> action)
        {
            TryDispatchAwaitable(action, out DispatchHandle handle);
            return handle;
        }

        /// <summary>Queues an awaitable main-thread action that can report progress.</summary>
        public static DispatchHandle DispatchAwaitable(Func<DispatchHandle, Awaitable> action)
        {
            TryDispatchAwaitable(action, out DispatchHandle handle);
            return handle;
        }

        /// <summary>Queues an awaitable main-thread function.</summary>
        public static DispatchHandle<T> DispatchAwaitable<T>(Func<Awaitable<T>> action)
        {
            TryDispatchAwaitable(action, out DispatchHandle<T> handle);
            return handle;
        }

        /// <summary>Queues an awaitable main-thread function that can report progress.</summary>
        public static DispatchHandle<T> DispatchAwaitable<T>(Func<DispatchHandle<T>, Awaitable<T>> action)
        {
            TryDispatchAwaitable(action, out DispatchHandle<T> handle);
            return handle;
        }

        public static bool TryDispatch(Action action, out DispatchHandle handle)
        {
            handle = new DispatchHandle();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new ActionDispatchWorkItem(handle, _ => action.Invoke()));
        }

        public static bool TryDispatch(Action<DispatchHandle> action, out DispatchHandle handle)
        {
            handle = new DispatchHandle();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new ActionDispatchWorkItem(handle, action));
        }

        public static bool TryDispatch<T>(Func<T> action, out DispatchHandle<T> handle)
        {
            handle = new DispatchHandle<T>();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new ResultDispatchWorkItem<T>(handle, _ => action.Invoke()));
        }

        public static bool TryDispatch<T>(Func<DispatchHandle<T>, T> action, out DispatchHandle<T> handle)
        {
            handle = new DispatchHandle<T>();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new ResultDispatchWorkItem<T>(handle, action));
        }

        public static bool TryDispatchAwaitable(Func<Awaitable> action, out DispatchHandle handle)
        {
            handle = new DispatchHandle();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new AwaitableDispatchWorkItem(handle, _ => action.Invoke()));
        }

        public static bool TryDispatchAwaitable(Func<DispatchHandle, Awaitable> action, out DispatchHandle handle)
        {
            handle = new DispatchHandle();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new AwaitableDispatchWorkItem(handle, action));
        }

        public static bool TryDispatchAwaitable<T>(Func<Awaitable<T>> action, out DispatchHandle<T> handle)
        {
            handle = new DispatchHandle<T>();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new AwaitableResultDispatchWorkItem<T>(handle, _ => action.Invoke()));
        }

        public static bool TryDispatchAwaitable<T>(Func<DispatchHandle<T>, Awaitable<T>> action, out DispatchHandle<T> handle)
        {
            handle = new DispatchHandle<T>();
            if (action == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(action)));
            }

            return TryQueueWorkItem(handle, new AwaitableResultDispatchWorkItem<T>(handle, action));
        }

        private static bool TryQueueWorkItem(DispatchHandle handle, IDispatchWorkItem workItem)
        {
            Dispatcher dispatcher = Instance;
            if (!_isAvailable || ReferenceEquals(dispatcher, null))
            {
                QuickLog.SCritical(
                    "No Dispatcher instance found. " +
                    "Action cannot be dispatched on main thread."
                );
                return FailDispatch(
                    handle,
                    new InvalidOperationException("No Dispatcher instance is available."));
            }

            dispatcher.QueueWorkItem(workItem);
            return true;
        }

        private static bool FailDispatch(DispatchHandle handle, Exception error)
        {
            handle.TrySetException(error);
            return false;
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

        /// <summary>
        /// Creates an optional writable context for a manually dispatched coroutine.
        /// </summary>
        public static DispatcherContext CreateContext()
        {
            DispatchHandle handle = new DispatchHandle();
            ActivateManualContext(handle);
            return new DispatcherContext(handle);
        }

        /// <summary>
        /// Creates an optional writable context for a manually dispatched coroutine that returns a value.
        /// </summary>
        public static DispatcherContext<T> CreateContext<T>()
        {
            DispatchHandle<T> handle = new DispatchHandle<T>();
            ActivateManualContext(handle);
            return new DispatcherContext<T>(handle);
        }

        /// <summary>
        /// Queues a coroutine factory and injects a context that tracks its progress and completion.
        /// </summary>
        public static DispatchHandle DispatchCoroutine(
            Func<DispatcherContext, IEnumerator> coroutineFactory)
        {
            TryDispatchCoroutine(coroutineFactory, out DispatchHandle handle);
            return handle;
        }

        /// <summary>
        /// Queues a coroutine factory and injects a context that tracks its result.
        /// </summary>
        public static DispatchHandle<T> DispatchCoroutine<T>(
            Func<DispatcherContext<T>, IEnumerator> coroutineFactory)
        {
            TryDispatchCoroutine(coroutineFactory, out DispatchHandle<T> handle);
            return handle;
        }

        public static bool TryDispatchCoroutine(
            Func<DispatcherContext, IEnumerator> coroutineFactory,
            out DispatchHandle handle)
        {
            handle = new DispatchHandle();
            DispatcherContext context = new DispatcherContext(handle);
            if (coroutineFactory == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(coroutineFactory)));
            }

            return TryQueueWorkItem(
                handle,
                new ContextCoroutineDispatchWorkItem(
                    context,
                    () => coroutineFactory.Invoke(context)));
        }

        public static bool TryDispatchCoroutine<T>(
            Func<DispatcherContext<T>, IEnumerator> coroutineFactory,
            out DispatchHandle<T> handle)
        {
            handle = new DispatchHandle<T>();
            DispatcherContext<T> context = new DispatcherContext<T>(handle);
            if (coroutineFactory == null)
            {
                return FailDispatch(handle, new ArgumentNullException(nameof(coroutineFactory)));
            }

            return TryQueueWorkItem(
                handle,
                new ContextCoroutineDispatchWorkItem(
                    context,
                    () => coroutineFactory.Invoke(context)));
        }

        private static void ActivateManualContext(DispatchHandle handle)
        {
            Dispatcher dispatcher = Instance;
            if (!_isAvailable || ReferenceEquals(dispatcher, null))
            {
                handle.TrySetException(
                    new InvalidOperationException("No Dispatcher instance is available."));
                return;
            }

            handle.TryStart();
            lock (dispatcher._actionsLock)
            {
                dispatcher.TrackHandle(handle);
            }
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

        private interface IDispatchWorkItem
        {
            DispatchHandle Handle { get; }
            void Execute();
            void Fail(Exception error);
        }

        private sealed class ContextCoroutineDispatchWorkItem : IDispatchWorkItem
        {
            private readonly IDispatcherContext _context;
            private readonly Func<IEnumerator> _coroutineFactory;

            public ContextCoroutineDispatchWorkItem(
                IDispatcherContext context,
                Func<IEnumerator> coroutineFactory)
            {
                _context = context;
                _coroutineFactory = coroutineFactory;
            }

            public DispatchHandle Handle => _context.UntypedHandle;

            public void Execute()
            {
                if (!_context.TryStart())
                {
                    return;
                }

                try
                {
                    IEnumerator coroutine = _coroutineFactory.Invoke();
                    if (coroutine == null)
                    {
                        throw new InvalidOperationException("Coroutine factory returned null.");
                    }

                    Dispatcher dispatcher = Instance;
                    if (ReferenceEquals(dispatcher, null))
                    {
                        throw new InvalidOperationException("No Dispatcher instance is available.");
                    }

                    dispatcher.StartCoroutine(RunTrackedCoroutine(_context, coroutine));
                }
                catch (Exception exception)
                {
                    _context.TrySetException(exception);
                    LogDispatchException(exception);
                }
            }

            public void Fail(Exception error)
            {
                _context.TrySetException(error);
            }
        }

        private sealed class LegacyActionDispatchWorkItem : IDispatchWorkItem
        {
            private readonly Action _action;

            public LegacyActionDispatchWorkItem(Action action)
            {
                _action = action;
            }

            public DispatchHandle Handle => null;

            public void Execute()
            {
                try
                {
                    _action.Invoke();
                }
                catch (Exception exception)
                {
                    LogDispatchException(exception);
                }
            }

            public void Fail(Exception error)
            {
            }
        }

        private sealed class ActionDispatchWorkItem : IDispatchWorkItem
        {
            private readonly DispatchHandle _handle;
            private readonly Action<DispatchHandle> _action;

            public ActionDispatchWorkItem(DispatchHandle handle, Action<DispatchHandle> action)
            {
                _handle = handle;
                _action = action;
            }

            public DispatchHandle Handle => _handle;

            public void Execute()
            {
                if (!_handle.TryStart())
                {
                    return;
                }

                try
                {
                    _action.Invoke(_handle);
                    _handle.TrySetSucceeded();
                }
                catch (Exception exception)
                {
                    _handle.TrySetException(exception);
                    LogDispatchException(exception);
                }
            }

            public void Fail(Exception error)
            {
                _handle.TrySetException(error);
            }
        }

        private sealed class ResultDispatchWorkItem<T> : IDispatchWorkItem
        {
            private readonly DispatchHandle<T> _handle;
            private readonly Func<DispatchHandle<T>, T> _action;

            public ResultDispatchWorkItem(DispatchHandle<T> handle, Func<DispatchHandle<T>, T> action)
            {
                _handle = handle;
                _action = action;
            }

            public DispatchHandle Handle => _handle;

            public void Execute()
            {
                if (!_handle.TryStart())
                {
                    return;
                }

                try
                {
                    _handle.TrySetResult(_action.Invoke(_handle));
                }
                catch (Exception exception)
                {
                    _handle.TrySetException(exception);
                    LogDispatchException(exception);
                }
            }

            public void Fail(Exception error)
            {
                _handle.TrySetException(error);
            }
        }

        private sealed class AwaitableDispatchWorkItem : IDispatchWorkItem
        {
            private readonly DispatchHandle _handle;
            private readonly Func<DispatchHandle, Awaitable> _action;

            public AwaitableDispatchWorkItem(
                DispatchHandle handle,
                Func<DispatchHandle, Awaitable> action)
            {
                _handle = handle;
                _action = action;
            }

            public DispatchHandle Handle => _handle;

            public void Execute()
            {
                if (_handle.TryStart())
                {
                    _ = ExecuteAsync();
                }
            }

            public void Fail(Exception error)
            {
                _handle.TrySetException(error);
            }

            private async Awaitable ExecuteAsync()
            {
                try
                {
                    Awaitable operation = _action.Invoke(_handle);
                    if (operation == null)
                    {
                        throw new InvalidOperationException("Dispatched awaitable action returned null.");
                    }

                    await operation;
                    _handle.TrySetSucceeded();
                }
                catch (Exception exception)
                {
                    _handle.TrySetException(exception);
                    LogDispatchException(exception);
                }
            }
        }

        private sealed class AwaitableResultDispatchWorkItem<T> : IDispatchWorkItem
        {
            private readonly DispatchHandle<T> _handle;
            private readonly Func<DispatchHandle<T>, Awaitable<T>> _action;

            public AwaitableResultDispatchWorkItem(
                DispatchHandle<T> handle,
                Func<DispatchHandle<T>, Awaitable<T>> action)
            {
                _handle = handle;
                _action = action;
            }

            public DispatchHandle Handle => _handle;

            public void Execute()
            {
                if (_handle.TryStart())
                {
                    _ = ExecuteAsync();
                }
            }

            public void Fail(Exception error)
            {
                _handle.TrySetException(error);
            }

            private async Awaitable ExecuteAsync()
            {
                try
                {
                    Awaitable<T> operation = _action.Invoke(_handle);
                    if (operation == null)
                    {
                        throw new InvalidOperationException("Dispatched awaitable action returned null.");
                    }

                    T result = await operation;
                    _handle.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    _handle.TrySetException(exception);
                    LogDispatchException(exception);
                }
            }
        }

        private static void LogDispatchException(Exception exception)
        {
            QuickLog.SError(
                "Exception occurred while dispatching action on main thread: {0}",
                exception
            );
        }

        private static IEnumerator RunTrackedCoroutine(
            IDispatcherContext context,
            IEnumerator rootCoroutine)
        {
            Stack<IEnumerator> coroutineStack = new Stack<IEnumerator>();
            coroutineStack.Push(rootCoroutine);

            while (coroutineStack.Count > 0)
            {
                IEnumerator coroutine = coroutineStack.Peek();
                bool hasNext;
                object yieldedValue = null;

                try
                {
                    hasNext = coroutine.MoveNext();
                    if (hasNext)
                    {
                        yieldedValue = coroutine.Current;
                    }
                }
                catch (Exception exception)
                {
                    context.TrySetException(exception);
                    LogDispatchException(exception);
                    DisposeCoroutines(coroutineStack);
                    yield break;
                }

                if (!hasNext)
                {
                    coroutineStack.Pop();
                    (coroutine as IDisposable)?.Dispose();
                    continue;
                }

                if (yieldedValue is IEnumerator nestedCoroutine)
                {
                    coroutineStack.Push(nestedCoroutine);
                    continue;
                }

                yield return yieldedValue;
            }

            context.CompleteAtCoroutineEnd();
        }

        private static void DisposeCoroutines(Stack<IEnumerator> coroutines)
        {
            while (coroutines.Count > 0)
            {
                try
                {
                    (coroutines.Pop() as IDisposable)?.Dispose();
                }
                catch (Exception exception)
                {
                    QuickLog.SError("Failed to dispose dispatched coroutine: {0}", exception);
                }
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
            FailActions(
                _dispatchingActions,
                "Dispatcher was destroyed before queued actions could be dispatched.");
            _dispatchingActions.Clear();
            FailActiveHandles("Dispatcher was destroyed before the dispatched action completed.");
            base.OnDestroy();
        }

    }

    public static class DispatcherExtensions
    {
        public static DispatchHandle Dispatch(this Action action)
            => Dispatcher.Dispatch(action);

        public static DispatchHandle Dispatch(this Action<DispatchHandle> action)
            => Dispatcher.Dispatch(action);

        public static DispatchHandle<T> Dispatch<T>(this Func<T> action)
            => Dispatcher.Dispatch(action);

        public static DispatchHandle<T> Dispatch<T>(this Func<DispatchHandle<T>, T> action)
            => Dispatcher.Dispatch(action);

        public static DispatchHandle DispatchAwaitable(this Func<Awaitable> action)
            => Dispatcher.DispatchAwaitable(action);

        public static DispatchHandle DispatchAwaitable(this Func<DispatchHandle, Awaitable> action)
            => Dispatcher.DispatchAwaitable(action);

        public static DispatchHandle<T> DispatchAwaitable<T>(this Func<Awaitable<T>> action)
            => Dispatcher.DispatchAwaitable(action);

        public static DispatchHandle<T> DispatchAwaitable<T>(this Func<DispatchHandle<T>, Awaitable<T>> action)
            => Dispatcher.DispatchAwaitable(action);

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
