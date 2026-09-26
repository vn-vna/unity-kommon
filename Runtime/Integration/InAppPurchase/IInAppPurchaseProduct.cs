namespace Com.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseProduct
    {
        string ProductId { get; }
        bool AllowRecover { get; }
    }
}