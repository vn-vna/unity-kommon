#if PLA_CREATIVE_BUILD
using System;
using System.Threading.Tasks;

namespace Com.Scheherazade.Common.Threading
{
    /// <summary>
    /// Task-backed completion signals for creative builds. Owners still complete,
    /// cancel or fault the real operation; these sources never invent completion.
    /// Default TaskCompletionSource continuation behavior matches inline owner completion.
    /// </summary>
    public class CreativeCompletionSource<T>
    {
        private readonly TaskCompletionSource<T> _source = new TaskCompletionSource<T>();
        public Task<T> Awaitable => _source.Task;
        public void SetResult(T result) => _source.SetResult(result);
        public bool TrySetResult(T result) => _source.TrySetResult(result);
        public void SetException(Exception exception) => _source.SetException(exception);
        public bool TrySetException(Exception exception) => _source.TrySetException(exception);
        public void SetCanceled() => _source.SetCanceled();
        public bool TrySetCanceled() => _source.TrySetCanceled();
    }

    public sealed class CreativeCompletionSource : CreativeCompletionSource<bool>
    {
        public new Task Awaitable => base.Awaitable;
        public void SetResult() => base.SetResult(true);
        public bool TrySetResult() => base.TrySetResult(true);
    }
}
#endif
