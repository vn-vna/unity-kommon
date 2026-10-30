#if PLA_CREATIVE_BUILD
using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Compatibility
{
    /// <summary>
    /// Runtime value rectangle for Luna, whose UnityEngine.RectInt is a reporting stub.
    /// Authored data stays in its existing int[4] format. Native/Editor callsites retain
    /// Unity RectInt; this type mirrors its raw storage and normalized boundary semantics.
    /// </summary>
    public struct CreativeRectInt : IEquatable<CreativeRectInt>
    {
        private int _x;
        private int _y;
        private int _width;
        private int _height;

        public CreativeRectInt(int x, int y, int width, int height)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        public int x { get => _x; set => _x = value; }
        public int y { get => _y; set => _y = value; }
        public int width { get => _width; set => _width = value; }
        public int height { get => _height; set => _height = value; }
        public Vector2 center => new Vector2(_x + _width * 0.5f, _y + _height * 0.5f);

        public int xMin
        {
            get { int end = unchecked(_x + _width); return _x < end ? _x : end; }
            set { int previousMax = xMax; _x = value; _width = unchecked(previousMax - _x); }
        }

        public int yMin
        {
            get { int end = unchecked(_y + _height); return _y < end ? _y : end; }
            set { int previousMax = yMax; _y = value; _height = unchecked(previousMax - _y); }
        }

        public int xMax
        {
            get { int end = unchecked(_x + _width); return _x > end ? _x : end; }
            set => _width = unchecked(value - _x);
        }

        public int yMax
        {
            get { int end = unchecked(_y + _height); return _y > end ? _y : end; }
            set => _height = unchecked(value - _y);
        }

        public bool Contains(int pointX, int pointY)
            => pointX >= xMin && pointX < xMax && pointY >= yMin && pointY < yMax;

        public bool Overlaps(CreativeRectInt other)
            => other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin;

        public bool Equals(CreativeRectInt other)
            => _x == other._x && _y == other._y && _width == other._width && _height == other._height;

        public override bool Equals(object other) => other is CreativeRectInt rectangle && Equals(rectangle);

        public override int GetHashCode()
            => _x ^ (_y << 4) ^ (_y >> 28) ^ (_width >> 4) ^ (_width << 28) ^ (_height >> 4) ^ (_height << 28);

        public static bool operator ==(CreativeRectInt left, CreativeRectInt right) => left.Equals(right);
        public static bool operator !=(CreativeRectInt left, CreativeRectInt right) => !left.Equals(right);
    }
}
#endif
