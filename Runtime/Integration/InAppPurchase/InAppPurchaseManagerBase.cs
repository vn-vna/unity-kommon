using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{

    public abstract class InAppPurchaseManagerBase<T> :
        SingletonBehavior<T>,
        IInAppPurchaseManager
        where T : InAppPurchaseManagerBase<T>
    {
        #region Events & Delegates
        public event Action<IInAppPurchaseProduct> PurchaseInitiated;
        public event Action<IInAppPurchaseProduct> PurchaseSucceeded;
        public event Action<IInAppPurchaseProduct> PurchaseFailed;
        public event Action<IInAppPurchaseProduct> PurchaseDeferred;
        public event Action<IInAppPurchaseProduct> ProductRestored;
        public event Action<bool> AllProductsRestored;
        #endregion

        #region Interfaces & Properties 
        public IInAppPurchaseProvider Provider => _provider;
        public InAppPurchaseManagerStatus Status { get; protected set; }
        public abstract IInAppPurchaseDatabase ProductDatabase { get; }
        public bool HasRestorableProducts => _provider != null && _provider.HasRestorableProducts;
        #endregion

        #region Private Fields
        private IInAppPurchaseProvider _provider;
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            Status = InAppPurchaseManagerStatus.Uninitialized;
            Integration.RegisterManager(this);
        }
        #endregion

        #region Public Methods
        public void Initialize(float timeOut = float.MaxValue)
        {
            StartCoroutine(InitializeCoroutine(timeOut));
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            Status = InAppPurchaseManagerStatus.Initializing;
            yield return null;

            if (_provider == null)
            {
                Debug.LogError("InAppPurchaseProvider is not registered.");
                yield break;
            }

            float timer = 0f;

            _provider.Manager = this;
            _provider.Initialize();

            while (true)
            {
                if (timer > timeOut)
                {
                    Debug.LogError("InAppPurchase initialization timed out.");
                    Status = InAppPurchaseManagerStatus.Uninitialized;
                    yield break;
                }

                if (Provider.IsInitialized)
                {
                    Status = InAppPurchaseManagerStatus.Ready;
                    break;
                }

                timer += Time.deltaTime;
                yield return null;
            }

            HandleInitializationComplete();
        }

        public void Shutdown()
        {
            if (Provider != null && Provider.IsInitialized)
            {
                Provider.CleanUp();
            }

            Status = InAppPurchaseManagerStatus.Uninitialized;
        }

        public void RegisterProvider(IInAppPurchaseProvider provider)
        {
            provider.Manager = this;
            _provider = provider;
            _provider.PurchaseInitiated = HandlePurchaseInitiated;
            _provider.PurchaseSucceeded = HandlePurchaseSucceeded;
            _provider.PurchaseFailed = HandlePurchaseFailed;
            _provider.PurchaseDeferred = HandlePurchaseDeferred;
            _provider.ProductRestored = HandleProductRestored;
            _provider.AllProductsRestored = HandleAllProductsRestored;
        }

        public InAppPurchaseProductPrice? GetProductPrice(string productId) => Provider?.GetProductPrice(productId);

        public void BuyProduct(string productId)
        {
            Provider?.BuyProduct(productId);
        }

        public void RestorePurchases()
        {
            Provider?.RestorePurchases();
        }
        #endregion

        #region Private Methods
        protected virtual void HandleInitializationComplete()
        { }
        
        private void HandlePurchaseInitiated(IInAppPurchaseProduct product) => PurchaseInitiated?.Invoke(product);
        private void HandlePurchaseSucceeded(IInAppPurchaseProduct product) => PurchaseSucceeded?.Invoke(product);
        private void HandlePurchaseFailed(IInAppPurchaseProduct product) => PurchaseFailed?.Invoke(product);
        private void HandlePurchaseDeferred(IInAppPurchaseProduct product) => PurchaseDeferred?.Invoke(product);
        private void HandleProductRestored(IInAppPurchaseProduct product) => ProductRestored?.Invoke(product);
        private void HandleAllProductsRestored(bool success) => AllProductsRestored?.Invoke(success);

        #endregion
    }
}