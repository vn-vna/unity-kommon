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

        void Fail(Exception exception);

        Task<ItemOperationResult> CompletionTask { get; }
    }

    internal abstract class CommitOperation : ICommitOperation
    {
        private readonly TaskCompletionSource<ItemOperationResult> _completion
            = new TaskCompletionSource<ItemOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        protected CancellationToken CancellationToken { get; }

        public Task<ItemOperationResult> CompletionTask => _completion.Task;

        protected CommitOperation(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
        }

        public void Execute(ItemDatabaseEngine engine)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                _completion.TrySetResult(ItemOperationResult.Cancelled());
                return;
            }

            try
            {
                ItemOperationResult result = Apply(engine);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                _completion.TrySetResult(ItemOperationResult.Cancelled());
            }
            catch (ItemDatabaseException exception)
            {
                if (exception.ErrorCode == ItemDatabaseErrorCode.InternalFailure)
                {
                    Fail(exception);
                    return;
                }

                _completion.TrySetResult(ItemOperationResult.Rejected(
                    exception.ErrorCode,
                    exception.Message,
                    engine.MutationVersion
                ));
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public void Fail(Exception exception)
        {
            _completion.TrySetResult(ItemOperationResult.Failed(exception));
        }

        protected abstract ItemOperationResult Apply(ItemDatabaseEngine engine);

        protected void ThrowIfCancellationRequested()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        protected static void InvokePostCommitSafely(Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseCommitQueue>(
                    "Post-commit middleware failed after state was committed: {0}",
                    exception
                );
            }
        }
    }

    internal sealed class AddOperation : CommitOperation
    {
        internal AddItemRequest Request { get; }

        internal InventoryMiddlewarePipeline Pipeline { get; }

        internal AddOperation(
            AddItemRequest request,
            InventoryMiddlewarePipeline pipeline,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            Request = request;
            Pipeline = pipeline;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            InventoryItem item = Request.CreateInventoryItem(Request.RequestedKey);
            Pipeline?.BeforeAdd(item);
            ThrowIfCancellationRequested();
            ItemOperationResult result = engine.ApplyAdd(Request);
            if (result.Status == ItemOperationStatus.Succeeded)
            {
                foreach (ItemStackDelta delta in result.Deltas)
                {
                    InventoryItem? committedItem = engine.GetItem(delta.Key);
                    if (!committedItem.HasValue) continue;

                    InventoryItem value = committedItem.Value;
                    InvokePostCommitSafely(() => Pipeline?.AfterAdd(value));
                }
            }

            return result;
        }
    }

    internal sealed class RemoveOperation : CommitOperation
    {
        internal string Key { get; }

        internal InventoryMiddlewarePipeline Pipeline { get; }

        internal RemoveOperation(
            string key,
            InventoryMiddlewarePipeline pipeline,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            Key = key;
            Pipeline = pipeline;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            Pipeline?.BeforeRemove(Key);
            ThrowIfCancellationRequested();
            ItemOperationResult result = engine.ApplyRemove(Key);
            if (result.Status == ItemOperationStatus.Succeeded)
            {
                InvokePostCommitSafely(() => Pipeline?.AfterRemove(Key));
            }

            return result;
        }
    }

    internal sealed class SetTagOperation : CommitOperation
    {
        internal string Key { get; }

        internal Type TagDefinitionType { get; }

        internal CapturedTagData CapturedData { get; }

        internal InventoryMiddlewarePipeline Pipeline { get; }

        internal SetTagOperation(
            string key,
            Type tagDefinitionType,
            CapturedTagData capturedData,
            InventoryMiddlewarePipeline pipeline,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            Key = key;
            TagDefinitionType = tagDefinitionType;
            CapturedData = capturedData;
            Pipeline = pipeline;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            ITagData beforeData = ItemTagDataSerializer.Deserialize(
                CapturedData.Json,
                CapturedData.DataType
            );
            Pipeline?.BeforeSetTag(Key, TagDefinitionType, beforeData);
            ThrowIfCancellationRequested();
            CapturedTagData transformedData = ItemTagDataSerializer.Capture(beforeData);
            if (transformedData.TagDefinitionType != TagDefinitionType)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidTagData,
                    "Middleware changed the tag payload mapping.",
                    engine.MutationVersion
                );
            }

            ItemOperationResult result = engine.ApplySetTag(
                Key,
                TagDefinitionType,
                transformedData
            );
            if (result.Status == ItemOperationStatus.Succeeded)
            {
                InvokePostCommitSafely(() => Pipeline?.AfterSetTag(
                    Key,
                    TagDefinitionType,
                    ItemTagDataSerializer.Deserialize(
                        transformedData.Json,
                        transformedData.DataType
                    )
                ));
            }

            return result;
        }
    }

    internal sealed class RemoveTagOperation : CommitOperation
    {
        internal string Key { get; }

        internal Type TagDefinitionType { get; }

        internal RemoveTagOperation(
            string key,
            Type tagDefinitionType,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            Key = key;
            TagDefinitionType = tagDefinitionType;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            return engine.ApplyRemoveTag(Key, TagDefinitionType);
        }
    }

    internal sealed class RemoveQuantityOperation : CommitOperation
    {
        internal string Key { get; }

        internal int Quantity { get; }

        internal RemoveQuantityOperation(
            string key,
            int quantity,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            Key = key;
            Quantity = quantity;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            return engine.ApplyRemoveQuantity(Key, Quantity);
        }
    }

    internal sealed class ExpireItemsOperation : CommitOperation
    {
        internal DateTime NowUtc { get; }

        internal ExpireItemsOperation(
            DateTime nowUtc,
            CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            NowUtc = nowUtc;
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            return engine.ApplyExpire(NowUtc);
        }
    }

    internal sealed class BarrierOperation : CommitOperation
    {
        internal BarrierOperation(CancellationToken cancellationToken)
            : base(cancellationToken)
        {
        }

        protected override ItemOperationResult Apply(ItemDatabaseEngine engine)
        {
            return ItemOperationResult.NoChange(engine.MutationVersion);
        }
    }

    /// <summary>
    /// Ordered write queue. All mutations and middleware hooks execute in
    /// enqueue order on one asynchronous processor.
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
        private readonly object _enqueueLock = new object();

        private Task _processorTask;
        private Task<ItemOperationResult> _terminalBarrierTask;
        private bool _started;
        private bool _accepting = true;

        internal ItemDatabaseCommitQueue(ItemDatabaseEngine engine)
        {
            _engine = engine;
        }

        internal void Start()
        {
            if (_started) return;
            _started = true;
            _processorTask = ProcessLoopAsync(_cts.Token);
        }

        internal void Stop()
        {
            _ = StopAsync();
        }

        internal Task<ItemOperationResult> EnqueueAsync(ICommitOperation operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            lock (_enqueueLock)
            {
                if (!_accepting)
                {
                    return Task.FromResult(ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ModuleUnavailable,
                        "Item Database is stopping and no longer accepts commands."
                    ));
                }

                EnsureStarted();
                _queue.Enqueue(operation);
                _signal.Release();
                return operation.CompletionTask;
            }
        }

        internal Task<ItemOperationResult> DrainAsync(
            CancellationToken cancellationToken = default)
        {
            return EnqueueAsync(new BarrierOperation(cancellationToken));
        }

        internal void StopAccepting()
        {
            lock (_enqueueLock)
            {
                _accepting = false;
            }
        }

        internal Task<ItemOperationResult> SealAndDrainAsync()
        {
            lock (_enqueueLock)
            {
                if (_terminalBarrierTask != null) return _terminalBarrierTask;

                _accepting = false;
                if (!_started)
                {
                    _terminalBarrierTask = Task.FromResult(
                        ItemOperationResult.NoChange(_engine.MutationVersion)
                    );
                    return _terminalBarrierTask;
                }

                var barrier = new BarrierOperation(CancellationToken.None);
                _queue.Enqueue(barrier);
                _signal.Release();
                _terminalBarrierTask = barrier.CompletionTask;
                return _terminalBarrierTask;
            }
        }

        internal async Task StopAsync()
        {
            ItemOperationResult barrier = await SealAndDrainAsync();
            barrier.ThrowIfRejected();
            if (!_started) return;

            _cts.Cancel();
            _signal.Release();
            if (_processorTask != null) await _processorTask;
        }

        private async Task ProcessLoopAsync(
            CancellationToken ct)
        {
            while (true)
            {
                try
                {
                    await _signal.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    if (_queue.IsEmpty) break;
                }

                while (_queue.TryDequeue(out ICommitOperation operation))
                {
                    try
                    {
                        operation.Execute(_engine);
                    }
                    catch (Exception exception)
                    {
                        operation.Fail(exception);
                        QuickLog.Error<ItemDatabaseCommitQueue>(
                            "Commit operation failed: {0}",
                            exception
                        );
                    }
                }

                if (ct.IsCancellationRequested && _queue.IsEmpty) break;
            }
        }

        private void EnsureStarted()
        {
            if (!_started)
            {
                Start();
            }
        }
    }
}
