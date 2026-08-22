using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    internal sealed class ItemDatabaseExpiryScheduler
    {
        private readonly ItemDatabaseEngine _engine;
        private readonly Func<CancellationToken, Task<ItemOperationResult>> _expireAsync;
        private readonly CancellationTokenSource _lifetimeSource
            = new CancellationTokenSource();
        private readonly object _scheduleLock = new object();

        private CancellationTokenSource _scheduleSource;
        private Task _scheduledTask;
        private bool _started;

        internal ItemDatabaseExpiryScheduler(
            ItemDatabaseEngine engine,
            Func<CancellationToken, Task<ItemOperationResult>> expireAsync)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _expireAsync = expireAsync ?? throw new ArgumentNullException(nameof(expireAsync));
        }

        internal void Start()
        {
            if (_started) return;

            _started = true;
            _engine.MutationCommitted += HandleMutationCommitted;
            Reschedule();
        }

        internal void Stop()
        {
            if (!_started) return;

            _started = false;
            _engine.MutationCommitted -= HandleMutationCommitted;
            _lifetimeSource.Cancel();
            lock (_scheduleLock)
            {
                _scheduleSource?.Cancel();
            }
        }

        internal Task<ItemOperationResult> ExpireNowAsync(
            CancellationToken cancellationToken = default)
        {
            return _expireAsync(cancellationToken);
        }

        private void HandleMutationCommitted(long version)
        {
            Reschedule();
        }

        private void Reschedule()
        {
            lock (_scheduleLock)
            {
                if (!_started) return;

                _scheduleSource?.Cancel();
                _scheduleSource?.Dispose();
                _scheduleSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeSource.Token
                );

                DateTime? nextExpiryUtc = _engine.GetNextExpiryUtc();
                _scheduledTask = nextExpiryUtc.HasValue
                    ? WaitAndExpireAsync(nextExpiryUtc.Value, _scheduleSource.Token)
                    : Task.CompletedTask;
            }
        }

        private async Task WaitAndExpireAsync(
            DateTime expiryUtc,
            CancellationToken cancellationToken)
        {
            try
            {
                TimeSpan delay = expiryUtc - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    TimeSpan maximumDelay = TimeSpan.FromMilliseconds(int.MaxValue - 1);
                    await Task.Delay(
                        delay < maximumDelay ? delay : maximumDelay,
                        cancellationToken
                    );
                }

                if (cancellationToken.IsCancellationRequested) return;
                await _expireAsync(cancellationToken);
                Reschedule();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseExpiryScheduler>(
                    "Scheduled expiry failed: {0}",
                    exception
                );
                Reschedule();
            }
        }
    }
}
