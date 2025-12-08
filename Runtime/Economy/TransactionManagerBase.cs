using System;
using System.Collections.Generic;
using System.Linq;

using Com.Hapiga.FallAway.Economy;
using Com.Hapiga.FallAway.Inventory;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Economy;
using Com.Hapiga.Schehrazade.IS;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    [DisallowMultipleComponent]
    [AddComponentMenu("FallAway/Manager/Transaction Manager")]
    public abstract class TransactionManagerBase<T> :
        SingletonBehavior<T>
        where T : TransactionManagerBase<T>
    {
        #region Event & Delegates

        public event Action<TransactionResult> TransactionCompleted;

        #endregion

        #region Interfaces & Properties

        public TransactionDatabase TransactionDatabase => transactionDatabase;
        public abstract IInventoryManager InventoryManager { get; }

        #endregion

        #region Serialized Fields

        [SerializeField]
        private TransactionDatabase transactionDatabase;

        #endregion

        public bool CheckTransactionsPerformable(IEnumerable<Transaction> transactions)
        {
            var totalCosts = new Dictionary<ItemData, int>();
            foreach (var transaction in transactions)
            foreach (var cost in transaction.Costs)
                if (totalCosts.ContainsKey(cost.ItemDefinition.ItemData))
                    totalCosts[cost.ItemDefinition.ItemData] += cost.Count;
                else
                    totalCosts[cost.ItemDefinition.ItemData] = cost.Count;

            foreach (var (itemData, count) in totalCosts)
            {
                var total = InventoryManager.IngameInventory.CountStackByType(itemData.ItemDefinition.ItemType);
                if (total < count) return false;
            }

            return true;
        }

        public bool CheckTransactionPerformable(Transaction transaction)
        {
            foreach (var cost in transaction.Costs)
            {
                var total = InventoryManager.IngameInventory.CountStackByType(cost.ItemDefinition.ItemType);
                if (total < cost.Count) return false;
            }

            return true;
        }

        public TransactionResult PerformTransactions(params Transaction[] transactions)
        {
            return PerformTransactions((IEnumerable<Transaction>)transactions);
        }

        public TransactionResult PerformTransactions(IEnumerable<Transaction> transactions)
        {
            var result = new TransactionResult
            {
                Success = true,
                Transactions = transactions,
                FailedTransactions = new List<Transaction>()
            };

            foreach (var transaction in transactions)
            {
                if (CheckTransactionPerformable(transaction)) continue;
                result.FailedTransactions.Append(transaction);
            }

            if (result.FailedTransactions.Count() > 0)
            {
                result.Success = false;
                result.ErrorMessage = "Transaction failed.";
            }

            foreach (var transaction in transactions)
            {
                PayTransactionCosts(transaction);
                DistributeRewards(transaction);
            }

            InventoryManager.SaveIngameInventory();
            TransactionCompleted?.Invoke(result);
            return result;
        }

        private void DistributeRewards(Transaction transaction)
        {
            foreach (var reward in transaction.Rewards)
            {
                IEnumerable<InventoryItem> inventoryItems = InventoryManager.IngameInventory
                    .FindManyByItemId(reward.ItemDefinition.ItemData.ItemId)
                    .OrderByDescending(item => item.Count);

                DistributeSingleReward(reward, inventoryItems);
            }
        }

        private void DistributeSingleReward(TransactionItem reward, IEnumerable<InventoryItem> inventoryItems)
        {
            switch (reward.ExpiryMode)
            {
                case ExpirationMode.NoExpiration:
                    InventoryManager.IngameInventory
                        .AddItem(reward.ItemDefinition.ItemData.ItemId, reward.Count, (DateTime?)null);
                    break;
                case ExpirationMode.AtDate:
                    InventoryManager.IngameInventory
                        .AddItem(reward.ItemDefinition.ItemData.ItemId, reward.Count, reward.ExpiryDate);
                    break;
                case ExpirationMode.AfterDuration:
                    InventoryManager.IngameInventory
                        .AddItem(reward.ItemDefinition.ItemData.ItemId, reward.Count, reward.ExpiryDuration);
                    break;
            }
        }

        private void PayTransactionCosts(Transaction transaction)
        {
            foreach (var cost in transaction.Costs)
            {
                IEnumerable<InventoryItem> inventoryItems = InventoryManager.IngameInventory
                    .FindManyByItemId(cost.ItemDefinition.ItemData.ItemId)
                    .OrderBy(item => item.Count);

                var remainingCost = cost.Count;
                var payQueue = new Queue<InventoryItem>(inventoryItems);
                while (remainingCost > 0)
                {
                    payQueue.TryDequeue(out var item);
                    var payAmount = Math.Min(item.Count, remainingCost);
                    item.Count -= payAmount;
                    remainingCost -= payAmount;
                }
            }
        }
    }
}

namespace Com.Hapiga.FallAway.Economy
{
    public struct TransactionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public IEnumerable<Transaction> Transactions { get; set; }
        public IEnumerable<Transaction> FailedTransactions { get; set; }
    }
}