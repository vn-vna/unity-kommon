using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    public class GithubFawazahmed0ExchangeApiConverterModule :
        ICurrencyConverterModule
    {
        const string URL = "https://latest.currency-api.pages.dev/v1/currencies/usd.json";

        public bool IsInitialized { get; private set; }
        public int Priority => 0;

        private Dictionary<string, double> _fromUsdRate;

        public void Initialize()
        {
            IsInitialized = false;
            InitializeInternalAsync();
        }

        private async Task InitializeInternalAsync()
        {
            int attempts = 0;
            while (!IsInitialized && attempts < 3)
            {
                await FetchRatesAsync();
                attempts++;
            }
        }

        private async Task FetchRatesAsync()
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(URL))
            {
                var operation = webRequest.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    QuickLog.Error<GithubFawazahmed0ExchangeApiConverterModule>(
                        "Failed to fetch currency rates: {0}",
                        webRequest.error
                    );
                    return;
                }

                var json = webRequest.downloadHandler.text;
                var response = JsonConvert.DeserializeObject<Fawazahmed0ExchangeApiJsonResponse>(json);
                _fromUsdRate = response.usd;
                IsInitialized = true;
            }
        }

        public double? ConvertToUsd(string currencyCode, double amount)
        {
            if (
                _fromUsdRate != null &&
                _fromUsdRate.TryGetValue(currencyCode, out var rate)
            )
            {
                return amount / rate;
            }
            return null;
        }

        public double? ConvertFromUsd(string currencyCode, double amount)
        {
            if (
                _fromUsdRate != null &&
                _fromUsdRate.TryGetValue(currencyCode, out var rate)
            )
            {
                return amount * rate;
            }
            return null;
        }
    }

}