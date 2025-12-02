using System;

namespace Com.Hapiga.Scheherazade.Common.MBA
{
    public class TinyGridData
    {
        private int _width;
        private int _height;
        private MagicBooleanArray _data;

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

        public long RawData => _data.Data;

        public TinyGridData(int width = 1, int height = 1)
        {
            Width = width;
            Height = height;
            _data = new MagicBooleanArray(_width * _height);
        }

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

        public void Clear()
        {
            _data.Clear();
        }

        public void SetAll(bool value)
        {
            _data.SetAll(value);
        }

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