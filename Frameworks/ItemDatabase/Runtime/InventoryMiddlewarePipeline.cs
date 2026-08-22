using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private readonly Dictionary<Type, object> _registeredInstances
            = new Dictionary<Type, object>();

        public void Register(object middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));

            Type middlewareType = middleware.GetType();
            ValidateMiddlewareType(
                middlewareType,
                requireParameterlessConstructor: false
            );
            if (!_registeredInstances.TryAdd(middlewareType, middleware))
            {
                throw new ItemDatabaseException(
                    $"Middleware '{middlewareType.FullName}' is registered more than once."
                );
            }

            if (middleware is IItemDatabaseBeforeAddMiddleware beforeAdd)
            {
                _beforeAdd.Add(beforeAdd);
            }

            if (middleware is IItemDatabaseBeforeRemoveMiddleware beforeRemove)
            {
                _beforeRemove.Add(beforeRemove);
            }

            if (middleware is IItemDatabaseBeforeSetTagMiddleware beforeSetTag)
            {
                _beforeSetTag.Add(beforeSetTag);
            }

            if (middleware is IItemDatabaseAfterAddMiddleware afterAdd)
            {
                _afterAdd.Add(afterAdd);
            }

            if (middleware is IItemDatabaseAfterRemoveMiddleware afterRemove)
            {
                _afterRemove.Add(afterRemove);
            }

            if (middleware is IItemDatabaseAfterSetTagMiddleware afterSetTag)
            {
                _afterSetTag.Add(afterSetTag);
            }

            SortAll();
        }

        public void Unregister(object middleware)
        {
            if (middleware == null) return;

            Type middlewareType = middleware.GetType();
            if (!_registeredInstances.TryGetValue(
                    middlewareType,
                    out object registered)
                || !ReferenceEquals(registered, middleware))
            {
                return;
            }

            _registeredInstances.Remove(middlewareType);
            if (middleware is IItemDatabaseBeforeAddMiddleware bAdd) _beforeAdd.Remove(bAdd);
            if (middleware is IItemDatabaseBeforeRemoveMiddleware bRem) _beforeRemove.Remove(bRem);
            if (middleware is IItemDatabaseBeforeSetTagMiddleware bSet) _beforeSetTag.Remove(bSet);
            if (middleware is IItemDatabaseAfterAddMiddleware aAdd) _afterAdd.Remove(aAdd);
            if (middleware is IItemDatabaseAfterRemoveMiddleware aRem) _afterRemove.Remove(aRem);
            if (middleware is IItemDatabaseAfterSetTagMiddleware aSet) _afterSetTag.Remove(aSet);
        }

        public void BeforeAdd(InventoryItem item)
        {
            foreach (IItemDatabaseBeforeAddMiddleware middleware in _beforeAdd)
            {
                middleware.OnBeforeAdd(item);
            }
        }

        public void BeforeRemove(string key)
        {
            foreach (IItemDatabaseBeforeRemoveMiddleware middleware in _beforeRemove)
            {
                middleware.OnBeforeRemove(key);
            }
        }

        public void BeforeSetTag(string key, Type tagDefType, ITagData data)
        {
            foreach (IItemDatabaseBeforeSetTagMiddleware middleware in _beforeSetTag)
            {
                middleware.OnBeforeSetTag(key, tagDefType, data);
            }
        }

        public void AfterAdd(InventoryItem item)
        {
            foreach (IItemDatabaseAfterAddMiddleware middleware in _afterAdd)
            {
                middleware.OnAfterAdd(item);
            }
        }

        public void AfterRemove(string key)
        {
            foreach (IItemDatabaseAfterRemoveMiddleware middleware in _afterRemove)
            {
                middleware.OnAfterRemove(key);
            }
        }

        public void AfterSetTag(string key, Type tagDefType, ITagData data)
        {
            foreach (IItemDatabaseAfterSetTagMiddleware middleware in _afterSetTag)
            {
                middleware.OnAfterSetTag(key, tagDefType, data);
            }
        }

        private void SortAll()
        {
            StableSort(_beforeAdd, middleware => middleware.Priority);
            StableSort(_beforeRemove, middleware => middleware.Priority);
            StableSort(_beforeSetTag, middleware => middleware.Priority);
            StableSort(_afterAdd, middleware => middleware.Priority);
            StableSort(_afterRemove, middleware => middleware.Priority);
            StableSort(_afterSetTag, middleware => middleware.Priority);
        }

        internal static InventoryMiddlewarePipeline FromConfig(ItemDatabaseConfiguration config)
        {
            var pipeline = new InventoryMiddlewarePipeline();
            var configuredTypes = new HashSet<Type>();
            foreach (string typeName in config.MiddlewareTypeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    throw new ItemDatabaseException(
                        "Middleware configuration contains an empty type name."
                    );
                }

                Type type = Type.GetType(typeName);
                if (type == null)
                {
                    throw new ItemDatabaseException(
                        $"Middleware type was not found: {typeName}"
                    );
                }

                ValidateMiddlewareType(
                    type,
                    requireParameterlessConstructor: true
                );
                if (!configuredTypes.Add(type))
                {
                    throw new ItemDatabaseException(
                        $"Middleware '{type.FullName}' is configured more than once."
                    );
                }

                pipeline.Register(Activator.CreateInstance(type));
            }

            return pipeline;
        }

        private static void ValidateMiddlewareType(
            Type type,
            bool requireParameterlessConstructor)
        {
            if (type == null
                || type.IsAbstract
                || type.IsInterface
                || type.ContainsGenericParameters)
            {
                throw new ItemDatabaseException("Middleware must be a concrete class.");
            }

            if (type.GetCustomAttribute<ItemDatabaseMiddlewareAttribute>() == null)
            {
                throw new ItemDatabaseException(
                    $"Middleware '{type.FullName}' is missing [ItemDatabaseMiddleware]."
                );
            }

            if (requireParameterlessConstructor
                && type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new ItemDatabaseException(
                    $"Middleware '{type.FullName}' needs a public parameterless constructor."
                );
            }

            bool implementsHook = ImplementsSupportedHook(type);
            if (!implementsHook)
            {
                throw new ItemDatabaseException(
                    $"Middleware '{type.FullName}' does not implement an Item Database hook."
                );
            }
        }

        internal static bool ImplementsSupportedHook(Type type)
        {
            return type != null
                && (typeof(IItemDatabaseBeforeAddMiddleware).IsAssignableFrom(type)
                || typeof(IItemDatabaseBeforeRemoveMiddleware).IsAssignableFrom(type)
                || typeof(IItemDatabaseBeforeSetTagMiddleware).IsAssignableFrom(type)
                || typeof(IItemDatabaseAfterAddMiddleware).IsAssignableFrom(type)
                || typeof(IItemDatabaseAfterRemoveMiddleware).IsAssignableFrom(type)
                || typeof(IItemDatabaseAfterSetTagMiddleware).IsAssignableFrom(type));
        }

        private static void StableSort<T>(List<T> values, Func<T, int> getPriority)
        {
            T[] ordered = values
                .Select((value, index) => new { value, index })
                .OrderBy(entry => getPriority(entry.value))
                .ThenBy(entry => entry.index)
                .Select(entry => entry.value)
                .ToArray();
            values.Clear();
            values.AddRange(ordered);
        }
    }
}
