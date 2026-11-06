using System.Collections.Generic;

namespace Com.Scheherazade.Economy
{
    public struct InAppPurchaseResult
    {
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public IEnumerable<InAppPurchasePack> Packs { get; set; }
        public IEnumerable<InAppPurchasePack> FailedPacks { get; set; }
    }
}