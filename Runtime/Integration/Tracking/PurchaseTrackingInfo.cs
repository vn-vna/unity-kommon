using System;
using Newtonsoft.Json;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct PurchaseTrackingInfo
    {
        public string OrderId { get; set; }
        public string ProductId { get; set; }
        public double Price { get; set; }
        public double Value { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public string FailureReason { get; set; }
        public string ReceiptRaw { get; set; }

        public Receipt Receipt
        {
            get
            {
                return JsonConvert.DeserializeObject<Receipt>(ReceiptRaw);
            }
        }
    }

    [Serializable]
    public struct Receipt
    {
        public string Store;
        public string TransactionID;
        public string Payload;

        public PayloadAndroid PayloadAndroid
        {
            get
            {
                return JsonConvert.DeserializeObject<PayloadAndroid>(Payload);
            }
        }
    }

    [Serializable]
    public struct PayloadAndroid
    {
        public string Json;
        public string Signature;
    }
}