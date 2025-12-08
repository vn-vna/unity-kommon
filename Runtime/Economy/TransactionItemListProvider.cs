using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    public abstract class TransactionItemListProvider :
        ScriptableObject
    {
        public abstract TransactionItem[] Items { get; }
    }
}