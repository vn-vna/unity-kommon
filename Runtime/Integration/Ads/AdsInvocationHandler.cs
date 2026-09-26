using System;

namespace Com.Scheherazade.Common.Integration.Ads
{
    public enum AdsInvocationStatus
    {
        Pending,
        Showing,
        Succeeded,
        Failed,
        Skipped,
        Cancelled
    }

    /// <summary>Mutable only through monotonic provider transition methods.</summary>
    public sealed class AdsInvocationHandler
    {
        private Action<AdsInvocationHandler> _observers;

        public AdsType AdType { get; }
        public string Placement { get; }
        public AdsInvocationStatus Status { get; private set; } = AdsInvocationStatus.Pending;
        public bool IsTerminal => IsTerminalStatus(Status);
        public bool WasDisplayed { get; private set; }
        public bool WasClosed { get; private set; }
        public bool RewardEarned { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        public AdsInvocationHandler(AdsType adType, string placement = "")
        {
            AdType = adType;
            Placement = placement ?? string.Empty;
        }

        /// <summary>
        /// Observes the current state immediately by default, then every later transition.
        /// Terminal handlers do not retain the observer.
        /// </summary>
        public IDisposable Observe(Action<AdsInvocationHandler> observer, bool notifyImmediately = true)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (!IsTerminal) _observers += observer;
            if (notifyImmediately) InvokeSafely(observer);
            return IsTerminal ? EmptySubscription.Instance : new Subscription(this, observer);
        }

        public bool MarkShowing()
        {
            if (IsTerminal) return false;
            WasDisplayed = true;
            Status = AdsInvocationStatus.Showing;
            NotifyObservers();
            return !IsTerminal;
        }

        public bool MarkRewardEarned()
        {
            if (IsTerminal || RewardEarned) return false;
            RewardEarned = true;
            NotifyObservers();
            return !IsTerminal;
        }

        public bool MarkClosed()
        {
            if (IsTerminal || WasClosed) return false;
            WasClosed = true;
            NotifyObservers();
            return !IsTerminal;
        }

        public bool CompleteSucceeded(string reason = "", bool wasClosed = false) =>
            Complete(AdsInvocationStatus.Succeeded, reason, wasClosed);

        public bool CompleteFailed(string reason, bool wasClosed = false) =>
            Complete(AdsInvocationStatus.Failed, reason, wasClosed);

        public bool CompleteSkipped(string reason = "") =>
            Complete(AdsInvocationStatus.Skipped, reason, false);

        public bool CompleteCancelled(string reason, bool wasClosed = false) =>
            Complete(AdsInvocationStatus.Cancelled, reason, wasClosed);

        public static AdsInvocationHandler Failed(AdsType type, string placement, string reason)
        {
            var handler = new AdsInvocationHandler(type, placement);
            handler.CompleteFailed(reason);
            return handler;
        }

        public static AdsInvocationHandler Skipped(AdsType type, string placement, string reason)
        {
            var handler = new AdsInvocationHandler(type, placement);
            handler.CompleteSkipped(reason);
            return handler;
        }

        private bool Complete(AdsInvocationStatus status, string reason, bool wasClosed)
        {
            if (IsTerminal || !IsTerminalStatus(status)) return false;
            WasClosed |= wasClosed;
            Reason = reason ?? string.Empty;
            Status = status;
            NotifyObservers();
            _observers = null;
            return true;
        }

        private void NotifyObservers()
        {
            Action<AdsInvocationHandler> observers = _observers;
            if (observers == null) return;
            foreach (Action<AdsInvocationHandler> observer in observers.GetInvocationList())
                InvokeSafely(observer);
        }

        private void InvokeSafely(Action<AdsInvocationHandler> observer)
        {
            try { observer(this); }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
        }

        private void Unsubscribe(Action<AdsInvocationHandler> observer) => _observers -= observer;

        private static bool IsTerminalStatus(AdsInvocationStatus status) =>
            status == AdsInvocationStatus.Succeeded || status == AdsInvocationStatus.Failed ||
            status == AdsInvocationStatus.Skipped || status == AdsInvocationStatus.Cancelled;

        private sealed class Subscription : IDisposable
        {
            private AdsInvocationHandler _owner;
            private Action<AdsInvocationHandler> _observer;

            internal Subscription(AdsInvocationHandler owner, Action<AdsInvocationHandler> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose()
            {
                AdsInvocationHandler owner = _owner;
                Action<AdsInvocationHandler> observer = _observer;
                _owner = null;
                _observer = null;
                owner?.Unsubscribe(observer);
            }
        }

        private sealed class EmptySubscription : IDisposable
        {
            internal static readonly EmptySubscription Instance = new EmptySubscription();
            public void Dispose() { }
        }
    }
}
