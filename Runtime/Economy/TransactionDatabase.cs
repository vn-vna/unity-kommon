using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.MappedList;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{

    [CreateAssetMenu(fileName = "NewTransactionDatabase", menuName = "FallAway/Economy/TransactionDatabase")]
    public class TransactionDatabase :
        ScriptableObject,
        ISerializationCallbackReceiver
    {
        public Transaction[] Transactions => transactions;
        public MappedList<string, Transaction> TransactionMapping => _transactionMaping.Value;

        [SerializeField]
        private Transaction[] transactions;

        private Lazy<MappedList<string, Transaction>> _transactionMaping;

        public Transaction GetTransactionById(string transactionId)
        {
            foreach (var transaction in transactions)
            {
                if (transaction.TransactionId != transactionId) continue;
                return transaction;
            }
            return null;
        }

        public Transaction FindOne(Func<Transaction, bool> predicate)
        {
            foreach (var transaction in transactions)
            {
                if (!predicate(transaction)) continue;
                return transaction;
            }
            return null;
        }

        public IEnumerable<Transaction> FindMany(Func<Transaction, bool> predicate)
        {
            foreach (var transaction in transactions)
            {
                if (!predicate(transaction)) continue;
                yield return transaction;
            }
        }

        public bool CheckRegistered(Transaction transaction)
        {
            return TransactionMapping.ContainsKey(transaction.TransactionId) &&
                TransactionMapping[transaction.TransactionId] == transaction;
        }

        public bool CheckRegistered(string transactionId)
        {
            return TransactionMapping.ContainsKey(transactionId);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _transactionMaping = new Lazy<MappedList<string, Transaction>>(CreateTransactionMapping);
        }

        private MappedList<string, Transaction> CreateTransactionMapping()
        {
            return new MappedList<string, Transaction>(
                transactions,
                transaction => transaction.TransactionId
            );
        }

#if UNITY_EDITOR
        [ContextMenu("Refresh Database")]
        private void RefreshDatabase()
        {
            transactions = UnityEditor.AssetDatabase.FindAssets("t:Transaction")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<Transaction>)
                .Where(asset => asset != null)
                .ToArray();
        }
#endif
    }
}