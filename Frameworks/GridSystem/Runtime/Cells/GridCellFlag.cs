using System;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    [Flags]
    public enum GridCellFlag
    {
        None = 0x0,
        Occupied = 0x1,
        Selected = 0x2,
        Tracked = 0x4,
        Debug = 0x8,
        Border = 0x10,
    }
}
