using System;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{

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
        public Type ResgistrationHolder { get; }
        public string RegistrationName { get; }

        public AutoRegisterGlobalAttribute(Type holder, string registrationName)
        {
            ResgistrationHolder = holder;
            RegistrationName = registrationName;
        }
    }

    public class SingletonBehavior<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        public static T Instance
        {
            get => _instance;
        }

        private static T _instance;
        private PropertyInfo _registrationHolderProperty;

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = (T)(object)this;

                DontDestroyOnLoadAttribute singletonAttribute = GetType().GetCustomAttribute(typeof(DontDestroyOnLoadAttribute), false) as DontDestroyOnLoadAttribute;

                if (singletonAttribute != null)
                {
                    DontDestroyOnLoad(gameObject);
                }

                AutoRegisterGlobalAttribute autoRegisterAttribute = GetType().GetCustomAttribute(typeof(AutoRegisterGlobalAttribute), false) as AutoRegisterGlobalAttribute;
                if (autoRegisterAttribute != null)
                {
                    AutoRegister(autoRegisterAttribute);
                }
            }
            else
            {
                Debug.LogWarning($"An instance of {typeof(T).Name} already exists. Destroying duplicate.");
                Destroy(gameObject);
            }
        }

        private void AutoRegister(AutoRegisterGlobalAttribute autoRegisterAttribute)
        {
            _registrationHolderProperty = autoRegisterAttribute
                .ResgistrationHolder
                .GetProperty(autoRegisterAttribute.RegistrationName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            try
            {
                _registrationHolderProperty.SetValue(
                    null,
                    _instance
                );

#if !PRODUCTION_BUILD
                Debug.Log(
                    $"Auto-registering singleton {typeof(T).Name} " +
                    $"to {autoRegisterAttribute.ResgistrationHolder.Name}.{autoRegisterAttribute.RegistrationName}."
                );
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Failed to auto-register singleton {typeof(T).Name} " +
                    $"to {autoRegisterAttribute.ResgistrationHolder.Name}.{autoRegisterAttribute.RegistrationName}.\n" +
                    $"Exception: {ex}"
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

#if !PRODUCTION_BUILD
                Debug.Log(
                    $"Un-registering singleton {typeof(T).Name} " +
                    $"from {_registrationHolderProperty.DeclaringType.Name}.{_registrationHolderProperty.Name}."
                );
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Failed to un-register singleton {typeof(T).Name} " +
                    $"from {_registrationHolderProperty.DeclaringType.Name}.{_registrationHolderProperty.Name}.\n" +
                    $"Exception: {ex}"
                );
            }
        }

    }

}
