using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{

    public abstract class CurrencyConverterBase<T> :
        SingletonBehavior<T>,
        ICurrencyConverter
        where T : CurrencyConverterBase<T>
    {
        public CurrencyConverterStatus Status { get; private set; }

        private List<ICurrencyConverterModule> _modules;

        public CurrencyConverterBase()
        {
            _modules = new List<ICurrencyConverterModule>();
        }

        protected override void Awake()
        {
            base.Awake();
            Integration.RegisterManager(this);
            Status = CurrencyConverterStatus.NotInitialized;
        }

        public void RegisterModule(ICurrencyConverterModule module)
        {
            _modules.Add(module);
            _modules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public void Initialize()
        {
            StartCoroutine(InitializeCoroutine());
        }

        public IEnumerator InitializeCoroutine()
        {
            Status = CurrencyConverterStatus.Initializing;

            foreach (var module in _modules)
            {
                module.Initialize();
            }

            while (true)
            {
                bool anyInitialized = false;
                foreach (var module in _modules)
                {
                    anyInitialized |= module.IsInitialized;
                    if (anyInitialized) break;
                }

                if (anyInitialized)
                {
                    break;
                }

                yield return null;
            }

            Status = CurrencyConverterStatus.Initialized;
        }

        public decimal? Convert(string from, string to, decimal amount)
        {
            from = from.ToLower();
            to = to.ToLower();
            double? usdAmount = null;
            foreach (var module in _modules)
            {
                usdAmount = module.ConvertToUsd(from, (double)amount);
                if (usdAmount.HasValue) break;
            }

            if (!usdAmount.HasValue)
            {
                QuickLog.Warning<CurrencyConverterBase<T>>(
                    $"No conversion available from {from} to USD."
                );
                return null;
            }

            double? finalAmount = null;
            foreach (var module in _modules)
            {
                finalAmount = module.ConvertFromUsd(to, usdAmount.Value);
                if (finalAmount.HasValue) break;
            }

            if (!finalAmount.HasValue)
            {
                QuickLog.Warning<CurrencyConverterBase<T>>(
                    $"No conversion available from USD to {to}."
                );
                return null;
            }

            return (decimal)finalAmount.Value;
        }
    }
}