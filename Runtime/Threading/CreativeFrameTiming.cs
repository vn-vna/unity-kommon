#if PLA_CREATIVE_BUILD
#if UNITY_LUNA
using Thread = Com.Scheherazade.Common.Threading.CreativeBrowserThread;
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
#if UNITY_LUNA || UNITY_EDITOR
using System.Threading.Tasks;
#endif
using UnityEngine;

namespace Com.Scheherazade.Common.Threading
{
    /// <summary>Creative-only frame waits, driven by the existing Dispatcher's lifetime and Update.</summary>
    public static class CreativeFrameTiming
    {
        private static CreativeFrameQueue _queue;

        internal static void Initialize(int frame, float scaledTime)
        {
            _queue?.Dispose();
            _queue = new CreativeFrameQueue(frame, scaledTime);
        }

        internal static void Advance(int frame, float scaledTime) => _queue?.Advance(frame, scaledTime);
        internal static void Shutdown() => _queue?.Dispose();

        private static CreativeFrameQueue Queue
        {
            get
            {
                if (_queue == null)
                    throw new InvalidOperationException("The main-thread Dispatcher must initialize before creative frame waits.");
                if (_queue.IsMainThread) _queue.SynchronizeClock(Time.frameCount, Time.time);
                return _queue;
            }
        }

        public static CreativeFrameAwaitable NextFrameAsync(CancellationToken cancellationToken = default)
            => Queue.NextFrameAsync(cancellationToken);

        public static CreativeFrameAwaitable WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
            => Queue.WaitForSecondsAsync(seconds, cancellationToken);

        public static CreativeFrameAwaitable MainThreadAsync() => Queue.MainThreadAsync();

        /// <summary>Registers owner cleanup that runs before this dispatcher cancels its frame waits.</summary>
        public static IDisposable RegisterOwnerShutdown(Action shutdown) => Queue.RegisterOwnerShutdown(shutdown);
    }

    /// <summary>
    /// Single-main-thread clock with thread-safe registration. A snapshot/epoch prevents
    /// a continuation's next wait from being consumed by the same Update. Cancellation
    /// completes immediately on the owner thread, or is marshalled to its next pump.
    /// No timers, worker scheduling, or completed-task substitutes for frame waits.
    /// </summary>
    public sealed class CreativeFrameQueue : IDisposable
    {
        private readonly object _gate = new object();
        private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<WaitOperation> _waiting = new List<WaitOperation>();
        private readonly List<Action> _posted = new List<Action>();
        private readonly List<ShutdownRegistration> _shutdown = new List<ShutdownRegistration>();
        private List<WaitOperation> _completing;
        private int _frame;
        private int _lastPumpedFrame;
        private float _scaledTime;
        private bool _closed;

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public CreativeFrameQueue(int frame = 0, float scaledTime = 0f)
        {
            _frame = frame;
            _lastPumpedFrame = frame - 1;
            _scaledTime = scaledTime;
        }

        public void SynchronizeClock(int frame, float scaledTime)
        {
            RequireMainThread();
            lock (_gate) { _frame = frame; _scaledTime = scaledTime; }
        }

        public IDisposable RegisterOwnerShutdown(Action shutdown)
        {
            RequireMainThread();
            if (shutdown == null) throw new ArgumentNullException(nameof(shutdown));
            lock (_gate)
            {
                if (_closed) throw new OperationCanceledException("The creative frame owner has already shut down.");
                var registration = new ShutdownRegistration(this, shutdown);
                _shutdown.Add(registration);
                return registration;
            }
        }

        public CreativeFrameAwaitable NextFrameAsync(CancellationToken cancellationToken = default)
            => Enqueue(0f, false, cancellationToken);

        public CreativeFrameAwaitable WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
        {
            if (float.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            return Enqueue(Math.Max(0f, seconds), false, cancellationToken);
        }

        public CreativeFrameAwaitable MainThreadAsync()
        {
            if (!IsMainThread) return Enqueue(0f, true, default);
            // Teardown callbacks may still need to finish Unity-owned cleanup.
            // Already being on the owner thread does not require an open frame queue.
            var operation = new WaitOperation(this, _frame, _scaledTime, default);
            Complete(operation, null);
            return new CreativeFrameAwaitable(operation);
        }

        private CreativeFrameAwaitable Enqueue(float seconds, bool mainThreadOnly, CancellationToken token)
        {
            WaitOperation operation;
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(CreativeFrameQueue));
                operation = new WaitOperation(this, mainThreadOnly ? _frame : _frame + 1,
                    _scaledTime + seconds, token);
                _waiting.Add(operation);
            }
            if (mainThreadOnly && IsMainThread) Complete(operation, null);
            if (token.CanBeCanceled)
            {
                CancellationTokenRegistration registration = token.Register(() =>
                {
                    if (IsMainThread) Complete(operation, new OperationCanceledException(token));
                    else Post(() => Complete(operation, new OperationCanceledException(token)));
                });
                bool dispose;
                lock (_gate)
                {
                    dispose = operation.Completed;
                    if (!dispose) operation.CancellationRegistration = registration;
                }
                if (dispose) registration.Dispose();
            }
            return new CreativeFrameAwaitable(operation);
        }

        public void Advance(int frame, float scaledTime)
        {
            RequireMainThread();
            List<WaitOperation> ready;
            List<Action> posted;
            lock (_gate)
            {
                if (_closed || frame <= _lastPumpedFrame) return;
                _frame = frame;
                _scaledTime = scaledTime;
                _lastPumpedFrame = frame;
                ready = new List<WaitOperation>();
                for (int index = 0; index < _waiting.Count;)
                {
                    WaitOperation operation = _waiting[index];
                    if (operation.Completed || operation.Token.IsCancellationRequested
                        || (frame >= operation.DueFrame && scaledTime >= operation.DueTime))
                    {
                        _waiting.RemoveAt(index);
                        if (!operation.Completed) ready.Add(operation);
                    }
                    else index++;
                }
                _completing = ready;
                posted = new List<Action>(_posted);
                _posted.Clear();
            }
            List<Exception> failures = null;
            try
            {
                foreach (Action action in posted) Invoke(action, ref failures);
                foreach (WaitOperation operation in ready)
                {
                    Invoke(() => Complete(operation, operation.Token.IsCancellationRequested
                        ? new OperationCanceledException(operation.Token) : null), ref failures);
                }
            }
            finally { lock (_gate) _completing = null; }
            if (failures != null) throw new AggregateException(failures);
        }

        public void Dispose()
        {
            RequireMainThread();
            List<WaitOperation> pending;
            List<Action> posted;
            List<ShutdownRegistration> shutdown;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                pending = new List<WaitOperation>(_waiting);
                if (_completing != null) pending.AddRange(_completing);
                _waiting.Clear();
                posted = new List<Action>(_posted);
                _posted.Clear();
                shutdown = new List<ShutdownRegistration>(_shutdown);
                _shutdown.Clear();
            }
            List<Exception> failures = null;
            // Owners first detach Unity-facing state and cancel their logical runs.
            // Late worker callbacks can then only deliver/observe terminal failure.
            foreach (ShutdownRegistration registration in shutdown)
                Invoke(registration.Notify, ref failures);
            foreach (WaitOperation operation in pending)
                Invoke(() => Complete(operation, new OperationCanceledException("The creative frame dispatcher was destroyed.")), ref failures);
            foreach (Action action in posted) Invoke(action, ref failures);
            if (failures != null) throw new AggregateException(failures);
        }

        private void Complete(WaitOperation operation, Exception error)
        {
            RequireMainThread();
            Action continuation;
            CancellationTokenRegistration registration;
            lock (_gate)
            {
                if (operation.Completed) return;
                operation.Completed = true;
                operation.Error = error;
                continuation = operation.Continuation;
                operation.Continuation = null;
                registration = operation.CancellationRegistration;
                operation.CancellationRegistration = default;
            }
            registration.Dispose();
            continuation?.Invoke();
        }

        private void Post(Action continuation)
        {
            lock (_gate)
            {
                if (_closed) return; // Shutdown already settles every registered operation.
                _posted.Add(continuation);
            }
        }

        private static void Invoke(Action action, ref List<Exception> failures)
        {
            try { action(); }
            catch (Exception exception) { (failures ??= new List<Exception>()).Add(exception); }
        }

        private void RequireMainThread()
        {
            if (!IsMainThread) throw new InvalidOperationException("The creative frame queue must be pumped/disposed on its owner thread.");
        }

        private sealed class ShutdownRegistration : IDisposable
        {
            private readonly CreativeFrameQueue _owner;
            private Action _shutdown;

            internal ShutdownRegistration(CreativeFrameQueue owner, Action shutdown)
            { _owner = owner; _shutdown = shutdown; }

            internal void Notify()
            {
                Action shutdown;
                lock (_owner._gate) { shutdown = _shutdown; _shutdown = null; }
                shutdown?.Invoke();
            }

            public void Dispose()
            {
                _owner.RequireMainThread();
                lock (_owner._gate)
                {
                    _shutdown = null;
                    _owner._shutdown.Remove(this);
                }
            }
        }

        internal sealed class WaitOperation
        {
            private readonly CreativeFrameQueue _owner;
            internal readonly int DueFrame;
            internal readonly float DueTime;
            internal readonly CancellationToken Token;
            internal bool Completed;
            internal Exception Error;
            internal Action Continuation;
            internal CancellationTokenRegistration CancellationRegistration;
            private bool _continuationRegistered;

            internal WaitOperation(CreativeFrameQueue owner, int frame, float time, CancellationToken token)
            { _owner = owner; DueFrame = frame; DueTime = time; Token = token; }

            internal bool IsCompleted
            {
                get { lock (_owner._gate) return Completed && (_owner.IsMainThread || (_owner._closed && Error != null)); }
            }

            internal void OnCompleted(Action continuation)
            {
                if (continuation == null) throw new ArgumentNullException(nameof(continuation));
                bool completed;
                lock (_owner._gate)
                {
                    if (_continuationRegistered) throw new InvalidOperationException("A creative frame wait can only be consumed once.");
                    _continuationRegistered = true;
                    completed = Completed;
                    if (!completed) Continuation = continuation;
                    else if (!_owner.IsMainThread)
                    {
                        if (_owner._closed)
                        {
                            // A dead pump cannot accept this continuation. Deliver only terminal
                            // failure on this worker; never claim a successful owner-thread switch.
                            Error ??= new OperationCanceledException("The creative frame owner closed before the continuation was registered.");
                        }
                        else
                        {
                            _owner._posted.Add(continuation);
                            return;
                        }
                    }
                }
                // Even the closed-owner error path must resume an async state machine;
                // throwing from OnCompleted can strand its returned Task. Never invoke under _gate.
                if (completed) continuation();
            }

            internal void GetResult()
            {
                lock (_owner._gate)
                {
                    if (!Completed) throw new InvalidOperationException("The creative frame wait has not completed.");
                    if (Error != null) throw Error;
                }
            }
        }
    }

    public readonly struct CreativeFrameAwaitable
    {
        private readonly CreativeFrameQueue.WaitOperation _operation;
        internal CreativeFrameAwaitable(CreativeFrameQueue.WaitOperation operation) { _operation = operation; }
#if UNITY_LUNA
        // The generic framework awaiter is scriptable. The nongeneric Bridge names
        // are ambiguous, and custom INotifyCompletion bases cannot be emitted.
        public TaskAwaiter<bool> GetAwaiter() => ConsumeAsTask().GetAwaiter();
#else
        public Awaiter GetAwaiter() => new Awaiter(_operation);
#endif

#if UNITY_LUNA || UNITY_EDITOR
        /// <summary>
        /// A fresh consumption reserves the existing one-shot wait immediately.
        /// It never advances the clock or manufactures completion. A stored error,
        /// including OCE, faults this private promise and is rethrown unchanged.
        /// There was no public frame Task-state contract before this adapter.
        /// </summary>
        internal Task<bool> ConsumeAsTask()
        {
            var completion = new TaskCompletionSource<bool>();
            CreativeFrameQueue.WaitOperation operation = _operation;
            operation.OnCompleted(() =>
            {
                try
                {
                    operation.GetResult();
                    completion.TrySetResult(true);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            });
            return completion.Task;
        }
#endif

#if !UNITY_LUNA
        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly CreativeFrameQueue.WaitOperation _operation;
            internal Awaiter(CreativeFrameQueue.WaitOperation operation) { _operation = operation; }
            public bool IsCompleted => _operation.IsCompleted;
            public void OnCompleted(Action continuation) => _operation.OnCompleted(continuation);
            public void GetResult() => _operation.GetResult();
        }
#endif
    }
}
#endif
