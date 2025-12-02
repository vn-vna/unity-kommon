using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    public class FallbackCurrencyConverterConfig :
        ScriptableObject
    {
        public UsdConversionRate[] conversionRates;
    }

}