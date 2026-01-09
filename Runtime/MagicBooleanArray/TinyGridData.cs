using System;

namespace Com.Hapiga.Scheherazade.Common.MBA
{
    /// <summary>
    /// Represents a small 2D grid of boolean values using the MagicBooleanArray for memory-efficient storage.
    /// </summary>
    /// <remarks>
    /// This class stores a 2D boolean grid (up to 8x8) in a single 64-bit integer using MagicBooleanArray.
    /// It's useful for small grid-based data like tile states, visibility maps, or simple collision grids.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a 4x4 grid
    /// var grid = new TinyGridData(4, 4);
    /// 
    /// // Set some values
    /// grid[0, 0] = true;
    /// grid[2, 3] = true;
    /// 
    /// // Clear all values
    /// grid.Clear();
    /// 
    /// // Set all to true
    /// grid.SetAll(true);
    /// 
    /// // Save and load raw data
    /// long savedData = grid.RawData;
    /// var loadedGrid = TinyGridData.FromRawData(savedData, 4, 4);
    /// </code>
    /// </example>
    public class TinyGridData
    {
        private int _width;
        private int _height;
        private MagicBooleanArray _data;

        /// <summary>
        /// Gets or sets the width of the grid (1-64).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not between 1 and 64.</exception>
        public int Width
        {
            get => _width;
            set
            {
                if (value < 1 || value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value), "Width must be between 1 and 64.");
                _width = value;
                _data.Length = _width * _height;
            }
        }

        /// <summary>
        /// Gets or sets the height of the grid (1-64).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not between 1 and 64.</exception>
        public int Height
        {
            get => _height;
            set
            {
                if (value < 1 || value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value), "Height must be between 1 and 64.");
                _height = value;
                _data.Length = _width * _height;
            }
        }

        /// <summary>
        /// Gets the raw 64-bit data representing the entire grid.
        /// </summary>
        public long RawData => _data.Data;

        /// <summary>
        /// Initializes a new TinyGridData with the specified dimensions.
        /// </summary>
        /// <param name="width">The width of the grid (default: 1).</param>
        /// <param name="height">The height of the grid (default: 1).</param>
        public TinyGridData(int width = 1, int height = 1)
        {
            Width = width;
            Height = height;
            _data = new MagicBooleanArray(_width * _height);
        }

        /// <summary>
        /// Gets or sets the boolean value at the specified coordinates.
        /// </summary>
        /// <param name="x">The x coordinate (0 to Width-1).</param>
        /// <param name="y">The y coordinate (0 to Height-1).</param>
        /// <returns>The boolean value at the specified coordinates.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when coordinates are out of bounds.</exception>
        public bool this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= _width || y < 0 || y >= _height)
                    throw new ArgumentOutOfRangeException("Coordinates are out of bounds.");
                return _data[x + y * _width];
            }
            set
            {
                if (x < 0 || x >= _width || y < 0 || y >= _height)
                    throw new ArgumentOutOfRangeException("Coordinates are out of bounds.");
                _data[x + y * _width] = value;
            }
        }

        /// <summary>
        /// Clears all grid values to false.
        /// </summary>
        public void Clear()
        {
            _data.Clear();
        }

        /// <summary>
        /// Sets all grid values to the specified value.
        /// </summary>
        /// <param name="value">The value to set all grid cells to.</param>
        public void SetAll(bool value)
        {
            _data.SetAll(value);
        }

        /// <summary>
        /// Returns a string representation of the grid using '1' and '0' characters.
        /// </summary>
        /// <returns>A string representing the grid data row by row.</returns>
        public override string ToString()
        {
            char[] chars = new char[_width * _height];
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    chars[x + y * _width] = this[x, y] ? '1' : '0';
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Creates a TinyGridData from a string of '1' and '0' characters.
        /// </summary>
        /// <param name="data">The string to convert (length must be a perfect square).</param>
        /// <returns>A new TinyGridData.</returns>
        /// <exception cref="ArgumentException">Thrown when data is null, empty, or length is not a perfect square.</exception>
        public static TinyGridData FromString(string data)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("Data cannot be null or empty.", nameof(data));

            int width = (int)Math.Sqrt(data.Length);
            int height = width;

            if (width * height != data.Length)
                throw new ArgumentException("Data length must be a perfect square.", nameof(data));

            TinyGridData grid = new TinyGridData(width, height);
            for (int i = 0; i < data.Length; i++)
            {
                grid[i % width, i / width] = data[i] == '1';
            }
            return grid;
        }

        /// <summary>
        /// Creates a TinyGridData from raw 64-bit data.
        /// </summary>
        /// <param name="rawData">The raw data to load.</param>
        /// <param name="width">The width of the grid (1-64).</param>
        /// <param name="height">The height of the grid (1-64).</param>
        /// <returns>A new TinyGridData.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when width or height are not between 1 and 64.</exception>
        public static TinyGridData FromRawData(long rawData, int width, int height)
        {
            if (width < 1 || width > 64 || height < 1 || height > 64)
                throw new ArgumentOutOfRangeException("Width and height must be between 1 and 64.");

            TinyGridData grid = new TinyGridData(width, height);
            grid._data = MagicBooleanArray.FromRawData(rawData);
            return grid;
        }
    }
}