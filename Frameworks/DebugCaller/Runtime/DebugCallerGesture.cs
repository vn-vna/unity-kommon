using System;

namespace Com.Scheherazade.Common.DebugCaller
{
    public enum DebugCallerPhase
    {
        AwaitingCornerHold,
        AwaitingTapSequence,
        Succeeded
    }

    public enum DebugCallerCorner
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public readonly struct DebugCallerCornerPair
    {
        public DebugCallerCorner First { get; }
        public DebugCallerCorner Second { get; }

        public bool IsOpposite => First != DebugCallerCorner.None
            && Second != DebugCallerCorner.None
            && ((First == DebugCallerCorner.TopLeft && Second == DebugCallerCorner.BottomRight)
                || (First == DebugCallerCorner.BottomRight && Second == DebugCallerCorner.TopLeft)
                || (First == DebugCallerCorner.TopRight && Second == DebugCallerCorner.BottomLeft)
                || (First == DebugCallerCorner.BottomLeft && Second == DebugCallerCorner.TopRight));

        public DebugCallerCornerPair(DebugCallerCorner first, DebugCallerCorner second)
        {
            First = first;
            Second = second;
        }

        public bool Contains(DebugCallerCorner corner)
        {
            return corner != DebugCallerCorner.None && (corner == First || corner == Second);
        }
    }

    public sealed class DebugCallerGesture
    {
        private readonly float _cornerHoldDuration;
        private readonly float _tapWindowDuration;
        private readonly int _requiredTapCount;

        private float _cornerHoldStartedAt = -1f;
        private float _tapWindowEndsAt;
        private int _tapCount;
        private DebugCallerCornerPair _heldCorners;
        private DebugCallerCorner _tapCorner;

        public DebugCallerPhase Phase { get; private set; }
        public float CornerHoldElapsed { get; private set; }
        public int TapCount => _tapCount;

        public bool IsBorderVisible => Phase == DebugCallerPhase.AwaitingTapSequence;

        public DebugCallerGesture(
            float cornerHoldDuration,
            float tapWindowDuration,
            int requiredTapCount)
        {
            if (cornerHoldDuration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cornerHoldDuration));
            }

            if (tapWindowDuration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(tapWindowDuration));
            }

            if (requiredTapCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredTapCount));
            }

            _cornerHoldDuration = cornerHoldDuration;
            _tapWindowDuration = tapWindowDuration;
            _requiredTapCount = requiredTapCount;
            Phase = DebugCallerPhase.AwaitingCornerHold;
        }

        public void UpdateCornerHold(DebugCallerCornerPair heldCorners, float currentTime)
        {
            if (Phase == DebugCallerPhase.Succeeded)
            {
                return;
            }

            if (Phase == DebugCallerPhase.AwaitingTapSequence)
            {
                if (currentTime >= _tapWindowEndsAt)
                {
                    Reset();
                }

                return;
            }

            if (!heldCorners.IsOpposite)
            {
                Reset();
                return;
            }

            if (_cornerHoldStartedAt < 0f || !IsSamePair(_heldCorners, heldCorners))
            {
                _heldCorners = heldCorners;
                _cornerHoldStartedAt = currentTime;
                CornerHoldElapsed = 0f;
                return;
            }

            CornerHoldElapsed = Math.Min(
                _cornerHoldDuration,
                Math.Max(0f, currentTime - _cornerHoldStartedAt)
            );
            if (CornerHoldElapsed < _cornerHoldDuration)
            {
                return;
            }

            Phase = DebugCallerPhase.AwaitingTapSequence;
            _cornerHoldStartedAt = -1f;
            _tapWindowEndsAt = currentTime + _tapWindowDuration;
        }

        public bool RegisterTap(DebugCallerCorner corner, float currentTime)
        {
            if (Phase != DebugCallerPhase.AwaitingTapSequence)
            {
                return false;
            }

            if (currentTime >= _tapWindowEndsAt || !_heldCorners.Contains(corner))
            {
                Reset();
                return false;
            }

            if (_tapCorner == DebugCallerCorner.None)
            {
                _tapCorner = corner;
            }
            else if (_tapCorner != corner)
            {
                Reset();
                return false;
            }

            _tapCount++;
            if (_tapCount < _requiredTapCount)
            {
                return false;
            }

            Phase = DebugCallerPhase.Succeeded;
            return true;
        }

        public void Reset()
        {
            Phase = DebugCallerPhase.AwaitingCornerHold;
            _cornerHoldStartedAt = -1f;
            _tapWindowEndsAt = 0f;
            _tapCount = 0;
            CornerHoldElapsed = 0f;
            _heldCorners = default;
            _tapCorner = DebugCallerCorner.None;
        }

        private static bool IsSamePair(
            DebugCallerCornerPair first,
            DebugCallerCornerPair second)
        {
            return first.Contains(second.First) && first.Contains(second.Second);
        }
    }
}
