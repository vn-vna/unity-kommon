using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Integer coordinate used across the Grid System (cell position, size,
    /// offset, direction). Replaces UnityEngine.Vector2Int inside the grid
    /// layer so the framework has no dependency on Unity vector math for its
    /// core logic. Field names (<c>x</c>, <c>y</c>) intentionally match
    /// <see cref="UnityEngine.Vector2Int"/> so serialized assets keep their
    /// values when upgrading an existing project.
    /// </summary>
    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>
    {
        #region Fields
        public int x;
        public int y;
        #endregion

        #region Constructors
        public GridCoord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        #endregion

        #region Constants
        public static GridCoord zero => new GridCoord(0, 0);
        public static GridCoord one => new GridCoord(1, 1);
        public static GridCoord up => new GridCoord(0, 1);
        public static GridCoord down => new GridCoord(0, -1);
        public static GridCoord left => new GridCoord(-1, 0);
        public static GridCoord right => new GridCoord(1, 0);
        #endregion

        #region Operators
        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.x + b.x, a.y + b.y);
        public static GridCoord operator -(GridCoord a, GridCoord b) => new GridCoord(a.x - b.x, a.y - b.y);
        public static GridCoord operator -(GridCoord a) => new GridCoord(-a.x, -a.y);
        public static GridCoord operator *(GridCoord a, int scalar) => new GridCoord(a.x * scalar, a.y * scalar);
        public static GridCoord operator *(int scalar, GridCoord a) => new GridCoord(a.x * scalar, a.y * scalar);
        public static GridCoord operator /(GridCoord a, int divisor) => new GridCoord(a.x / divisor, a.y / divisor);

        public static bool operator ==(GridCoord a, GridCoord b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(GridCoord a, GridCoord b) => !(a == b);
        #endregion

        #region Conversions

        // --- Integer types (lossless) ---

        public static explicit operator Vector2Int(GridCoord c) => new Vector2Int(c.x, c.y);
        public static explicit operator GridCoord(Vector2Int v) => new GridCoord(v.x, v.y);

        public static explicit operator Vector3Int(GridCoord c) => new Vector3Int(c.x, c.y, 0);
        public static explicit operator GridCoord(Vector3Int v) => new GridCoord(v.x, v.y);

        // --- Float types (exact toward world space, round-to-nearest back) ---

        public static explicit operator Vector2(GridCoord c) => new Vector2(c.x, c.y);
        public static explicit operator GridCoord(Vector2 v) => new GridCoord(
            Mathf.RoundToInt(v.x),
            Mathf.RoundToInt(v.y)
        );

        public static explicit operator Vector3(GridCoord c) => new Vector3(c.x, c.y, 0f);
        public static explicit operator GridCoord(Vector3 v) => new GridCoord(
            Mathf.RoundToInt(v.x),
            Mathf.RoundToInt(v.y)
        );

        public static explicit operator Vector4(GridCoord c) => new Vector4(c.x, c.y, 0f, 0f);
        public static explicit operator GridCoord(Vector4 v) => new GridCoord(
            Mathf.RoundToInt(v.x),
            Mathf.RoundToInt(v.y)
        );

        #endregion

        #region Equality
        public bool Equals(GridCoord other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => unchecked((x * 397) ^ y);
        public override string ToString() => $"({x}, {y})";
        #endregion
    }
}
