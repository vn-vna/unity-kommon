using UnityEngine;

namespace Com.Scheherazade.Economy
{
    public abstract class TransactionItemListProvider :
        ScriptableObject
    {
        public abstract TransactionItem[] Items { get; }
    }
}