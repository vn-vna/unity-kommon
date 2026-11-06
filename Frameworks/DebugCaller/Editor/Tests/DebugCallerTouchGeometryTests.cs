using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller.Editor.Tests
{
    public sealed class DebugCallerTouchGeometryTests
    {
        private static readonly Vector2 PortraitScreen = new(1080f, 2400f);

        [Test]
        public void CalculateCornerExtent_UsesLargerDpiBasedPhysicalTarget()
        {
            float extent = DebugCallerTouchGeometry.CalculateCornerExtent(
                PortraitScreen,
                440f,
                0.9f,
                0.25f,
                0.45f
            );

            Assert.That(extent, Is.EqualTo(396f).Within(0.001f));
        }

        [TestCase(0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void CalculateCornerExtent_UsesRatioFallbackForInvalidDpi(float dpi)
        {
            float extent = DebugCallerTouchGeometry.CalculateCornerExtent(
                PortraitScreen,
                dpi,
                0.9f,
                0.25f,
                0.45f
            );

            Assert.That(extent, Is.EqualTo(270f).Within(0.001f));
        }

        [Test]
        public void CalculateCornerExtent_ClampsVeryLargeDpiAndIsOrientationIndependent()
        {
            float portraitExtent = DebugCallerTouchGeometry.CalculateCornerExtent(
                PortraitScreen,
                1000f,
                0.9f,
                0.25f,
                0.45f
            );
            float landscapeExtent = DebugCallerTouchGeometry.CalculateCornerExtent(
                new Vector2(PortraitScreen.y, PortraitScreen.x),
                1000f,
                0.9f,
                0.25f,
                0.45f
            );

            Assert.That(portraitExtent, Is.EqualTo(486f).Within(0.001f));
            Assert.That(landscapeExtent, Is.EqualTo(portraitExtent));
        }

        [TestCase(TouchPhase.Began, true)]
        [TestCase(TouchPhase.Moved, true)]
        [TestCase(TouchPhase.Stationary, true)]
        [TestCase(TouchPhase.Ended, false)]
        [TestCase(TouchPhase.Canceled, false)]
        public void IsActiveTouchPhase_RejectsReleasedTouches(
            TouchPhase phase,
            bool expected)
        {
            Assert.That(
                DebugCallerTouchGeometry.IsActiveTouchPhase(phase),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void GetCorner_UsesDpiResolvedExtentForHoldAndTapTargets()
        {
            const float extent = 396f;

            Assert.That(
                DebugCallerTouchGeometry.GetCorner(
                    new Vector2(350f, 2100f),
                    PortraitScreen,
                    extent
                ),
                Is.EqualTo(DebugCallerCorner.TopLeft)
            );
            Assert.That(
                DebugCallerTouchGeometry.GetCorner(
                    new Vector2(730f, 300f),
                    PortraitScreen,
                    extent
                ),
                Is.EqualTo(DebugCallerCorner.BottomRight)
            );
            Assert.That(
                DebugCallerTouchGeometry.GetCorner(
                    new Vector2(397f, 2003f),
                    PortraitScreen,
                    extent
                ),
                Is.EqualTo(DebugCallerCorner.None)
            );
        }
    }
}
