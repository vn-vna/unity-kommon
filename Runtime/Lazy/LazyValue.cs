using System;

namespace Com.Hapiga.Scheherazade.Common.Lazy
{
    public class LazyValue<T>
    {
        #region Constructors

        public LazyValue(Func<T> factory)
        {
            _factory = factory;
        }

        #endregion

        #region Interfaces

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

        public event Action<T> OnValueInitialized;

        #endregion

        #region Private Fields

        private T _value;
        private readonly Func<T> _factory;

        #endregion
    }
}