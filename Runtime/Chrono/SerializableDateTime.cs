using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [Serializable]
    public struct SerializableDateTime : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private long _ticks;
        [SerializeField, HideInInspector] private int _kindValue;

        [NonSerialized] private DateTime _value;

        public DateTime Value => _value;

        public DateTimeKind Kind => _value.Kind;

        public SerializableDateTime(DateTime value)
        {
            _value = value;
            _ticks = 0;
            _kindValue = 0;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            _ticks = _value.Ticks;
            _kindValue = (int)_value.Kind;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _value = new DateTime(_ticks, (DateTimeKind)_kindValue);
        }

        public static implicit operator DateTime(SerializableDateTime sdt)
        {
            return sdt._value;
        }

        public static implicit operator SerializableDateTime(DateTime dt)
        {
            return new SerializableDateTime(dt);
        }

        public override string ToString()
        {
            return _value.ToString("O");
        }

        public override bool Equals(object obj)
        {
            return obj is SerializableDateTime other && _value.Equals(other._value);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public static bool operator ==(SerializableDateTime left, SerializableDateTime right)
        {
            return left._value == right._value;
        }

        public static bool operator !=(SerializableDateTime left, SerializableDateTime right)
        {
            return left._value != right._value;
        }
    }
}
