using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    public enum ItemDatabaseLifecycleState
    {
        Uninitialized,
        Disabled,
        Initializing,
        Ready,
        Failed,
        Stopping,
        Stopped
    }

    public sealed class ItemDatabaseInitializationResult
    {
        public ItemDatabaseLifecycleState State { get; }

        public Exception Exception { get; }

        public bool IsReady => State == ItemDatabaseLifecycleState.Ready;

        internal ItemDatabaseInitializationResult(
            ItemDatabaseLifecycleState state,
            Exception exception = null)
        {
            State = state;
            Exception = exception;
        }
    }
}
