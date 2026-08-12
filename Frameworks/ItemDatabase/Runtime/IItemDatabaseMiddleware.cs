using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Marks a class as discoverable Item Database middleware.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ItemDatabaseMiddlewareAttribute : Attribute
    {
        public int DefaultPriority { get; set; } = 500;

        public string Description { get; set; }
    }

    // ─── BEFORE hooks (caller thread, BEFORE enqueue) ───

    public interface IItemDatabaseBeforeAddMiddleware
    {
        int Priority { get; }
        void OnBeforeAdd(InventoryItem item);
    }

    public interface IItemDatabaseBeforeRemoveMiddleware
    {
        int Priority { get; }
        void OnBeforeRemove(string key);
    }

    public interface IItemDatabaseBeforeSetTagMiddleware
    {
        int Priority { get; }
        void OnBeforeSetTag(string key, Type tagDefType, ITagData data);
    }

    // ─── AFTER hooks (commit thread, AFTER state applied) ───

    public interface IItemDatabaseAfterAddMiddleware
    {
        int Priority { get; }
        void OnAfterAdd(InventoryItem item);
    }

    public interface IItemDatabaseAfterRemoveMiddleware
    {
        int Priority { get; }
        void OnAfterRemove(string key);
    }

    public interface IItemDatabaseAfterSetTagMiddleware
    {
        int Priority { get; }
        void OnAfterSetTag(string key, Type tagDefType, ITagData data);
    }
}
