namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// External controller attached to a <see cref="GridCell"/>. Drop-car port
    /// with the color-config surface stripped (visual concern stays game-side).
    /// </summary>
    public interface ICellExternalController
    {
        GridCell ControlledGridMapCell { get; set; }
        bool CheckObjectPlaceable(IGridOccupant occupant, GridCell cell);
        void HandleControllerAttached(GridCell cell);
        void HandleControllerDetached(GridCell cell);
    }
}
