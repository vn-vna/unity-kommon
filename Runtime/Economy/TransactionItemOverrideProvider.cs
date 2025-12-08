using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    public abstract class TransactionItemOverrideProvider :    
        ScriptableObject
    {
        public abstract int? ItemCount { get; }
        public abstract string ExpiryDate { get; }
        public abstract float? ExpiryDuration { get; }
    }
}