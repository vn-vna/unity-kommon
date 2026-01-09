using System.Reflection;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Singleton
{
    /// <summary>
    /// Attribute that configures singleton behavior for MonoBehaviour classes.
    /// </summary>
    /// <remarks>
    /// Apply this attribute to classes that inherit from SingletonBehavior to customize their persistence behavior.
    /// </remarks>
    /// <example>
    /// <code>
    /// [Singleton(DontDestroyOnLoad = true)]
    /// public class GameManager : SingletonBehavior&lt;GameManager&gt;
    /// {
    ///     // This singleton will persist across scene loads
    /// }
    /// </code>
    /// </example>
    public class SingletonAttribute : System.Attribute
    {
        /// <summary>
        /// Gets or sets whether the singleton GameObject should persist across scene loads.
        /// </summary>
        public bool DontDestroyOnLoad { get; set; }
    }

    /// <summary>
    /// Base class for implementing the singleton pattern with MonoBehaviour components.
    /// </summary>
    /// <typeparam name="T">The type of the singleton class, which must inherit from MonoBehaviour.</typeparam>
    /// <remarks>
    /// This class ensures that only one instance of the component exists in the scene.
    /// If multiple instances are detected, duplicates are automatically destroyed.
    /// Use the SingletonAttribute to configure persistence behavior.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class AudioManager : SingletonBehavior&lt;AudioManager&gt;
    /// {
    ///     public void PlaySound(AudioClip clip)
    ///     {
    ///         // Implementation
    ///     }
    /// }
    /// 
    /// // Usage from other scripts:
    /// AudioManager.Instance.PlaySound(myClip);
    /// </code>
    /// </example>
    public class SingletonBehavior<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        /// <summary>
        /// Gets the singleton instance of this component.
        /// </summary>
        public static T Instance
        {
            get => _instance;
        }

        private static T _instance;

        /// <summary>
        /// Initializes the singleton instance or destroys duplicate instances.
        /// </summary>
        /// <remarks>
        /// Override this method in derived classes to add custom initialization logic.
        /// Always call base.Awake() when overriding.
        /// </remarks>
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

        /// <summary>
        /// Clears the singleton instance reference when this object is destroyed.
        /// </summary>
        /// <remarks>
        /// Override this method in derived classes to add custom cleanup logic.
        /// Always call base.OnDestroy() when overriding.
        /// </remarks>
        protected virtual void OnDestroy()
        {
            if (_instance != null && _instance == this)
            {
                _instance = null;
            }
        }
    }

}
