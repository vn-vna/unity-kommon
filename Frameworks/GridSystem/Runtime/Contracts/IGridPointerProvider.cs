using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Optional press-only target assistance. It changes which cell is selected,
    /// never the pointer ray or the position used to calculate the grab offset.
    /// </summary>
    public interface IGridPointerSelectionProvider
    {
        GridCell ResolvePressCell(GridBoard board, Vector3 pointerWorldPosition, GridCell directCell);
    }

    /// <summary>
    /// Turns screen input (mouse/touch) into a world ray plus activity flags.
    /// The game owns the input backend and UI gating; the framework only polls.
    /// </summary>
    public interface IGridPointerProvider
    {
        bool Ready { get; }              // camera available, can produce rays
        bool IsPointerActive { get; }    // mouse button held / touch pressing (polled per tick)
        bool IsPointerOverUI { get; }    // pointer over a UI element -> board ignores it
        Ray GetPointerRay();             // pointer screen position -> world ray
    }
}
