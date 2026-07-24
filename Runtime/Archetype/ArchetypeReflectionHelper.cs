using System;
using System.Collections.Generic;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Archetype
{
    public static class ArchetypeReflectionHelper
    {
        private static readonly Dictionary<string, Delegate> RegisterCache
            = new Dictionary<string, Delegate>();

        private static readonly Dictionary<string, Delegate> UnregisterCache
            = new Dictionary<string, Delegate>();

        private static Type[] _allTypes;

        public static void TryRegister(
            object instance,
            IEnumerable<ArchetypeAttribute> attributes)
        {
            if (instance == null || attributes == null)
            {
                return;
            }

            foreach (ArchetypeAttribute attr in attributes)
            {
                TryInvokeCached(
                    instance,
                    attr.ArchetypeName,
                    "Register",
                    attr.InterfaceType,
                    RegisterCache);
            }
        }

        public static void TryUnregister(
            object instance,
            IEnumerable<ArchetypeAttribute> attributes)
        {
            if (instance == null || attributes == null)
            {
                return;
            }

            foreach (ArchetypeAttribute attr in attributes)
            {
                TryInvokeCached(
                    instance,
                    attr.ArchetypeName,
                    "Unregister",
                    attr.InterfaceType,
                    UnregisterCache);
            }
        }

        private static void TryInvokeCached(
            object instance,
            string archetypeName,
            string methodName,
            Type interfaceType,
            Dictionary<string, Delegate> cache)
        {
            if (!cache.TryGetValue(archetypeName, out Delegate del))
            {
                Type archetypeType = FindArchetypeType(archetypeName);
                if (archetypeType == null)
                {
                    QuickLog.SWarning(
                        "Archetype class '{0}' not found. "
                        + "Skipping {1} for {2}.",
                        archetypeName,
                        methodName,
                        instance.GetType().Name);
                    return;
                }

                MethodInfo method = archetypeType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static);

                if (method == null)
                {
                    QuickLog.SWarning(
                        "Method '{0}' not found on archetype class '{1}'.",
                        methodName,
                        archetypeName);
                    return;
                }

                try
                {
                    Type delegateType = typeof(Action<>).MakeGenericType(
                        interfaceType);
                    del = Delegate.CreateDelegate(delegateType, method);
                }
                catch (Exception ex)
                {
                    QuickLog.SError(
                        "Failed to create delegate for {0}.{1}(). "
                        + "Check that the interface type {2} matches. "
                        + "Exception: {3}",
                        archetypeName,
                        methodName,
                        interfaceType.Name,
                        ex);
                    return;
                }

                cache[archetypeName] = del;
            }

            try
            {
                del.DynamicInvoke(instance);
            }
            catch (Exception ex)
            {
                QuickLog.SError(
                    "Failed to invoke {0}.{1}() for instance of {2}. "
                    + "Exception: {3}",
                    archetypeName,
                    methodName,
                    instance.GetType().Name,
                    ex);
            }
        }

        private static Type FindArchetypeType(string archetypeName)
        {
            if (_allTypes == null)
            {
                CacheAllTypes();
            }

            for (int i = 0; i < _allTypes.Length; i++)
            {
                if (_allTypes[i].Name == archetypeName)
                {
                    return _allTypes[i];
                }
            }

            return null;
        }

        private static void CacheAllTypes()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Type> allTypes = new List<Type>();

            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type[] types = assemblies[i].GetTypes();
                    allTypes.AddRange(types);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (ex.Types != null)
                    {
                        for (int j = 0; j < ex.Types.Length; j++)
                        {
                            if (ex.Types[j] != null)
                            {
                                allTypes.Add(ex.Types[j]);
                            }
                        }
                    }
                }
                catch
                {
                    // Assembly cannot be scanned — skip
                }
            }

            _allTypes = allTypes.ToArray();
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InvalidateCacheOnReload()
        {
            _allTypes = null;
            RegisterCache.Clear();
            UnregisterCache.Clear();
        }
#endif
    }
}
