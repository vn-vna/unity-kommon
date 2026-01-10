using System;

namespace Com.Hapiga.Scheherazade.Common.Lazy
{
    /// <summary>
    /// Provides lazy initialization of a value using a factory function.
    /// </summary>
    /// <typeparam name="T">The type of the value to be lazily initialized.</typeparam>
    /// <remarks>
    /// The value is created only when first accessed via the Value property. 
    /// The factory function is called once, and the result is cached for subsequent accesses.
    /// An event is raised when the value is first initialized.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a lazy-loaded expensive object
    /// var lazyData = new LazyValue&lt;ExpensiveData&gt;(() => new ExpensiveData());
    /// 
    /// // Subscribe to initialization event
    /// lazyData.OnValueInitialized += (data) => Debug.Log("Data initialized!");
    /// 
    /// // Value is created on first access
    /// var data = lazyData.Value;
    /// 
    /// // Subsequent accesses return the cached value
    /// var sameDat = lazyData.Value;
    /// </code>
    /// </example>
    public class LazyValue<T>
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the LazyValue class with the specified factory function.
        /// </summary>
        /// <param name="factory">The function to create the value when first accessed.</param>
        public LazyValue(Func<T> factory)
        {
            _factory = factory;
        }

        #endregion

        #region Interfaces

        /// <summary>
        /// Gets the lazily initialized value, creating it on first access.
        /// </summary>
        /// <remarks>
        /// The first access to this property will invoke the factory function and cache the result.
        /// Subsequent accesses return the cached value.
        /// </remarks>
        public T Value
        {
            get
            {
                if (_value == null)
                {
                    _value = _factory();
                    OnValueInitialized?.Invoke(_value);
                }

                return _value;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Event raised when the value is first initialized.
        /// </summary>
        public event Action<T> OnValueInitialized;

        #endregion

        #region Private Fields

        private T _value;
        private readonly Func<T> _factory;

        #endregion
    }
}