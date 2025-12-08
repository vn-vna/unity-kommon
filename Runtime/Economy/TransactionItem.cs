using System;

using Com.Hapiga.FallAway.Economy;
using Com.Hapiga.FallAway.Inventory;
using Com.Hapiga.Schehrazade.IS;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    [Serializable]
    public class TransactionItem
    {
        public int Count
        {
            get
            {
                if (!overrideProvider) return count;
                return overrideProvider.ItemCount ?? count;
            }
        }

        public ItemDefinition ItemDefinition => item;

        public DateTime? ExpiryDate
        {
            get
            {
                if (expirationMode != ExpirationMode.AtDate) return null;
                if (overrideProvider)
                {
                    string overrideDate = overrideProvider.ExpiryDate;
                    if (!string.IsNullOrEmpty(overrideDate))
                    {
                        return DateTime.Parse(overrideDate);
                    }
                }
                return DateTime.Parse(expiryDate);
            }
        }
        public TimeSpan? ExpiryDuration
        {
            get
            {
                if (expirationMode != ExpirationMode.AfterDuration) return null;
                if (overrideProvider)
                {
                    float? overrideDuration = overrideProvider.ExpiryDuration;
                    if (overrideDuration.HasValue)
                    {
                        return TimeSpan.FromSeconds(overrideDuration.Value);
                    }
                }
                return TimeSpan.FromSeconds(expiryDuration);
            }
        }
        public ExpirationMode ExpiryMode => expirationMode;

        [SerializeField]
        private ItemDefinition item;

        [SerializeField]
        private int count;

        [SerializeField]
        private ExpirationMode expirationMode;

        [SerializeField]
        private string expiryDate;

        [SerializeField]
        private float expiryDuration;

        [SerializeField]
        private TransactionItemOverrideProvider overrideProvider;
    }

    public enum ExpirationMode
    {
        NoExpiration,
        AtDate,
        AfterDuration
    }
}