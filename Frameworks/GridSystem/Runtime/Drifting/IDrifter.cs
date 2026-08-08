using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Driftable entity contract. Audio/haptic surface was removed from drop-car's
    /// <c>IDrifter</c> — feedback is funneled through <see cref="IGridFeedbackProvider"/>.
    /// </summary>
    public interface IDrifter
    {
        IGridOccupant Occupant { get; }
        IPlaceableObject PlaceableObject { get; }
        Vector3 DriftingAnchor { get; }
        Vector3 ControlledPosition { get; set; }        // WORLD space (entity transform)
        DirectionFlag ControlledMovementMask { get; set; }
        GridCell HookedCell { get; }
        DirectionFlag MovementLimitations { get; }
        bool Driftable { get; }

        void HandleDrifterFailed();     // drift attempt rejected (not driftable / blocked)
        void HandleDriftAttached();     // drifter joined the drift set
        void HandleDrifterPreUpdate();  // called before movement each tick
        void HandleDriftUpdated();      // position was changed this tick
        void HandleDriftDetached();     // released
    }
}
