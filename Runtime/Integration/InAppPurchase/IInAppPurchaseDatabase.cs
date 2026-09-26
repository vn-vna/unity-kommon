using System.Collections.Generic;

namespace Com.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseDatabase
    {
        public IEnumerable<IInAppPurchaseProduct> Products { get; }
    }
}