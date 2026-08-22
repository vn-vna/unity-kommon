using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Base exception for the Item Database framework.
    /// </summary>
    public class ItemDatabaseException : Exception
    {
        public ItemDatabaseErrorCode ErrorCode { get; }

        public ItemDatabaseException(string message)
            : this(ItemDatabaseErrorCode.InternalFailure, message)
        {
        }

        public ItemDatabaseException(string message, Exception inner)
            : this(ItemDatabaseErrorCode.InternalFailure, message, inner)
        {
        }

        public ItemDatabaseException(
            ItemDatabaseErrorCode errorCode,
            string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public ItemDatabaseException(
            ItemDatabaseErrorCode errorCode,
            string message,
            Exception inner)
            : base(message, inner)
        {
            ErrorCode = errorCode;
        }
    }
}
