using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>A synchronous, read-only proposal. Do not retain the host's scratch collection.</summary>
    public readonly struct GridDriftContext
    {
        public GridBoard Board { get; }
        public GridMap Map { get; }
        public IReadOnlyList<IGridOccupant> Occupants { get; }

        public GridDriftContext(GridBoard board, GridMap map, IReadOnlyList<IGridOccupant> occupants)
        {
            Board = board;
            Map = map;
            Occupants = occupants;
        }
    }

    /// <summary>Modules may veto a proposal, but must not mutate the board during evaluation.</summary>
    public interface IGridDrifterModule
    {
        void HandleRegistered(GridDrifter host);
        bool CanBeginDrag(in GridDriftContext context);
        bool CanMove(in GridDriftContext context, GridCoord delta);
        void HandleUnregistered(GridDrifter host);
    }
}
