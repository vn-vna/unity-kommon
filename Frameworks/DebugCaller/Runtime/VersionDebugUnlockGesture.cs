using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller
{
    public readonly struct VersionDebugTouch
    {
        public int FingerId { get; }
        public TouchPhase Phase { get; }
        public bool IsOverVersion { get; }
        public bool IsHeld => Phase != TouchPhase.Ended && Phase != TouchPhase.Canceled;

        public VersionDebugTouch(int fingerId, TouchPhase phase, bool isOverVersion)
        {
            FingerId = fingerId;
            Phase = phase;
            IsOverVersion = isOverVersion;
        }
    }

    // Receives snapshots rather than reading Input, so timing and interruptions are deterministic.
    public sealed class VersionDebugUnlockGesture
    {
        public const float HoldDuration = 10f;
        public const int RequiredTapCount = 10;
        public const float MaximumTapDuration = 0.5f;

        private Phase _phase;
        private int _firstHoldFinger;
        private int _secondHoldFinger;
        private float _holdStartedAt = -1f;
        private int _tapFinger = -1;
        private float _tapStartedAt;

        public bool IsArmed => _phase == Phase.AwaitingRelease || _phase == Phase.AwaitingTaps;
        public int TapCount { get; private set; }

        public bool UpdateTouches(
            int touchCount,
            VersionDebugTouch first,
            VersionDebugTouch second,
            float currentTime
        )
        {
            switch (_phase)
            {
                case Phase.Holding:
                    UpdateHold(touchCount, first, second, currentTime);
                    return false;
                case Phase.AwaitingRelease:
                    UpdateRelease(touchCount, first, second);
                    return false;
                case Phase.AwaitingTaps:
                    return UpdateTap(touchCount, first, currentTime);
                default:
                    return false;
            }
        }

        public void Reset()
        {
            _phase = Phase.Holding;
            _holdStartedAt = -1f;
            _firstHoldFinger = 0;
            _secondHoldFinger = 0;
            _tapFinger = -1;
            _tapStartedAt = 0f;
            TapCount = 0;
        }

        private void UpdateHold(
            int touchCount,
            VersionDebugTouch first,
            VersionDebugTouch second,
            float currentTime
        )
        {
            if (touchCount != 2 || first.FingerId == second.FingerId
                || !first.IsHeld || !second.IsHeld
                || !first.IsOverVersion || !second.IsOverVersion)
            {
                Reset();
                return;
            }

            bool sameFingers = ContainsHoldFinger(first.FingerId) && ContainsHoldFinger(second.FingerId);
            if (_holdStartedAt < 0f || !sameFingers)
            {
                _firstHoldFinger = first.FingerId;
                _secondHoldFinger = second.FingerId;
                _holdStartedAt = currentTime;
            }

            if (currentTime - _holdStartedAt >= HoldDuration)
            {
                _phase = Phase.AwaitingRelease;
            }
        }

        private void UpdateRelease(int touchCount, VersionDebugTouch first, VersionDebugTouch second)
        {
            if (touchCount == 0)
            {
                _phase = Phase.AwaitingTaps;
                return;
            }

            if (touchCount > 2 || !IsValidRelease(first)
                || (touchCount == 2 && !IsValidRelease(second)))
            {
                Reset();
                return;
            }

            if (!first.IsHeld && (touchCount == 1 || !second.IsHeld))
            {
                _phase = Phase.AwaitingTaps;
            }
        }

        private bool UpdateTap(int touchCount, VersionDebugTouch touch, float currentTime)
        {
            if (touchCount == 0 && _tapFinger < 0)
            {
                return false;
            }

            if (touchCount != 1 || !touch.IsOverVersion || touch.Phase == TouchPhase.Canceled)
            {
                Reset();
                return false;
            }

            if (touch.Phase == TouchPhase.Began && _tapFinger < 0)
            {
                _tapFinger = touch.FingerId;
                _tapStartedAt = currentTime;
                return false;
            }

            if (_tapFinger != touch.FingerId || touch.Phase == TouchPhase.Began
                || currentTime - _tapStartedAt > MaximumTapDuration)
            {
                Reset();
                return false;
            }

            if (touch.Phase != TouchPhase.Ended)
            {
                return false;
            }

            _tapFinger = -1;
            TapCount++;
            if (TapCount < RequiredTapCount)
            {
                return false;
            }

            _phase = Phase.Succeeded;
            return true;
        }

        private bool ContainsHoldFinger(int fingerId)
        {
            return fingerId == _firstHoldFinger || fingerId == _secondHoldFinger;
        }

        private bool IsValidRelease(VersionDebugTouch touch)
        {
            return ContainsHoldFinger(touch.FingerId) && touch.IsOverVersion
                && touch.Phase != TouchPhase.Began && touch.Phase != TouchPhase.Canceled;
        }

        private enum Phase
        {
            Holding,
            AwaitingRelease,
            AwaitingTaps,
            Succeeded
        }
    }
}
