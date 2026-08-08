using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// State per drifter, owned by <see cref="GridDrifter"/>.
    /// </summary>
    public class DrifterInformation
    {
        public IDrifter Drifter { get; set; }
        public DirectionFlag MovementAbility { get; set; }   // corrected name (drop-car: MovememtAbility)
        public Vector2 RelativeMousePosition { get; set; }   // grid-plane space, set on attach
        public Vector2Int SelectedCellRelativePosition { get; set; }
    }
}
