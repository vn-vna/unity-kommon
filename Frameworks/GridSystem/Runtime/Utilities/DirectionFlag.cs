using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    [Flags]
    public enum DirectionFlag
    {
        None = 0x0,

        North = 0x1,
        South = 0x2,
        East = 0x4,
        West = 0x8,

        NorthWest = 0x10,
        NorthEast = 0x20,
        SouthWest = 0x40,
        SouthEast = 0x80,

        Centre = 0x100,

        Up = North,
        Down = South,
        Left = West,
        Right = East,

        Horizontal = West | East,
        Vertical = North | South,
        Cardinal = Horizontal | Vertical,
        DiagonalNorth = NorthWest | NorthEast,
        DiagonalSouth = SouthWest | SouthEast,
        DiagonalWest = NorthWest | SouthWest,
        DiagonalEast = NorthEast | SouthEast,
        DiagonalForward = NorthEast | SouthWest,
        DiagonalBackward = NorthWest | SouthEast,
        Diagonal = NorthEast | NorthWest | SouthEast | SouthWest,
        NorthWestArc = NorthWest | North | West,
        NorthEastArc = NorthEast | North | East,
        SouthWestArc = SouthWest | South | West,
        SouthEastArc = SouthEast | South | East,
        CardinalNorthWest = North | West,
        CardinalNorthEast = North | East,
        CardinalSouthWest = South | West,
        CardinalSouthEast = South | East,
        All = Cardinal | Diagonal,
        Full = All | Centre
    }

    public static class DirectionFlagHelper
    {
        /// <summary>
        /// The eight single-cell neighbor directions (cardinals + diagonals), in
        /// the same order the legacy lookup dictionary used to iterate. Useful for
        /// hot loops that must evaluate every neighbor (e.g. movement checks).
        /// </summary>
        public static readonly DirectionFlag[] AllCellDirections =
        {
            DirectionFlag.North,
            DirectionFlag.South,
            DirectionFlag.West,
            DirectionFlag.East,
            DirectionFlag.NorthWest,
            DirectionFlag.NorthEast,
            DirectionFlag.SouthWest,
            DirectionFlag.SouthEast
        };

        public static DirectionFlag GetFullArcDirection(this DirectionFlag direction)
        {
            switch (direction)
            {
                case DirectionFlag.NorthWest: return DirectionFlag.NorthWestArc;
                case DirectionFlag.NorthEast: return DirectionFlag.NorthEastArc;
                case DirectionFlag.SouthWest: return DirectionFlag.SouthWestArc;
                case DirectionFlag.SouthEast: return DirectionFlag.SouthEastArc;
                default: return direction;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static GridCoord ToGridCoord(this DirectionFlag direction)
        {
            switch (direction)
            {
                case DirectionFlag.None:      return GridCoord.zero;
                case DirectionFlag.North:     return GridCoord.up;
                case DirectionFlag.South:     return GridCoord.down;
                case DirectionFlag.West:      return GridCoord.left;
                case DirectionFlag.East:      return GridCoord.right;
                case DirectionFlag.NorthWest: return new GridCoord(-1, 1);
                case DirectionFlag.NorthEast: return new GridCoord(1, 1);
                case DirectionFlag.SouthWest: return new GridCoord(-1, -1);
                case DirectionFlag.SouthEast: return new GridCoord(1, -1);
                default: throw new ArgumentException($"Invalid direction flag: {direction}");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToVector2(this DirectionFlag direction)
        {
            switch (direction)
            {
                case DirectionFlag.None:      return Vector2.zero;
                case DirectionFlag.North:     return Vector2.up;
                case DirectionFlag.South:     return Vector2.down;
                case DirectionFlag.West:      return Vector2.left;
                case DirectionFlag.East:      return Vector2.right;
                case DirectionFlag.NorthWest: return new Vector2(-1f, 1f);
                case DirectionFlag.NorthEast: return new Vector2(1f, 1f);
                case DirectionFlag.SouthWest: return new Vector2(-1f, -1f);
                case DirectionFlag.SouthEast: return new Vector2(1f, -1f);
                default: throw new ArgumentException($"Invalid direction flag: {direction}");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag<T>(T x, T y)
            where T : IComparable<T>
        {
            T zero = default;
            int cx = x.CompareTo(zero);
            int cy = y.CompareTo(zero);

            DirectionFlag result = DirectionFlag.None;

            if (cx > 0) result |= DirectionFlag.East;
            else if (cx < 0) result |= DirectionFlag.West;

            if (cy > 0) result |= DirectionFlag.North;
            else if (cy < 0) result |= DirectionFlag.South;

            if (cx > 0)
            {
                if (cy > 0) result |= DirectionFlag.NorthEast;
                else if (cy < 0) result |= DirectionFlag.SouthEast;
            }
            else if (cx < 0)
            {
                if (cy > 0) result |= DirectionFlag.NorthWest;
                else if (cy < 0) result |= DirectionFlag.SouthWest;
            }

            return result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(this GridCoord dir)
        {
            return ToDirectionFlag(dir.x, dir.y);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(this Vector2 dir)
        {
            return ToDirectionFlag(dir.x, dir.y);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(this Vector3 dir)
        {
            return ToDirectionFlag(dir.x, dir.y); // grid-plane projection (drops z)
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(int x, int y)
        {
            DirectionFlag result = DirectionFlag.None;

            if (x > 0) result |= DirectionFlag.East;
            else if (x < 0) result |= DirectionFlag.West;

            if (y > 0) result |= DirectionFlag.North;
            else if (y < 0) result |= DirectionFlag.South;

            if (x > 0)
            {
                if (y > 0) result |= DirectionFlag.NorthEast;
                else if (y < 0) result |= DirectionFlag.SouthEast;
            }
            else if (x < 0)
            {
                if (y > 0) result |= DirectionFlag.NorthWest;
                else if (y < 0) result |= DirectionFlag.SouthWest;
            }

            return result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToVector3(this DirectionFlag direction)
        {
            GridCoord c = direction.ToGridCoord();
            return new Vector3(c.x, c.y, 0f);
        }

        // --- Classification (membership tests) ---

        public static bool IsCardinal(this DirectionFlag direction)
            => (direction & DirectionFlag.Cardinal) != 0;

        public static bool IsDiagonal(this DirectionFlag direction)
            => (direction & DirectionFlag.Diagonal) != 0;

        public static bool IsHorizontal(this DirectionFlag direction)
            => (direction & DirectionFlag.Horizontal) != 0;

        public static bool IsVertical(this DirectionFlag direction)
            => (direction & DirectionFlag.Vertical) != 0;

        // --- Rotation (45-degree steps) ---

        public static DirectionFlag RotateClockwise(this DirectionFlag direction)
        {
            switch (direction)
            {
                case DirectionFlag.North:     return DirectionFlag.NorthEast;
                case DirectionFlag.NorthEast: return DirectionFlag.East;
                case DirectionFlag.East:      return DirectionFlag.SouthEast;
                case DirectionFlag.SouthEast: return DirectionFlag.South;
                case DirectionFlag.South:     return DirectionFlag.SouthWest;
                case DirectionFlag.SouthWest: return DirectionFlag.West;
                case DirectionFlag.West:      return DirectionFlag.NorthWest;
                case DirectionFlag.NorthWest: return DirectionFlag.North;
                default: throw new ArgumentException($"Invalid direction flag: {direction}");
            }
        }

        public static DirectionFlag RotateCounterClockwise(this DirectionFlag direction)
        {
            switch (direction)
            {
                case DirectionFlag.North:     return DirectionFlag.NorthWest;
                case DirectionFlag.NorthWest: return DirectionFlag.West;
                case DirectionFlag.West:      return DirectionFlag.SouthWest;
                case DirectionFlag.SouthWest: return DirectionFlag.South;
                case DirectionFlag.South:     return DirectionFlag.SouthEast;
                case DirectionFlag.SouthEast: return DirectionFlag.East;
                case DirectionFlag.East:      return DirectionFlag.NorthEast;
                case DirectionFlag.NorthEast: return DirectionFlag.North;
                default: throw new ArgumentException($"Invalid direction flag: {direction}");
            }
        }

        public static DirectionFlag Rotate90Clockwise(this DirectionFlag direction)
            => RotateClockwise(RotateClockwise(direction));

        public static DirectionFlag Rotate90CounterClockwise(this DirectionFlag direction)
            => RotateCounterClockwise(RotateCounterClockwise(direction));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag GetOppositeDirection(this DirectionFlag direction)
        {
            DirectionFlag flag = DirectionFlag.None;
            if (direction.HasFlag(DirectionFlag.North)) flag |= DirectionFlag.South;
            if (direction.HasFlag(DirectionFlag.South)) flag |= DirectionFlag.North;
            if (direction.HasFlag(DirectionFlag.East)) flag |= DirectionFlag.West;
            if (direction.HasFlag(DirectionFlag.West)) flag |= DirectionFlag.East;
            if (direction.HasFlag(DirectionFlag.NorthEast)) flag |= DirectionFlag.SouthWest;
            if (direction.HasFlag(DirectionFlag.NorthWest)) flag |= DirectionFlag.SouthEast;
            if (direction.HasFlag(DirectionFlag.SouthEast)) flag |= DirectionFlag.NorthWest;
            if (direction.HasFlag(DirectionFlag.SouthWest)) flag |= DirectionFlag.NorthEast;
            return flag;
        }
    }
}
