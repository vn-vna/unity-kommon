using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    public class FallbackCurrencyConverterModule :
        ICurrencyConverterModule
    {
        public bool IsInitialized => true;

        public int Priority => int.MinValue;

        private FallbackCurrencyConverterConfig _config;
        private Dictionary<string, double> _fromUsdRate;

        public FallbackCurrencyConverterModule(FallbackCurrencyConverterConfig config)
        {
            _config = config;
        }

        public void Initialize()
        {
            _fromUsdRate = new Dictionary<string, double>();
            foreach (var rate in _config.conversionRates)
            {
                _fromUsdRate[rate.isoCode] = rate.fromUsdRate;
            }
        }

        public double? ConvertToUsd(string currencyCode, double amount)
        {
            if (_fromUsdRate.TryGetValue(currencyCode, out var rate))
            {
                return amount / rate;
            }
            return null;
        }

        public double? ConvertFromUsd(string currencyCode, double amount)
        {
            if (_fromUsdRate.TryGetValue(currencyCode, out var rate))
            {
                return amount * rate;
            }
            return null;
        }
    }

}