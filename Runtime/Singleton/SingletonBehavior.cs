using System;
using System.Collections.Generic;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{
    public class GlobalReferenceHelper
    {
        private static Type _holderType;
        private static Dictionary<Type, PropertyInfo> _registrationProperties;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeHolder()
        {
            QuickLog.Debug<GlobalReferenceHelper>(
                "Initializing Global Reference Helper"
            );

            _registrationProperties = new Dictionary<Type, PropertyInfo>();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            List<Type> holderTypes = new List<Type>();
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (
                            type.GetCustomAttribute(
                                typeof(GlobalReferenceHolderAttribute), false
                            ) is GlobalReferenceHolderAttribute
                        )
                        {
                            holderTypes.Add(type);
                        }
                    }
                }
                catch
                {
                    // Ignore assemblies that can't be loaded
                }
            }

            if (holderTypes.Count == 0)
            {
                QuickLog.Warning<GlobalReferenceHelper>(
                    "No Global Reference Holder types found in loaded assemblies. Skipping initialization."
                );
                return;
            }

            if (holderTypes.Count > 1)
            {
                QuickLog.Warning<GlobalReferenceHelper>(
                    "Multiple Global Reference Holder types found. Using the first one: {0}",
                    holderTypes[0].FullName
                );
            }

            _holderType = holderTypes[0];

            QuickLog.Debug<GlobalReferenceHelper>(
                "Global Reference Helper initialized with holder type: {0}",
                _holderType.FullName
            );

            foreach (PropertyInfo property in _holderType.GetProperties(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ))
            {
                if (property.PropertyType.IsClass || property.PropertyType.IsInterface)
                {
                    _registrationProperties[property.PropertyType] = property;

                    QuickLog.Debug<GlobalReferenceHelper>(
                        "Registered global reference property: {0}.{1} of type {2}",
                        _holderType.Name,
                        property.Name,
                        property.PropertyType.Name
                    );
                }
            }
        }

        public static T GetReference<T>() where T : class
        {
            if (_registrationProperties == null)
            {
                QuickLog.Error<GlobalReferenceHelper>(
                    "Global Reference Helper is not initialized. Cannot get reference of type {0}.",
                    typeof(T).Name
                );
                return null;
            }

            if (_registrationProperties.TryGetValue(typeof(T), out PropertyInfo property))
            {
                return property.GetValue(null) as T;
            }

            foreach (KeyValuePair<Type, PropertyInfo> kvp in _registrationProperties)
            {
                if (typeof(T).IsAssignableFrom(kvp.Key))
                {
                    return kvp.Value.GetValue(null) as T;
                }
            }

            QuickLog.Warning<GlobalReferenceHelper>(
                "No global reference found for type {0}.",
                typeof(T).Name
            );

            return null;
        }
    }

    [AttributeUsage(
        AttributeTargets.Class,
        Inherited = false,
        AllowMultiple = false
    )]
    public class DontDestroyOnLoadAttribute : Attribute
    {
        public DontDestroyOnLoadAttribute()
        { }
    }

    [AttributeUsage(
        AttributeTargets.Class,
        Inherited = false,
        AllowMultiple = false
    )]
    public class AutoRegisterGlobalAttribute : Attribute
    {
        public Type RegistrationHolder { get; }
        public string RegistrationName { get; }

        public AutoRegisterGlobalAttribute(Type holder, string registrationName)
        {
            RegistrationHolder = holder;
            RegistrationName = registrationName;
        }
    }

    [AttributeUsage(
        AttributeTargets.Class,
        Inherited = false,
        AllowMultiple = false
    )]
    public class GlobalReferenceHolderAttribute : Attribute
    {
        public GlobalReferenceHolderAttribute()
        { }
    }

    public class SingletonBehavior<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        public static T Instance => _instance;

        private static T _instance;
        private PropertyInfo _registrationHolderProperty;

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                InitializeSingletonBehaviour();
            }
            else
            {
                QuickLog.Warning<SingletonBehavior<T>>(
                    "An instance of singleton {0} already exists. Destroying duplicate on {1}.",
                    typeof(T).Name,
                    gameObject.scene.name
                );

                Destroy(gameObject);
            }
        }

        private void InitializeSingletonBehaviour()
        {
            _instance = (T)(object)this;

            if (
                GetType().GetCustomAttribute(
                    typeof(DontDestroyOnLoadAttribute), false
                ) is DontDestroyOnLoadAttribute
            )
            {
                DontDestroyOnLoad(gameObject);
            }

            if (
                GetType().GetCustomAttribute(
                    typeof(AutoRegisterGlobalAttribute), false
                ) is AutoRegisterGlobalAttribute autoRegisterAttribute
            )
            {
                AutoRegister(autoRegisterAttribute);
            }
        }

        private void AutoRegister(AutoRegisterGlobalAttribute autoRegisterAttribute)
        {
            _registrationHolderProperty = autoRegisterAttribute
                .RegistrationHolder
                .GetProperty(
                    autoRegisterAttribute.RegistrationName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );

            try
            {
                _registrationHolderProperty.SetValue(
                    null, _instance
                );

                QuickLog.Debug<SingletonBehavior<T>>(
                    "Auto-registered singleton {0} to {1}.{2}.",
                    typeof(T).Name,
                    autoRegisterAttribute.RegistrationHolder.Name,
                    autoRegisterAttribute.RegistrationName
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<SingletonBehavior<T>>(
                    "Failed to auto-register singleton {0} to {1}.{2}. Exception: {3}",
                    typeof(T).Name,
                    autoRegisterAttribute.RegistrationHolder.Name,
                    autoRegisterAttribute.RegistrationName,
                    ex
                );
            }

        }

        protected virtual void OnDestroy()
        {
            if (_instance == null || _instance != this) return;
            Unregister();
            _instance = null;
        }

        private void Unregister()
        {
            try
            {
                _registrationHolderProperty.SetValue(null, null);

                QuickLog.Debug<SingletonBehavior<T>>(
                    "Un-registered singleton {0} from {1}.{2}.",
                    typeof(T).Name,
                    _registrationHolderProperty.DeclaringType.Name,
                    _registrationHolderProperty.Name
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<SingletonBehavior<T>>(
                    "Failed to un-register singleton {0} from {1}.{2}. Exception: {3}",
                    typeof(T).Name,
                    _registrationHolderProperty.DeclaringType.Name,
                    _registrationHolderProperty.Name,
                    ex
                );
            }
        }
    }
}
