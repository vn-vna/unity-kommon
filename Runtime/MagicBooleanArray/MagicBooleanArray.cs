using System;

namespace Com.Hapiga.Scheherazade.Common.MBA
{
    /// <summary>
    /// A memory-efficient boolean array that stores up to 58 boolean values in a single 64-bit integer.
    /// </summary>
    /// <remarks>
    /// This struct uses bit manipulation to pack boolean values densely. The first 6 bits store the length (0-63),
    /// and the remaining 58 bits store the actual boolean values. This is highly memory-efficient for small
    /// boolean arrays and supports serialization as a single long value.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create an array of 10 booleans
    /// var arr = new MagicBooleanArray(10);
    /// 
    /// // Set some values
    /// arr[0] = true;
    /// arr[5] = true;
    /// arr[9] = false;
    /// 
    /// // Convert to string representation
    /// Debug.Log(arr.ToString()); // "1000010000"
    /// 
    /// // Create from string
    /// var arr2 = MagicBooleanArray.FromString("10101");
    /// 
    /// // Save/load raw data
    /// long savedData = arr.Data;
    /// var loadedArr = MagicBooleanArray.FromRawData(savedData);
    /// </code>
    /// </example>
    public struct MagicBooleanArray
    {
        private const long LengthMask = 0x3FL << 58; // 6 bits for length
        private const long DataMask = 0x3FFFFFFFFFFFFFFFL; // remaining 58 bits for data
        private long _data;
        
        /// <summary>
        /// Gets the raw 64-bit integer containing both length and data.
        /// </summary>
        public readonly long Data => _data;

        /// <summary>
        /// Gets or sets the length of the boolean array (0-63).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not between 0 and 63.</exception>
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

        /// <summary>
        /// Gets or sets the boolean value at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the boolean value.</param>
        /// <returns>The boolean value at the specified index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
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

        /// <summary>
        /// Initializes a new MagicBooleanArray with the specified length.
        /// </summary>
        /// <param name="length">The length of the array (0-63).</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when length is not between 0 and 63.</exception>
        public MagicBooleanArray(int length)
        {
            if (length < 0 || length > 63)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be between 0 and 63.");
            _data = ((long)length << 58) & LengthMask;
        }

        /// <summary>
        /// Clears all boolean values to false while preserving the length.
        /// </summary>
        public void Clear()
        {
            _data &= LengthMask; // Reset data bits, keep length
        }

        /// <summary>
        /// Sets all boolean values to the specified value.
        /// </summary>
        /// <param name="value">The value to set all booleans to.</param>
        public void SetAll(bool value)
        {
            if (value)
                _data |= DataMask; // Set all bits to 1
            else
                _data &= LengthMask; // Clear all bits, keep length
        }

        /// <summary>
        /// Returns a string representation of the boolean array using '1' and '0' characters.
        /// </summary>
        /// <returns>A string of '1' and '0' characters representing the array.</returns>
        public override string ToString()
        {
            char[] chars = new char[Length];
            for (int i = 0; i < Length; i++)
            {
                chars[i] = this[i] ? '1' : '0';
            }
            return new string(chars);
        }

        /// <summary>
        /// Creates a MagicBooleanArray from a string of '1' and '0' characters.
        /// </summary>
        /// <param name="str">The string to convert (max 63 characters).</param>
        /// <returns>A new MagicBooleanArray.</returns>
        /// <exception cref="ArgumentNullException">Thrown when str is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when str length exceeds 63.</exception>
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

        /// <summary>
        /// Creates a MagicBooleanArray from raw 64-bit data.
        /// </summary>
        /// <param name="data">The raw data containing length and boolean values.</param>
        /// <returns>A new MagicBooleanArray.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the data contains an invalid length.</exception>
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