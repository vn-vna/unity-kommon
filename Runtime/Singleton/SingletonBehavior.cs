using System.Reflection;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{

    public class SingletonAttribute : System.Attribute
    {
        public bool DontDestroyOnLoad { get; set; }
    }

    public class SingletonBehavior<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        public static T Instance
        {
            get => _instance;
        }

        private static T _instance;

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = (T)(object)this;

                SingletonAttribute singletonAttribute = GetType().GetCustomAttribute(typeof(SingletonAttribute), false) as SingletonAttribute;

                if (singletonAttribute != null && singletonAttribute.DontDestroyOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else
            {
                Debug.LogWarning($"An instance of {typeof(T).Name} already exists. Destroying duplicate.");
                Destroy(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_instance != null && _instance == this)
            {
                _instance = null;
            }
        }
    }

}
