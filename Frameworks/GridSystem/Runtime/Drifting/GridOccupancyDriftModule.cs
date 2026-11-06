namespace Com.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>Default board geometry and IDrifter restrictions, shared by prediction and commit.</summary>
    public sealed class GridOccupancyDriftModule : IGridDrifterModule
    {
        public void HandleRegistered(GridDrifter host) { }
        public void HandleUnregistered(GridDrifter host) { }

        public bool CanBeginDrag(in GridDriftContext context)
        {
            for (int index = 0; index < context.Occupants.Count; index++)
            {
                IGridOccupant occupant = context.Occupants[index];
                if (occupant?.HookedCell == null || occupant is not IDrifter drifter || !drifter.Driftable)
                    return false;
            }
            return context.Occupants.Count > 0;
        }

        public bool CanMove(in GridDriftContext context, GridCoord delta)
        {
            DirectionFlag direction = delta.ToDirectionFlag();
            for (int index = 0; index < context.Occupants.Count; index++)
            {
                IGridOccupant occupant = context.Occupants[index];
                if (occupant?.HookedCell == null || occupant is not IDrifter drifter
                    || !drifter.Driftable || (drifter.MovementLimitations & direction) != direction)
                    return false;

                // GridMap's placement walker skips out-of-range cells. Check the entire footprint first.
                IPlaceableObject body = occupant.PlaceableObject;
                if (body?.Grid == null) return false;
                GridCoord origin = occupant.HookedCell.GridPosition + delta + body.Offset;
                for (int y = 0; y < body.Grid.Size.y; y++)
                for (int x = 0; x < body.Grid.Size.x; x++)
                {
                    if (body.Grid[x, y] == PlaceableObjectGrid.GridCellEmptySentinelValue) continue;
                    GridCell cell = context.Map.AccessCell(origin + new GridCoord(x, y));
                    if (cell == null || cell.IsBorder) return false;
                }
            }
            return context.Map.CheckGroupPlaceable(context.Occupants, delta);
        }
    }
}
