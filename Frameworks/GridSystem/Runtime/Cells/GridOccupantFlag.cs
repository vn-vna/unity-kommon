using System;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Flags describing how an occupant behaves in the grid.
    /// </summary>
    [Serializable]
    [Flags]
    public enum GridOccupantFlag
    {
        None = 0,

        /// <summary>
        /// The occupant is an overlay, which means it can be placed on top of
        /// other objects.
        /// </summary>
        Overlay = 1 << 0,

        RemoveBase = 1 << 1,

        /// <summary>
        /// Marks the occupant as blocking the border region. Cells holding such
        /// occupants are reported as impassable by <see cref="GridMap.BuildBorderData"/>.
        /// </summary>
        BlocksBorder = 1 << 2,
    }
}
