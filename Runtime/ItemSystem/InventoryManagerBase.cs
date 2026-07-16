using System;
using Com.Hapiga.FallAway.Inventory;
using Com.Hapiga.Scheherazade.Common;
using Com.Hapiga.Scheherazade.Common.DataSync;
using DS = global::Com.Hapiga.Scheherazade.Common.DataSync.DataSync;
using Com.Hapiga.Scheherazade.Common.Singleton;

using UnityEngine;

namespace Com.Hapiga.Schehrazade.IS
{
    public interface IInventoryManager
    {
        Inventory IngameInventory { get; }
        ItemDatabase ItemDatabase { get; }

        void ReloadIngameInventory();
        void SaveIngameInventory();
    }

    public abstract class InventoryManagerBase<SelfT, InventoryT> :
        SingletonBehavior<SelfT>, IInventoryManager
        where SelfT : SingletonBehavior<SelfT>
        where InventoryT : Inventory<InventoryT>, new()
    {
        public static string IngameInventoryKey => "ingame_inventory";

        Inventory IInventoryManager.IngameInventory => _ingameInventory;
        public InventoryT IngameInventory => _ingameInventory;
        public ItemDatabase ItemDatabase => itemDatabase;

        [SerializeField]
        private ItemDatabase itemDatabase;

        [SerializeField]
        private float writeDelay = 2f;

        private InventoryT _ingameInventory;
        private bool _writePending = false;
        private float _writeTimer;

        private void LateUpdate()
        {
            if (_ingameInventory.UpdateInventory()) SaveIngameInventory();

            if (_writeTimer > 0)
                _writeTimer -= Time.unscaledDeltaTime;
            else if (_writePending) PerformWriteInventory();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && _writePending) PerformWriteInventory();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause && _writePending) PerformWriteInventory();
        }

    private void PerformWriteInventory()
    {
        _writeTimer = writeDelay;
        _writePending = false;
        DS.Save(IngameInventoryKey, _ingameInventory);
    }

    public async void ReloadIngameInventory()
    {
        try
        {
            _ingameInventory = await DS.LoadAsync<InventoryT>(IngameInventoryKey);
        }
        catch (Exception)
        {
            _ingameInventory = new InventoryT();
        }
    }

    protected override async void Awake()
    {
        base.Awake();

        foreach (var itemData in itemDatabase.ItemMapping.Values)
        {
            itemData.ItemDefinition.ItemData = itemData;
        }

        if (!await DS.ExistsAsync(IngameInventoryKey))
        {
            _ingameInventory = new InventoryT();
            DS.Save(IngameInventoryKey, _ingameInventory);
        }

        ReloadIngameInventory();
        _writePending = false;
    }

        public void SaveIngameInventory()
        {
            _writePending = true;
        }
    }
}