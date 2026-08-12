using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Items with this tag expire after a certain date.
    /// Runtime data: ExpirableData { expiresAt }.
    /// When paired with StackableTag, items with the same expiry
    /// are auto-merged by AutoStackMiddleware.
    /// ExpirableItemMiddleware rejects expired batches and sweeps them.
    /// </summary>
    public class ExpirableTag : TagDefinition
    {
    }

    /// <summary>
    /// Expirable data: tracks the expiry date for this instance.
    /// Linked to ExpirableTag via [TagData].
    /// Used by ExpiryMiddleware and IngameCurrency.
    /// </summary>
    [Serializable]
    [TagData(typeof(ExpirableTag))]
    public class ExpirableData : ITagData
    {
        public DateTime expiresAt;

        /// <summary>True when the expiry date has passed.</summary>
        public bool IsExpired => expiresAt <= DateTime.UtcNow;
    }
}
