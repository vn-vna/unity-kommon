namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public enum PurchaseStatus
    {
        Pending,
        Confirmed,
        Failed,
        Canceled,
        Deferred,
        Unavailable,
        Busy
    }

    /// <summary>Optional provider diagnostics for manager/UI outcome mapping.</summary>
    public interface IInAppPurchaseProviderDiagnostics
    {
        bool IsInitializing { get; }
        string UnavailableReason { get; }
        PurchaseStatus LastPurchaseStatus { get; }
    }
}
