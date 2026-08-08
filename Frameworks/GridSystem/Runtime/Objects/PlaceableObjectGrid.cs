using System;
using Com.Hapiga.Scheherazade.Common.Extensions;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    [Serializable]
    public class PlaceableObjectGrid
    {
        public const int GridCellEmptySentinelValue = -1;
        public const int MaxGridWidth = int.MaxValue;
        public const int MaxGridHeight = int.MaxValue;

        public Vector2Int Size
        {
            get => new Vector2Int(_width, _height);
            set => Resize(value);
        }

        public int this[int x, int y]
        {
            get => _occupiedCells[x + y * _width];
            set => _occupiedCells[x + y * _width] = value;
        }

        public int this[Vector2Int position]
        {
            get => this[position.x, position.y];
            set => this[position.x, position.y] = value;
        }

        public int[] FlatData => _occupiedCells;
        public bool[] OccupationData => GetOccupationData();

        [SerializeField]
        private int _width = 1;

        [SerializeField]
        private int _height = 1;

        [SerializeField]
        private int[] _occupiedCells;

        [NonSerialized]
        private bool[] _occupationDataCache;

        public PlaceableObjectGrid(Vector2Int size)
            : this(size.x, size.y)
        { }

        public PlaceableObjectGrid(int width = 1, int height = 1)
        {
            _width = Mathf.Clamp(width, 1, MaxGridWidth);
            _height = Mathf.Clamp(height, 1, MaxGridHeight);
            _occupiedCells = new int[_width * _height];
            _occupationDataCache = null;
        }

        public void SetGridData(int x, int y, int value)
        {
            if (x.OutRange(0, _width - 1) || y.OutRange(0, _height - 1))
            {
                return;
            }
            _occupiedCells[x + y * _width] = value;
            _occupationDataCache = null;
        }

        public void SetGridData(int[] gridData)
        {
            if (gridData == null || gridData.Length != _width * _height)
            {
                return;
            }

            _occupiedCells = new int[_width * _height];
            for (int i = 0; i < gridData.Length; i++)
            {
                _occupiedCells[i] = gridData[i];
            }

            _occupationDataCache = null;
        }

        public void Resize(Vector2Int newSize)
        {
            Resize(newSize.x, newSize.y);
        }

        public void Resize(int newWidth, int newHeight)
        {
            newWidth = Mathf.Clamp(newWidth, 1, MaxGridWidth);
            newHeight = Mathf.Clamp(newHeight, 1, MaxGridHeight);

            var newCells = new int[newWidth * newHeight];

            int copyWidth = Mathf.Min(_width, newWidth);
            int copyHeight = Mathf.Min(_height, newHeight);

            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    newCells[x + y * newWidth] = this[x, y];
                }
            }

            _occupiedCells = newCells;
            _occupationDataCache = null;
            _width = newWidth;
            _height = newHeight;
        }

        public void FillWith(int value)
        {
            for (int i = 0; i < _occupiedCells.Length; i++)
            {
                _occupiedCells[i] = value;
            }
            _occupationDataCache = null;
        }

        public bool CheckBounds(int x, int y)
        {
            return x >= 0 && x < _width && y >= 0 && y < _height;
        }

        private bool[] GetOccupationData()
        {
            if (_occupationDataCache == null || _occupationDataCache.Length != _occupiedCells.Length)
            {
                _occupationDataCache = new bool[_occupiedCells.Length];
            }

            for (int i = 0; i < _occupiedCells.Length; i++)
            {
                _occupationDataCache[i] = _occupiedCells[i] != GridCellEmptySentinelValue;
            }

            return _occupationDataCache;
        }
    }
}
