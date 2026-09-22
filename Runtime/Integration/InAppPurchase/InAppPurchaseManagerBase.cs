using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{

    public abstract class InAppPurchaseManagerBase<T> :
        SingletonScriptableObject<T>,
        IInAppPurchaseManager,
        IIntegrationModule,
        ITickableModule
        where T : ScriptableObject
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
        public InAppPurchaseReceiptValidationPipeline ReceiptValidationPipeline
        {
            get => receiptValidationPipeline;
            protected set => receiptValidationPipeline = value;
        }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private ScriptableObject provider;

        [Header("Receipt Validation")]
        [SerializeField]
        private InAppPurchaseReceiptValidationPipeline receiptValidationPipeline;
        #endregion

        #region Private Fields
        private IInAppPurchaseProvider _provider;
        private int _processingGeneration;
        private bool _processingInitializing;
        private bool _watchProcessingReadiness;
        private bool _processingShuttingDown;
        #endregion

        #region Lifecycle & Unity Methods
        protected override void OnEnable()
        {
            base.OnEnable();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            if (_processingShuttingDown) return;
            if (UsesTransactionProcessing) Shutdown();
            Status = InAppPurchaseManagerStatus.Uninitialized;

            if (provider == null)
            {
                return;
            }

            if (provider is not IInAppPurchaseProvider inAppPurchaseProvider)
            {
                Debug.LogError("Assigned provider does not implement IInAppPurchaseProvider.");
                return;
            }

            RegisterProvider(inAppPurchaseProvider);
        }
        #endregion

        #region Public Methods
        public virtual void Initialize(float timeOut = float.MaxValue)
        {
            if (_processingShuttingDown) return;
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
        }

        public virtual IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (UsesTransactionProcessing)
            {
                var steps = InitializeProcessingCoroutine(timeOut);
                try { while (steps.MoveNext()) yield return steps.Current; }
                finally { (steps as IDisposable)?.Dispose(); }
                yield break;
            }
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

        public virtual void Shutdown()
        {
            if (_processingShuttingDown) return;
            _processingShuttingDown = UsesTransactionProcessing;
            ++_processingGeneration;
            _processingInitializing = false;
            _watchProcessingReadiness = false;
            try
            {
                // Advanced sessions can own SDK callbacks before becoming ready.
                // CleanUp disposes that session while retaining the reusable SO config.
                if (Provider != null && (UsesTransactionProcessing || Provider.IsInitialized))
                    Provider.CleanUp();
            }
            finally
            {
                Status = InAppPurchaseManagerStatus.Uninitialized;
                _processingShuttingDown = false;
            }
        }

        public virtual void Tick(float deltaTime)
        {
            AdvanceProcessing(Time.unscaledDeltaTime);
        }

        protected void AdvanceProcessing(float unscaledDeltaTime)
        {
            if (_processingShuttingDown) return;
            float delta = float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime)
                ? 0 : Mathf.Max(0, unscaledDeltaTime);
            if (Provider is IInAppPurchaseRetryPump pump) pump.Tick(delta);
            RefreshProcessingReadiness();
        }

        public void RegisterProvider(IInAppPurchaseProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (_processingShuttingDown) throw new InvalidOperationException("Cannot replace a provider during shutdown.");
            if (!ReferenceEquals(_provider, provider) && UsesTransactionProcessing)
            {
                var previous = _provider;
                Shutdown();
                previous.PurchaseInitiated = null;
                previous.PurchaseSucceeded = null;
                previous.PurchaseFailed = null;
                previous.PurchaseDeferred = null;
                previous.ProductRestored = null;
                previous.AllProductsRestored = null;
                previous.Manager = null;
            }
            provider.Manager = this;
            _provider = provider;
            _provider.PurchaseInitiated = HandlePurchaseInitiated;
            _provider.PurchaseSucceeded = HandlePurchaseSucceeded;
            _provider.PurchaseFailed = HandlePurchaseFailed;
            _provider.PurchaseDeferred = HandlePurchaseDeferred;
            _provider.ProductRestored = HandleProductRestored;
            _provider.AllProductsRestored = HandleAllProductsRestored;
        }

        public InAppPurchaseProductPrice? GetProductPrice(string productId) 
            => Provider?.GetProductPrice(productId);

        public virtual void BuyProduct(string productId)
        {
            if (_processingShuttingDown) return;
            Provider?.BuyProduct(productId);
        }

        public virtual void RestorePurchases()
        {
            if (_processingShuttingDown) return;
            Provider?.RestorePurchases();
        }
        #endregion

        #region Private Methods
        private bool UsesTransactionProcessing => Provider is ITransactionProcessingIapProvider configurable && configurable.IsTransactionProcessingEnabled;

        private IEnumerator InitializeProcessingCoroutine(float timeOut)
        {
            if (_processingShuttingDown || _processingInitializing || (Status == InAppPurchaseManagerStatus.Ready && Provider.IsInitialized)) yield break;
            int generation = ++_processingGeneration;
            _processingInitializing = true;
            _watchProcessingReadiness = true;
            Status = InAppPurchaseManagerStatus.Initializing;
            try
            {
                Provider.Manager = this;
                Provider.Initialize();
                float timer = 0;
                float timeout = float.IsNaN(timeOut) || float.IsInfinity(timeOut) || timeOut <= 0 || timeOut == float.MaxValue
                    ? 30 : timeOut;
                while (generation == _processingGeneration && !Provider.IsInitialized && timer < timeout)
                {
                    timer += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (generation != _processingGeneration) yield break;
                RefreshProcessingReadiness();
                if (!Provider.IsInitialized) Status = InAppPurchaseManagerStatus.Uninitialized;
            }
            finally
            {
                if (generation == _processingGeneration) _processingInitializing = false;
            }
        }

        private void RefreshProcessingReadiness()
        {
            if (!_watchProcessingReadiness || !UsesTransactionProcessing) return;
            if (!Provider.IsInitialized)
            {
                if (Status == InAppPurchaseManagerStatus.Ready) Status = InAppPurchaseManagerStatus.Uninitialized;
                return;
            }
            bool becameReady = Status != InAppPurchaseManagerStatus.Ready;
            Status = InAppPurchaseManagerStatus.Ready;
            if (becameReady) HandleInitializationComplete();
        }

        protected override void OnDisable()
        {
            if (UsesTransactionProcessing) Shutdown();
            base.OnDisable();
        }

        /// <summary>Replaces the manager-owned receipt validation binding for derived managers or fixtures.</summary>
        protected void SetReceiptValidationPipeline(InAppPurchaseReceiptValidationPipeline pipeline) =>
            ReceiptValidationPipeline = pipeline;

        protected virtual void HandleInitializationComplete()
        { }

        protected virtual void HandlePurchaseInitiated(IInAppPurchaseProduct product) => PurchaseInitiated?.Invoke(product);
        protected virtual void HandlePurchaseSucceeded(IInAppPurchaseProduct product) => PurchaseSucceeded?.Invoke(product);
        protected virtual void HandlePurchaseFailed(IInAppPurchaseProduct product) => PurchaseFailed?.Invoke(product);
        protected virtual void HandlePurchaseDeferred(IInAppPurchaseProduct product) => PurchaseDeferred?.Invoke(product);
        protected virtual void HandleProductRestored(IInAppPurchaseProduct product) => ProductRestored?.Invoke(product);
        protected virtual void HandleAllProductsRestored(bool success) => AllProductsRestored?.Invoke(success);

        #endregion
    }
}