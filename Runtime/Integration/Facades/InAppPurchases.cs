using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    /// <summary>
    /// Static facade over the registered <see cref="IInAppPurchaseManager"/>.
    /// Exposes query, event, fire-and-forget, coroutine and async APIs.
    /// </summary>
    public class InAppPurchases
    {
        #region Queries

        public static IInAppPurchaseManager Manager => Integration.InAppPurchaseManager;

        public static bool IsAvailable => Manager != null;

        public static InAppPurchaseManagerStatus Status =>
            Manager != null ? Manager.Status : InAppPurchaseManagerStatus.Uninitialized;

        public static bool IsReady => Status == InAppPurchaseManagerStatus.Ready;

        public static IInAppPurchaseDatabase ProductDatabase => Manager?.ProductDatabase;

        public static bool HasRestorableProducts => Manager != null && Manager.HasRestorableProducts;

        public static InAppPurchaseProductPrice? GetProductPrice(string productId)
        {
            return Manager != null ? Manager.GetProductPrice(productId) : null;
        }

        #endregion

        #region Events

        public static event Action<IInAppPurchaseProduct> PurchaseInitiated
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.PurchaseInitiated += value;
            }
            remove
            {
                if (Manager != null) Manager.PurchaseInitiated -= value;
            }
        }

        public static event Action<IInAppPurchaseProduct> PurchaseSucceeded
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.PurchaseSucceeded += value;
            }
            remove
            {
                if (Manager != null) Manager.PurchaseSucceeded -= value;
            }
        }

        public static event Action<IInAppPurchaseProduct> PurchaseFailed
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.PurchaseFailed += value;
            }
            remove
            {
                if (Manager != null) Manager.PurchaseFailed -= value;
            }
        }

        public static event Action<IInAppPurchaseProduct> PurchaseDeferred
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.PurchaseDeferred += value;
            }
            remove
            {
                if (Manager != null) Manager.PurchaseDeferred -= value;
            }
        }

        public static event Action<IInAppPurchaseProduct> ProductRestored
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.ProductRestored += value;
            }
            remove
            {
                if (Manager != null) Manager.ProductRestored -= value;
            }
        }

        public static event Action<bool> AllProductsRestored
        {
            add
            {
                if (TrySubscribe(out IInAppPurchaseManager manager)) manager.AllProductsRestored += value;
            }
            remove
            {
                if (Manager != null) Manager.AllProductsRestored -= value;
            }
        }

        #endregion

        #region Initialization

        public static void Initialize(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IInAppPurchaseManager manager))
            {
                return;
            }

            manager.Initialize(timeOut);
        }

        public static IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (!TryGetManager(out IInAppPurchaseManager manager))
            {
                yield break;
            }

            IEnumerator steps = manager.InitializeCoroutine(timeOut);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task InitializeAsync(float timeOut = float.MaxValue, CancellationToken ct = default)
        {
            RequireManager();
            return CoroutineTaskBridge.RunAsync(InitializeCoroutine(timeOut));
        }

        #endregion

        #region Buy Product

        public static void BuyProduct(string productId)
        {
            if (!TryGetManager(out IInAppPurchaseManager manager))
            {
                return;
            }

            manager.BuyProduct(productId);
        }

        public static IEnumerator BuyProductCoroutine(
            string productId,
            Action<bool> onResult,
            float timeoutSeconds = 30f
        )
        {
            if (!TryGetManager(out IInAppPurchaseManager manager))
            {
                onResult?.Invoke(false);
                yield break;
            }

            IEnumerator steps = BuyProductCoroutineImpl(manager, productId, onResult, timeoutSeconds);
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }

        public static Task<bool> BuyProductAsync(
            string productId,
            float timeoutSeconds = 30f,
            CancellationToken ct = default
        )
        {
            IInAppPurchaseManager manager = RequireManager();
            return CoroutineTaskBridge.RunWithCallbackAsync<bool>(
                onResult => BuyProductCoroutineImpl(manager, productId, onResult, timeoutSeconds),
                timeoutSeconds,
                ct
            );
        }

        #endregion

        #region Restore

        public static void RestorePurchases()
        {
            if (!TryGetManager(out IInAppPurchaseManager manager))
            {
                return;
            }

            manager.RestorePurchases();
        }

        #endregion

        #region Private Methods

        private static IEnumerator BuyProductCoroutineImpl(
            IInAppPurchaseManager manager,
            string productId,
            Action<bool> onResult,
            float timeoutSeconds
        )
        {
            bool completed = false;
            bool success = false;
            float deadline = Time.time + timeoutSeconds;

            void Complete(bool ok)
            {
                success = ok;
                completed = true;
            }

            void HandleSucceeded(IInAppPurchaseProduct product) => Complete(true);
            void HandleFailed(IInAppPurchaseProduct product) => Complete(false);
            void HandleDeferred(IInAppPurchaseProduct product) => Complete(false);

            try
            {
                manager.PurchaseSucceeded += HandleSucceeded;
                manager.PurchaseFailed += HandleFailed;
                manager.PurchaseDeferred += HandleDeferred;
                manager.BuyProduct(productId);
            }
            catch (Exception ex)
            {
                manager.PurchaseSucceeded -= HandleSucceeded;
                manager.PurchaseFailed -= HandleFailed;
                manager.PurchaseDeferred -= HandleDeferred;
                QuickLog.Error<InAppPurchases>("Buy product '{0}' failed: {1}", productId, ex);
                onResult?.Invoke(false);
                yield break;
            }

            while (!completed && Time.time < deadline)
            {
                yield return null;
            }

            manager.PurchaseSucceeded -= HandleSucceeded;
            manager.PurchaseFailed -= HandleFailed;
            manager.PurchaseDeferred -= HandleDeferred;

            if (!completed)
            {
                QuickLog.Warning<InAppPurchases>("Buy product '{0}' timed out.", productId);
            }

            onResult?.Invoke(success);
        }

        private static bool TryGetManager(out IInAppPurchaseManager manager)
        {
            manager = Integration.InAppPurchaseManager;
            if (manager == null)
            {
                QuickLog.Warning<InAppPurchases>(
                    "In-App Purchase manager is not registered. Ensure the module is enabled in the IntegrationCentre."
                );
            }

            return manager != null;
        }

        private static bool TrySubscribe(out IInAppPurchaseManager manager)
        {
            manager = Integration.InAppPurchaseManager;
            if (manager == null)
            {
                QuickLog.Warning<InAppPurchases>(
                    "In-App Purchase manager is not registered yet; subscription was dropped. " +
                    "Subscribe after initialization or use Integration.TryGetManager."
                );
            }

            return manager != null;
        }

        private static IInAppPurchaseManager RequireManager()
        {
            IInAppPurchaseManager manager = Integration.RequireManager<IInAppPurchaseManager>();
            if (manager.Status != InAppPurchaseManagerStatus.Ready)
            {
                throw new IntegrationNotInitializedException(nameof(InAppPurchases));
            }

            return manager;
        }

        #endregion
    }
}
