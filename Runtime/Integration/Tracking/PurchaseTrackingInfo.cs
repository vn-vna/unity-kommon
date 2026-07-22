using System;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
#endif

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct PurchaseTrackingInfo : ITrackingData
    {
        public string TransactionId { get; set; }
        public string ProductId { get; set; }
        public double Price { get; set; }
        public string Currency { get; set; }
        public string ReceiptRaw { get; set; }

        public Receipt Receipt
        {
            get
            {
#if NEWTONSOFT_JSON
                return JsonConvert.DeserializeObject<Receipt>(ReceiptRaw);
#else   
                throw new Exception("Newtonsoft JSON package is required");
#endif
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
#if NEWTONSOFT_JSON
                return JsonConvert.DeserializeObject<PayloadAndroid>(Payload);
#else   
                throw new Exception("Newtonsoft JSON package is required");
#endif
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