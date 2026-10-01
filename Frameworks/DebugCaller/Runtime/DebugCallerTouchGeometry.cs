using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller
{
    public static class DebugCallerTouchGeometry
    {
        public static float CalculateCornerExtent(
            Vector2 screenSize,
            float screenDpi,
            float minimumSizeInches,
            float fallbackRatio,
            float maximumRatio)
        {
            float shortEdge = Mathf.Min(screenSize.x, screenSize.y);
            if (shortEdge <= 0f)
            {
                return 0f;
            }

            float clampedFallbackRatio = Mathf.Clamp(fallbackRatio, 0.05f, 0.45f);
            float clampedMaximumRatio = Mathf.Clamp(
                maximumRatio,
                clampedFallbackRatio,
                0.45f
            );
            float fallbackExtent = shortEdge * clampedFallbackRatio;
            float maximumExtent = shortEdge * clampedMaximumRatio;
            float dpiExtent = IsUsableDpi(screenDpi)
                ? screenDpi * Mathf.Max(0f, minimumSizeInches)
                : 0f;

            return Mathf.Clamp(
                Mathf.Max(fallbackExtent, dpiExtent),
                fallbackExtent,
                maximumExtent
            );
        }

        public static bool IsActiveTouchPhase(TouchPhase phase)
        {
            return phase == TouchPhase.Began
                || phase == TouchPhase.Moved
                || phase == TouchPhase.Stationary;
        }

        public static DebugCallerCorner GetCorner(
            Vector2 position,
            Vector2 screenSize,
            float cornerExtent)
        {
            if (screenSize.x <= 0f
                || screenSize.y <= 0f
                || cornerExtent <= 0f
                || position.x < 0f
                || position.y < 0f
                || position.x > screenSize.x
                || position.y > screenSize.y)
            {
                return DebugCallerCorner.None;
            }

            bool isLeft = position.x <= cornerExtent;
            bool isRight = position.x >= screenSize.x - cornerExtent;
            bool isBottom = position.y <= cornerExtent;
            bool isTop = position.y >= screenSize.y - cornerExtent;

            if (isLeft && isBottom)
            {
                return DebugCallerCorner.BottomLeft;
            }

            if (isRight && isBottom)
            {
                return DebugCallerCorner.BottomRight;
            }

            if (isLeft && isTop)
            {
                return DebugCallerCorner.TopLeft;
            }

            return isRight && isTop
                ? DebugCallerCorner.TopRight
                : DebugCallerCorner.None;
        }

        private static bool IsUsableDpi(float dpi)
        {
            return dpi > 0f && !float.IsNaN(dpi) && !float.IsInfinity(dpi);
        }
    }
}
