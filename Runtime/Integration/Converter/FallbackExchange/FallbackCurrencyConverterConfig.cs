using UnityEngine;

namespace Com.Scheherazade.Common.Integration.Converter
{
    public class FallbackCurrencyConverterConfig :
        ScriptableObject
    {
        public UsdConversionRate[] conversionRates;
    }

}