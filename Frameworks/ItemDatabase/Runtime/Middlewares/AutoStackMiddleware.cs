namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Middlewares
{
    [ItemDatabaseMiddleware(DefaultPriority = 100,
        Description = "Compatibility hook. Capacity-aware stacking is enforced atomically by the Item Database engine.")]
    public class AutoStackMiddleware : IItemDatabaseBeforeAddMiddleware
    {
        public int Priority => 100;

        public void OnBeforeAdd(InventoryItem item)
        {
            // Core stacking executes inside the serialized add transaction.
        }
    }
}
