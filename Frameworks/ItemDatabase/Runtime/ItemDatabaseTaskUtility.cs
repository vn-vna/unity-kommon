using System;
using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    internal static class ItemDatabaseTaskUtility
    {
        internal static async Task WaitAsync(
            Task task,
            CancellationToken cancellationToken)
        {
            if (task == null) return;
            if (!cancellationToken.CanBeCanceled)
            {
                await task;
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var cancellationSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using (cancellationToken.Register(
                       () => cancellationSource.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(
                    task,
                    cancellationSource.Task
                );
                if (completed != task)
                {
                    ObserveAbandonedTask(task);
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            await task;
        }

        internal static async Task<T> WaitAsync<T>(
            Task<T> task,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await task;

            cancellationToken.ThrowIfCancellationRequested();
            var cancellationSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using (cancellationToken.Register(
                       () => cancellationSource.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(
                    task,
                    cancellationSource.Task
                );
                if (completed != task)
                {
                    ObserveAbandonedTask(task);
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task;
        }

        private static void ObserveAbandonedTask(Task task)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }
}
