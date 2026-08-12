using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Chain executor for split middleware hooks. Each hook point has its
    /// own ordered list; classes implement only the hooks they need.
    /// </summary>
    public class InventoryMiddlewarePipeline
    {
        private readonly List<IItemDatabaseBeforeAddMiddleware> _beforeAdd = new List<IItemDatabaseBeforeAddMiddleware>();
        private readonly List<IItemDatabaseBeforeRemoveMiddleware> _beforeRemove = new List<IItemDatabaseBeforeRemoveMiddleware>();
        private readonly List<IItemDatabaseBeforeSetTagMiddleware> _beforeSetTag = new List<IItemDatabaseBeforeSetTagMiddleware>();
        private readonly List<IItemDatabaseAfterAddMiddleware> _afterAdd = new List<IItemDatabaseAfterAddMiddleware>();
        private readonly List<IItemDatabaseAfterRemoveMiddleware> _afterRemove = new List<IItemDatabaseAfterRemoveMiddleware>();
        private readonly List<IItemDatabaseAfterSetTagMiddleware> _afterSetTag = new List<IItemDatabaseAfterSetTagMiddleware>();

        public void Register(object middleware)
        {
            if (middleware is IItemDatabaseBeforeAddMiddleware bAdd) _beforeAdd.Add(bAdd);
            if (middleware is IItemDatabaseBeforeRemoveMiddleware bRem) _beforeRemove.Add(bRem);
            if (middleware is IItemDatabaseBeforeSetTagMiddleware bSet) _beforeSetTag.Add(bSet);
            if (middleware is IItemDatabaseAfterAddMiddleware aAdd) _afterAdd.Add(aAdd);
            if (middleware is IItemDatabaseAfterRemoveMiddleware aRem) _afterRemove.Add(aRem);
            if (middleware is IItemDatabaseAfterSetTagMiddleware aSet) _afterSetTag.Add(aSet);
            SortAll();
        }

        public void Unregister(object middleware)
        {
            if (middleware is IItemDatabaseBeforeAddMiddleware bAdd) _beforeAdd.Remove(bAdd);
            if (middleware is IItemDatabaseBeforeRemoveMiddleware bRem) _beforeRemove.Remove(bRem);
            if (middleware is IItemDatabaseBeforeSetTagMiddleware bSet) _beforeSetTag.Remove(bSet);
            if (middleware is IItemDatabaseAfterAddMiddleware aAdd) _afterAdd.Remove(aAdd);
            if (middleware is IItemDatabaseAfterRemoveMiddleware aRem) _afterRemove.Remove(aRem);
            if (middleware is IItemDatabaseAfterSetTagMiddleware aSet) _afterSetTag.Remove(aSet);
        }

        public void BeforeAdd(InventoryItem item)
        { foreach (var mw in _beforeAdd) mw.OnBeforeAdd(item); }

        public void BeforeRemove(string key)
        { foreach (var mw in _beforeRemove) mw.OnBeforeRemove(key); }

        public void BeforeSetTag(string key, Type tagDefType, ITagData data)
        { foreach (var mw in _beforeSetTag) mw.OnBeforeSetTag(key, tagDefType, data); }

        public void AfterAdd(InventoryItem item)
        { foreach (var mw in _afterAdd) mw.OnAfterAdd(item); }

        public void AfterRemove(string key)
        { foreach (var mw in _afterRemove) mw.OnAfterRemove(key); }

        public void AfterSetTag(string key, Type tagDefType, ITagData data)
        { foreach (var mw in _afterSetTag) mw.OnAfterSetTag(key, tagDefType, data); }

        private void SortAll()
        {
            _beforeAdd.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _beforeRemove.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _beforeSetTag.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _afterAdd.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _afterRemove.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _afterSetTag.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        internal static InventoryMiddlewarePipeline FromConfig(ItemDatabaseConfiguration config)
        {
            var pipeline = new InventoryMiddlewarePipeline();
            foreach (var typeName in config.MiddlewareTypeNames)
            {
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    QuickLog.Warning<InventoryMiddlewarePipeline>("Middleware type not found: {0}", typeName);
                    continue;
                }

                pipeline.Register((object)Activator.CreateInstance(type));
            }

            return pipeline;
        }
    }
}
