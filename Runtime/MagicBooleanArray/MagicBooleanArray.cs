using System;

namespace Com.Hapiga.Scheherazade.Common.MBA
{
    public struct MagicBooleanArray
    {
        private const long LengthMask = 0x3FL << 58; // 6 bits for length
        private const long DataMask = 0x3FFFFFFFFFFFFFFFL; // remaining 58 bits for data
        private long _data;
        public readonly long Data => _data;

        public int Length
        {
            get => (int)((_data & LengthMask) >> 58);
            set
            {
                if (value < 0 || value > 63)
                    throw new ArgumentOutOfRangeException(nameof(value), "Length must be between 0 and 63.");
                _data = (_data & DataMask) | ((long)value << 58);
            }
        }

        public bool this[int index]
        {
            get
            {
                if (index < 0 || index >= Length)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
                return (_data & (1L << index)) != 0;
            }
            set
            {
                if (index < 0 || index >= Length)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
                if (value)
                    _data |= (1L << index);
                else
                    _data &= ~(1L << index);
            }
        }

        public MagicBooleanArray(int length)
        {
            if (length < 0 || length > 63)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be between 0 and 63.");
            _data = ((long)length << 58) & LengthMask;
        }

        public void Clear()
        {
            _data &= LengthMask; // Reset data bits, keep length
        }

        public void SetAll(bool value)
        {
            if (value)
                _data |= DataMask; // Set all bits to 1
            else
                _data &= LengthMask; // Clear all bits, keep length
        }

        public override string ToString()
        {
            char[] chars = new char[Length];
            for (int i = 0; i < Length; i++)
            {
                chars[i] = this[i] ? '1' : '0';
            }
            return new string(chars);
        }

        public static MagicBooleanArray FromString(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            if (str.Length > 63) throw new ArgumentOutOfRangeException(nameof(str), "String length must not exceed 63 characters.");

            MagicBooleanArray mba = new MagicBooleanArray(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                mba[i] = str[i] == '1';
            }
            return mba;
        }

        public static MagicBooleanArray FromRawData(long data)
        {
            MagicBooleanArray mba = new MagicBooleanArray();
            mba._data = data;
            int length = (int)((data & LengthMask) >> 58);
            if (length < 0 || length > 57)
                throw new ArgumentOutOfRangeException(nameof(data), "Invalid raw data length.");
            return mba;
        }

    }
}