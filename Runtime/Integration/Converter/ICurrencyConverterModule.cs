namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    public interface ICurrencyConverterModule
    {
        bool IsInitialized { get; }
        int Priority { get; }

        void Initialize();

        double? ConvertToUsd(string currencyCode, double amount);
        double? ConvertFromUsd(string currencyCode, double amount);
    }

} 