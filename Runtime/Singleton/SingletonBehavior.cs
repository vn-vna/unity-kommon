using System;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{

    public class DontDestroyOnLoadAttribute : Attribute
    {
        public DontDestroyOnLoadAttribute()
        { }
    }

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

            if (_registrationHolderProperty == null)
            {
                Debug.LogError(
                    $"Auto-registration failed for singleton {typeof(T).Name}. " +
                    $"Property {autoRegisterAttribute.RegistrationName} not found on type {autoRegisterAttribute.ResgistrationHolder.Name}."
                );
                return;
            }

            _registrationHolderProperty.SetValue(
                null,
                _instance
            );

            Debug.Log(
                $"Auto-registering singleton {typeof(T).Name} " +
                $"to {autoRegisterAttribute.ResgistrationHolder.Name}.{autoRegisterAttribute.RegistrationName}."
            );

        }

        protected virtual void OnDestroy()
        {
            Debug.Log($"Destroying singleton instance of {typeof(T).Name}.");

            if (_instance != null && _instance == this)
            {
                _instance = null;
                Unregister();
            }
        }

        private void Unregister()
        {
            if (_registrationHolderProperty == null)
            {
                return;
            }

            _registrationHolderProperty.SetValue(null, null);

            Debug.Log(
                $"Un-registering singleton {typeof(T).Name} " +
                $"from {_registrationHolderProperty.DeclaringType.Name}.{_registrationHolderProperty.Name}."
            );
        }

    }

}
