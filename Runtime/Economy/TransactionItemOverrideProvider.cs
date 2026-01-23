using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    public abstract class TransactionItemOverrideProvider :    
        ScriptableObject
    {
        public virtual int? ItemCount => null;
        public virtual ExpirationMode? ExpiryMode => null;
        public virtual string ExpiryDate => null;
        public virtual float? ExpiryDuration => null;
    }
}