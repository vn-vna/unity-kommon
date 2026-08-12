using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Base exception for the Item Database framework.
    /// </summary>
    public class ItemDatabaseException : Exception
    {
        public ItemDatabaseException(string message)
            : base(message)
        {
        }

        public ItemDatabaseException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
