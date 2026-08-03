using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    /// <summary>
    /// Static facade over the registered <see cref="ICurrencyConverter"/>.
    /// Conversion is synchronous; the facade adds availability queries and
    /// initialization flavors.
    /// </summary>
    public class Currency
    {
        #region Queries

        public static ICurrencyConverter Manager => Integration.CurrencyConverter;

        public static bool IsAvailable => Manager != null;

        public static CurrencyConverterStatus Status =>
            Manager != null ? Manager.Status : CurrencyConverterStatus.NotInitialized;

        public static bool IsInitialized => Status == CurrencyConverterStatus.Initialized;

        /// <summary>
        /// Converts <paramref name="amount"/> from <paramref name="from"/> to
        /// <paramref name="to"/> (ISO 4217 codes, case-insensitive).
        /// Returns null when the converter is unavailable or cannot convert.
        /// </summary>
        public static decimal? Convert(string from, string to, decimal amount)
        {
            return Manager != null ? Manager.Convert(from, to, amount) : null;
        }

        #endregion

        #region Initialization

        public static void Initialize()
        {
            if (!TryGetManager(out ICurrencyConverter manager))
            {
                return;
            }

            manager.Initialize();
        }

        public static IEnumerator InitializeCoroutine()
        {
            if (!TryGetManager(out ICurrencyConverter manager))
            {
                yield break;
            }

            IEnumerator steps = manager.InitializeCoroutine();
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task InitializeAsync(CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine());
        }

        #endregion

        #region Private Methods

        private static bool TryGetManager(out ICurrencyConverter manager)
        {
            manager = Integration.CurrencyConverter;
            if (manager == null)
            {
                QuickLog.Warning<Currency>(
                    "Currency converter is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static ICurrencyConverter RequireManager()
        {
            return Integration.RequireManager<ICurrencyConverter>();
        }

        #endregion
    }
}
