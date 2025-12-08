using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Economy
{
    public struct InAppPurchaseResult
    {
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public IEnumerable<InAppPurchasePack> Packs { get; set; }
        public IEnumerable<InAppPurchasePack> FailedPacks { get; set; }
    }
}