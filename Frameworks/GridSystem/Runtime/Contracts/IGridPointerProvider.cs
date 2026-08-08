using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
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
