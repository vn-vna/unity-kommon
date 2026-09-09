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
        public GridCoord SelectedCellRelativePosition { get; set; }
        public Vector2 IntentPointerAnchor { get; set; }
        public DirectionFlag PreferredAxis { get; set; }

        public void UpdatePointerIntent(Vector2 pointer, float threshold)
        {
            Vector2 travel = pointer - IntentPointerAnchor;
            if (Mathf.Max(Mathf.Abs(travel.x), Mathf.Abs(travel.y)) <= threshold)
                return;

            PreferredAxis = Mathf.Abs(travel.x) > Mathf.Abs(travel.y)
                ? DirectionFlag.Horizontal : DirectionFlag.Vertical;
            IntentPointerAnchor = pointer;
        }
    }
}
