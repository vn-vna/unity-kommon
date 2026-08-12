using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    internal interface ICommitOperation
    {
        void Execute(ItemDatabaseEngine engine);
        TaskCompletionSource<bool> Completion { get; }
    }

    internal class AddOperation : ICommitOperation
    {
        public InventoryItem Item;
        public TaskCompletionSource<bool> Completion { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute(ItemDatabaseEngine engine)
        {
            engine.ApplyAdd(Item);
            Completion.TrySetResult(true);
        }
    }

    internal class RemoveOperation : ICommitOperation
    {
        public string Key;
        public TaskCompletionSource<bool> Completion { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute(ItemDatabaseEngine engine)
        {
            engine.ApplyRemove(Key);
            Completion.TrySetResult(true);
        }
    }

    internal class SetTagOperation : ICommitOperation
    {
        public string Key;
        public Type TagDefType;    // TagDefinition type (e.g., typeof(StackableTag))
        public ITagData Data;      // The serializable ITagData instance
        public TaskCompletionSource<bool> Completion { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute(ItemDatabaseEngine engine)
        {
            engine.ApplySetTag(Key, TagDefType, Data);
            Completion.TrySetResult(true);
        }
    }

    internal class RemoveTagOperation : ICommitOperation
    {
        public string Key;
        public Type TagDefType;    // TagDefinition type
        public TaskCompletionSource<bool> Completion { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute(ItemDatabaseEngine engine)
        {
            engine.ApplyRemoveTag(Key, TagDefType);
            Completion.TrySetResult(true);
        }
    }

    /// <summary>
    /// Ordered write queue. All state mutations run on a single
    /// background processor thread; callers never block on writes.
    /// </summary>
    public class ItemDatabaseCommitQueue
    {
        private readonly ItemDatabaseEngine _engine;
        private readonly ConcurrentQueue<ICommitOperation> _queue
            = new ConcurrentQueue<ICommitOperation>();
        private readonly SemaphoreSlim _signal
            = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts
            = new CancellationTokenSource();
        private Task _processorTask;

        internal ItemDatabaseCommitQueue(ItemDatabaseEngine engine)
        {
            _engine = engine;
        }

        internal void Start()
        {
            _processorTask = ProcessLoopAsync(_cts.Token);
        }

        internal void Stop()
        {
            _cts.Cancel();
            _signal.Release();
        }

        internal Task EnqueueAsync(ICommitOperation op)
        {
            _queue.Enqueue(op);
            _signal.Release();
            return op.Completion.Task;
        }

        internal void Enqueue(ICommitOperation op)
        {
            _queue.Enqueue(op);
            _signal.Release();
        }

        private async Task ProcessLoopAsync(
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await _signal.WaitAsync(ct); }
                catch (OperationCanceledException) { break; }

                while (_queue.TryDequeue(out var op))
                {
                    try { op.Execute(_engine); }
                    catch (Exception ex)
                    {
                        op.Completion.TrySetException(ex);
                        QuickLog.Error<ItemDatabaseCommitQueue>(
                            "Commit operation failed: {0}", ex);
                    }
                }
            }
        }
    }
}
