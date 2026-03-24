using System;
using UnityEngine;

[Serializable]
public class IAPReceiptWrapper
{
    public string Store;
    public string Payload;
}

[Serializable]
public class IAPGooglePayload
{
    public string json;
    public string signature;
}

[Serializable]
public class IAPGooglePurchaseData
{
    public string orderId;
    public string packageName;
    public string productId;
    public long purchaseTime;
    public int purchaseState;
    public string purchaseToken;
}

public static class IAPUtils
{
    public static string ExtractOrderId(string receipt, string transactionId)
    {
        if (string.IsNullOrEmpty(receipt))
            return SafeFallback(transactionId);

        try
        {
            var wrapper = JsonUtility.FromJson<IAPReceiptWrapper>(receipt);

            if (wrapper == null)
                return SafeFallback(transactionId);

            if (wrapper.Store == "GooglePlay")
            {
                var payload = JsonUtility.FromJson<IAPGooglePayload>(wrapper.Payload);

                if (payload == null || string.IsNullOrEmpty(payload.json))
                    return SafeFallback(transactionId);

                var data = JsonUtility.FromJson<IAPGooglePurchaseData>(payload.json);

                if (data == null)
                    return SafeFallback(transactionId);

                if (!string.IsNullOrEmpty(data.orderId))
                    return data.orderId;

                return SafeFallback(transactionId);
            }

            if (wrapper.Store == "AppleAppStore")
                return SafeFallback(transactionId);
        }
        catch
        {
        }

        return SafeFallback(transactionId);
    }

    private static string SafeFallback(string transactionId)
    {
        if (!string.IsNullOrEmpty(transactionId))
            return transactionId;

        return Guid.NewGuid().ToString();
    }
}