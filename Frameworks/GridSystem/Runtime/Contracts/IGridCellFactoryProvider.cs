namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Creates/destroys/refreshes the per-cell view layer. The logic core stays
    /// visual-free; the factory is the game's injection point for cell
    /// presentation. A logic-only game can leave it null (cells then have no
    /// views — placement/drift still work).
    /// </summary>
    public interface IGridCellFactoryProvider
    {
        void AttachCellView(GridCell cell, IGridCoordinateProvider coordinates); // on pool creation
        void DetachCellView(GridCell cell);                                      // on pool clear
        void RefreshCellView(GridCell cell);                                     // visual refresh (flag/occupant changes)
        void SetCellActive(GridCell cell, bool active);                          // region enable/disable
    }
}
