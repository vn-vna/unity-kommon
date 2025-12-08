using System.Collections.Generic;

using Com.Hapiga.FallAway.Economy;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    [CreateAssetMenu(fileName = "InAppPurchasePack", menuName = "FallAway/Economy/In App Purchase Pack")]
    public class InAppPurchasePack :
        ScriptableObject,
        IInAppPurchaseProduct
    {
        string IInAppPurchaseProduct.ProductId => packId;
        bool IInAppPurchaseProduct.AllowRecover => allowRecover;
        public IReadOnlyList<Transaction> Transactions => transactions;
        public IReadOnlyList<Transaction> RecoveryTransactions => recoveryTransactions;
        public string PackId => packId;
        public string PackName => packName;
        public string SaleDisplay => saleDisplay;

        [SerializeField]
        private string packId;

        [SerializeField]
        private string packName;

        [SerializeField]
        private List<Transaction> transactions;

        [SerializeField]
        private bool allowRecover;

        [SerializeField]
        private List<Transaction> recoveryTransactions;

        [SerializeField]
        private string saleDisplay;
    }
}