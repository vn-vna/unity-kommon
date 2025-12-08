using Com.Hapiga.FallAway.Economy;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    [CreateAssetMenu(fileName = "NewTransaction", menuName = "FallAway/Economy/Transaction")]
    public class Transaction : ScriptableObject
    {
        public string TransactionId => transactionId;
        public string Name => transactionName;

        public TransactionItem[] Costs
        {
            get
            {
                if (costsProvider)
                {
                    TransactionItem[] items = costsProvider.Items;
                    if (items != null && items.Length > 0)
                    {
                        return items;
                    }
                }

                return costs;
            }
        }

        public TransactionItem[] Rewards
        {
            get
            {
                if (rewardProvider)
                {
                    TransactionItem[] items = rewardProvider.Items;
                    if (items != null && items.Length > 0)
                    {
                        return items;
                    }
                }

                return reward;
            }
        }

        [SerializeField]
        private string transactionId;

        [SerializeField]
        private string transactionName;

        [SerializeField]
        private TransactionItem[] costs;

        [SerializeField]
        private TransactionItemListProvider costsProvider;

        [SerializeField]
        private TransactionItem[] reward;

        [SerializeField]
        private TransactionItemListProvider rewardProvider;

    }
}