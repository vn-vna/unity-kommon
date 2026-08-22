using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    public enum ItemOperationStatus
    {
        Succeeded,
        NoChange,
        Disabled,
        Rejected,
        Cancelled,
        Failed
    }

    public enum ItemDatabaseErrorCode
    {
        None,
        ModuleDisabled,
        ModuleUnavailable,
        InvalidItemKey,
        InvalidItemId,
        DefinitionNotFound,
        DuplicateItemKey,
        ItemNotFound,
        TagNotAllowed,
        InvalidTagData,
        InvalidQuantity,
        InvalidStackCapacity,
        ImmutableStackIdentity,
        InsufficientQuantity,
        Cancelled,
        InternalFailure
    }

    public readonly struct ItemStackDelta
    {
        public string Key { get; }

        public int PreviousQuantity { get; }

        public int CurrentQuantity { get; }

        public bool WasCreated { get; }

        public bool WasRemoved => PreviousQuantity > 0 && CurrentQuantity == 0;

        public ItemStackDelta(
            string key,
            int previousQuantity,
            int currentQuantity,
            bool wasCreated)
        {
            Key = key;
            PreviousQuantity = previousQuantity;
            CurrentQuantity = currentQuantity;
            WasCreated = wasCreated;
        }
    }

    public sealed class ItemOperationResult
    {
        private static readonly ItemStackDelta[] EmptyDeltas = Array.Empty<ItemStackDelta>();

        public ItemOperationStatus Status { get; }

        public ItemDatabaseErrorCode ErrorCode { get; }

        public string Message { get; }

        public long CommittedVersion { get; }

        public IReadOnlyList<ItemStackDelta> Deltas { get; }

        public bool Succeeded => Status == ItemOperationStatus.Succeeded
            || Status == ItemOperationStatus.NoChange;

        private ItemOperationResult(
            ItemOperationStatus status,
            ItemDatabaseErrorCode errorCode,
            string message,
            long committedVersion,
            ItemStackDelta[] deltas)
        {
            Status = status;
            ErrorCode = errorCode;
            Message = message;
            CommittedVersion = committedVersion;
            Deltas = deltas ?? EmptyDeltas;
        }

        internal static ItemOperationResult Success(
            long committedVersion,
            ItemStackDelta[] deltas = null,
            string message = null)
        {
            return new ItemOperationResult(
                ItemOperationStatus.Succeeded,
                ItemDatabaseErrorCode.None,
                message,
                committedVersion,
                deltas
            );
        }

        internal static ItemOperationResult NoChange(
            long committedVersion,
            string message = null)
        {
            return new ItemOperationResult(
                ItemOperationStatus.NoChange,
                ItemDatabaseErrorCode.None,
                message,
                committedVersion,
                null
            );
        }

        internal static ItemOperationResult Disabled()
        {
            return new ItemOperationResult(
                ItemOperationStatus.Disabled,
                ItemDatabaseErrorCode.ModuleDisabled,
                "Item Database is disabled.",
                0,
                null
            );
        }

        internal static ItemOperationResult Unavailable(
            string message = "Item Database is not available in its current lifecycle state.")
        {
            return new ItemOperationResult(
                ItemOperationStatus.Rejected,
                ItemDatabaseErrorCode.ModuleUnavailable,
                message,
                0,
                null
            );
        }

        internal static ItemOperationResult Rejected(
            ItemDatabaseErrorCode errorCode,
            string message,
            long committedVersion = 0)
        {
            return new ItemOperationResult(
                ItemOperationStatus.Rejected,
                errorCode,
                message,
                committedVersion,
                null
            );
        }

        internal static ItemOperationResult Cancelled()
        {
            return new ItemOperationResult(
                ItemOperationStatus.Cancelled,
                ItemDatabaseErrorCode.Cancelled,
                "The operation was cancelled before commit.",
                0,
                null
            );
        }

        internal static ItemOperationResult Failed(Exception exception)
        {
            return new ItemOperationResult(
                ItemOperationStatus.Failed,
                ItemDatabaseErrorCode.InternalFailure,
                exception?.Message ?? "The operation failed.",
                0,
                null
            );
        }

        internal void ThrowIfRejected()
        {
            if (Succeeded || Status == ItemOperationStatus.Disabled) return;

            if (Status == ItemOperationStatus.Cancelled)
            {
                throw new OperationCanceledException(Message);
            }

            throw new ItemDatabaseException(
                $"{ErrorCode}: {Message}"
            );
        }
    }
}
