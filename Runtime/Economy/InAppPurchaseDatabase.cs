using System;
using System.Collections.Generic;
using System.Linq;

using Com.Hapiga.FallAway.Economy;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.MappedList;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    [CreateAssetMenu(fileName = "InAppPurchaseDatabase", menuName = "FallAway/Economy/In App Purchase Database")]
    public class InAppPurchaseDatabase :
        ScriptableObject,
        IInAppPurchaseDatabase,
        ISerializationCallbackReceiver
    {
        IEnumerable<IInAppPurchaseProduct> IInAppPurchaseDatabase.Products => Packs;

        public IReadOnlyList<InAppPurchasePack> Packs => packs;
        public MappedList<string, InAppPurchasePack> PackMapping => _packMapping.Value;


        [SerializeField]
        private List<InAppPurchasePack> packs;

        private Lazy<MappedList<string, InAppPurchasePack>> _packMapping;

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _packMapping = new Lazy<MappedList<string, InAppPurchasePack>>(CreatePackMapping);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        { }

        private MappedList<string, InAppPurchasePack> CreatePackMapping()
            => new MappedList<string, InAppPurchasePack>(packs, pack => pack.PackId);

#if UNITY_EDITOR
        [ContextMenu("Refresh Database")]
        void RefreshDatabase()
        {
            packs = UnityEditor.AssetDatabase.FindAssets("t: InAppPurchasePack")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<InAppPurchasePack>)
                .Where(asset => asset != null)
                .ToList();
        }
#endif
    }
}