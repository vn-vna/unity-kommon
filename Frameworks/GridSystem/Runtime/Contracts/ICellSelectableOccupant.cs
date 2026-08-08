using System;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Opt-in contract for occupants that expose a selection state (drop-car's
    /// <c>Hole</c> selection binding). <see cref="GridCell"/> subscribes to this
    /// instead of a concrete occupant type.
    /// </summary>
    public interface ICellSelectableOccupant
    {
        event Action<bool> SelectionChanged;   // true = selected
        bool IsSelected { get; }
    }
}
