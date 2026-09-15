using System;

namespace Com.Hapiga.Scheherazade.Common.Threading
{
    internal interface IDispatcherContext
    {
        DispatchHandle UntypedHandle { get; }
        bool TryStart();
        bool TrySetException(Exception exception);
        void CompleteAtCoroutineEnd();
    }

    /// <summary>
    /// Provides the writable side of a manually tracked Dispatcher operation.
    /// </summary>
    public sealed class DispatcherContext : IDispatcherContext
    {
        internal DispatcherContext(DispatchHandle handle)
        {
            Handle = handle;
        }

        /// <summary>The read-only lifecycle handle exposed to the caller.</summary>
        public DispatchHandle Handle { get; }

        public bool IsCompleted => Handle.IsCompleted;

        public void ReportProgress(float progress)
        {
            Handle.ReportProgress(progress);
        }

        public bool TrySetResult()
        {
            return Handle.TrySetSucceeded();
        }

        public bool TrySetException(Exception exception)
        {
            return Handle.TrySetException(exception);
        }

        DispatchHandle IDispatcherContext.UntypedHandle => Handle;

        bool IDispatcherContext.TryStart()
        {
            return Handle.TryStart();
        }

        void IDispatcherContext.CompleteAtCoroutineEnd()
        {
            if (!Handle.IsCompleted)
            {
                Handle.TrySetSucceeded();
            }
        }
    }

    /// <summary>
    /// Provides the writable side of a manually tracked Dispatcher operation that returns a value.
    /// </summary>
    public sealed class DispatcherContext<T> : IDispatcherContext
    {
        internal DispatcherContext(DispatchHandle<T> handle)
        {
            Handle = handle;
        }

        /// <summary>The read-only lifecycle handle exposed to the caller.</summary>
        public DispatchHandle<T> Handle { get; }

        public bool IsCompleted => Handle.IsCompleted;

        public void ReportProgress(float progress)
        {
            Handle.ReportProgress(progress);
        }

        public bool TrySetResult(T result)
        {
            return Handle.TrySetResult(result);
        }

        public bool TrySetException(Exception exception)
        {
            return Handle.TrySetException(exception);
        }

        DispatchHandle IDispatcherContext.UntypedHandle => Handle;

        bool IDispatcherContext.TryStart()
        {
            return Handle.TryStart();
        }

        void IDispatcherContext.CompleteAtCoroutineEnd()
        {
            if (!Handle.IsCompleted)
            {
                Handle.TrySetException(
                    new InvalidOperationException(
                        "Tracked coroutine completed without setting a result."));
            }
        }
    }
}
