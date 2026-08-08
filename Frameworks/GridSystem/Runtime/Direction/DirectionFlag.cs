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
        public static Dictionary<DirectionFlag, Vector2Int> D2VInt
            = new Dictionary<DirectionFlag, Vector2Int>
            {
                { DirectionFlag.North,     Vector2Int.up                        },
                { DirectionFlag.South,     Vector2Int.down                      },
                { DirectionFlag.West,      Vector2Int.left                      },
                { DirectionFlag.East,      Vector2Int.right                     },
                { DirectionFlag.NorthWest, Vector2Int.up + Vector2Int.left      },
                { DirectionFlag.NorthEast, Vector2Int.up + Vector2Int.right     },
                { DirectionFlag.SouthWest, Vector2Int.down + Vector2Int.left    },
                { DirectionFlag.SouthEast, Vector2Int.down + Vector2Int.right   },
            };

        public static Dictionary<DirectionFlag, Vector2> D2VFloat
            = new Dictionary<DirectionFlag, Vector2>
            {
                { DirectionFlag.North,     Vector2.up                           },
                { DirectionFlag.South,     Vector2.down                         },
                { DirectionFlag.West,      Vector2.left                         },
                { DirectionFlag.East,      Vector2.right                        },
                { DirectionFlag.NorthWest, Vector2.up + Vector2.left            },
                { DirectionFlag.NorthEast, Vector2.up + Vector2.right           },
                { DirectionFlag.SouthWest, Vector2.down + Vector2.left          },
                { DirectionFlag.SouthEast, Vector2.down + Vector2.right         }
            };

        public static DirectionFlag GetFullArcDirection(this DirectionFlag direction)
        {
            return direction switch
            {
                DirectionFlag.NorthWest => DirectionFlag.NorthWestArc,
                DirectionFlag.NorthEast => DirectionFlag.NorthEastArc,
                DirectionFlag.SouthWest => DirectionFlag.SouthWestArc,
                DirectionFlag.SouthEast => DirectionFlag.SouthEastArc,
                _ => direction
            };
        }

        public static Vector2Int ToVector2Int(this DirectionFlag direction)
        {
            if (direction == DirectionFlag.None)
            {
                return Vector2Int.zero;
            }

            if (D2VInt.TryGetValue(direction, out Vector2Int vector))
            {
                return vector;
            }

            throw new ArgumentException($"Invalid direction flag: {direction}");
        }

        public static Vector2 ToVector2(this DirectionFlag direction)
        {
            if (direction == DirectionFlag.None)
            {
                return Vector2.zero;
            }

            if (D2VFloat.TryGetValue(direction, out Vector2 vector))
            {
                return vector;
            }

            throw new ArgumentException($"Invalid direction flag: {direction}");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag<T>(T x, T y)
            where T : IComparable<T>
        {
            T zero = default;

            var result =
                (x.CompareTo(zero) > 0 ? DirectionFlag.East : x.CompareTo(zero) < 0 ? DirectionFlag.West : 0) |
                (y.CompareTo(zero) > 0 ? DirectionFlag.North : y.CompareTo(zero) < 0 ? DirectionFlag.South : 0) |
                (x.CompareTo(zero), y.CompareTo(zero)) switch
                {
                    ( > 0, > 0) => DirectionFlag.NorthEast,
                    ( < 0, > 0) => DirectionFlag.NorthWest,
                    ( < 0, < 0) => DirectionFlag.SouthWest,
                    ( > 0, < 0) => DirectionFlag.SouthEast,
                    _ => 0
                };

            return result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(this Vector2Int dir)
        {
            return ToDirectionFlag(dir.x, dir.y);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DirectionFlag ToDirectionFlag(this Vector2 dir)
        {
            return ToDirectionFlag(dir.x, dir.y);
        }

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
