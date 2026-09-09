using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Optional presentation support. The gameplay position and release events
    /// remain synchronous; implementations animate only their displayed pose.
    /// </summary>
    public interface IGridReleasePresentation
    {
        // Called BEFORE capturing the pointer offset, so re-grabbing can use the visible pose.
        void PrepareForDrift();
        // Called AFTER ControlledPosition has been recentered, BEFORE release events.
        void AnimateRelease(Vector3 previousPosition, float duration);
    }

    /// <summary>
    /// Driftable entity contract. Audio/haptic surface was removed from drop-car's
    /// <c>IDrifter</c> — feedback is funneled through <see cref="IGridFeedbackProvider"/>.
    /// </summary>
    public interface IDrifter
    {
        IGridOccupant Occupant { get; }
        IPlaceableObject PlaceableObject { get; }
        Vector3 DriftingAnchor { get; }
        Vector3 ControlledPosition { get; set; }        // WORLD gameplay anchor; may exclude a release visual offset
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
