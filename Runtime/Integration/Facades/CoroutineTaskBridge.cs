using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Threading;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    /// <summary>
    /// Bridges coroutine-based manager APIs to Task-based APIs so facades
    /// can expose async, coroutine and fire-and-forget flavors from a single
    /// coroutine implementation.
    /// </summary>
    internal static class CoroutineTaskBridge
    {
        /// <summary>
        /// Runs the coroutine on the Dispatcher and completes when it finishes.
        /// Exceptions thrown by the coroutine body are captured into the returned task.
        /// </summary>
        public static Task<bool> RunAsync(IEnumerator coroutine)
        {
            if (coroutine == null)
            {
                return Task.FromResult(false);
            }

            if (Dispatcher.Instance == null)
            {
                return Task.FromException<bool>(
                    new InvalidOperationException(
                        "No Dispatcher instance found. Coroutine cannot be dispatched."
                    )
                );
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Dispatcher.DispatchCoroutine(RunInternal(coroutine, tcs));
            return tcs.Task;
        }

        /// <summary>
        /// Runs a callback-driven coroutine factory and returns its completion result.
        /// Supports an optional timeout and cancellation token. The strict flavor:
        /// missing manager or operation failure surfaces as exceptions.
        /// </summary>
        public static async Task<T> RunWithCallbackAsync<T>(
            Func<Action<T>, IEnumerator> coroutineFactory,
            float timeoutSeconds = 0f,
            CancellationToken ct = default
        )
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            if (ct.CanBeCanceled)
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
            }

            Task<bool> runner = RunAsync(coroutineFactory(result => tcs.TrySetResult(result)));
            Task timeoutTask = timeoutSeconds > 0f
                ? Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))
                : null;

            Task[] awaited = timeoutTask == null
                ? new[] { runner }
                : new[] { runner, timeoutTask };

            Task finished = await Task.WhenAny(awaited).ConfigureAwait(false);

            if (finished == timeoutTask)
            {
                throw new TimeoutException(
                    $"Operation timed out after {timeoutSeconds:F0} seconds."
                );
            }

            await runner.ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        private static IEnumerator RunInternal(IEnumerator coroutine, TaskCompletionSource<bool> tcs)
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = coroutine.MoveNext();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return coroutine.Current;
            }

            tcs.TrySetResult(true);
        }
    }
}
