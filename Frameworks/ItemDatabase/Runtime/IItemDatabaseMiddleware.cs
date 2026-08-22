using System;
using UnityEngine.Scripting;

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

    // BEFORE hooks execute inside the serialized command, before mutation.

    [RequireImplementors]
    public interface IItemDatabaseBeforeAddMiddleware
    {
        int Priority { get; }
        void OnBeforeAdd(InventoryItem item);
    }

    [RequireImplementors]
    public interface IItemDatabaseBeforeRemoveMiddleware
    {
        int Priority { get; }
        void OnBeforeRemove(string key);
    }

    [RequireImplementors]
    public interface IItemDatabaseBeforeSetTagMiddleware
    {
        int Priority { get; }
        void OnBeforeSetTag(string key, Type tagDefType, ITagData data);
    }

    // AFTER hooks execute in command order after mutation. Their exceptions
    // are isolated and cannot change an already-committed result.

    [RequireImplementors]
    public interface IItemDatabaseAfterAddMiddleware
    {
        int Priority { get; }
        void OnAfterAdd(InventoryItem item);
    }

    [RequireImplementors]
    public interface IItemDatabaseAfterRemoveMiddleware
    {
        int Priority { get; }
        void OnAfterRemove(string key);
    }

    [RequireImplementors]
    public interface IItemDatabaseAfterSetTagMiddleware
    {
        int Priority { get; }
        void OnAfterSetTag(string key, Type tagDefType, ITagData data);
    }
}
