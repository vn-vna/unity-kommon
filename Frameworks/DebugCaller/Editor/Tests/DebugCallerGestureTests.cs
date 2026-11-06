using NUnit.Framework;

namespace Com.Scheherazade.Common.DebugCaller.Editor.Tests
{
    public sealed class DebugCallerGestureTests
    {
        private static readonly DebugCallerCornerPair TopLeftBottomRight = new(
            DebugCallerCorner.TopLeft,
            DebugCallerCorner.BottomRight
        );

        [Test]
        public void UpdateCornerHold_RequiresFiveSecondContinuousOppositeCornerHold()
        {
            var gesture = CreateGesture();

            gesture.UpdateCornerHold(TopLeftBottomRight, 0f);
            gesture.UpdateCornerHold(TopLeftBottomRight, 4.99f);

            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.AwaitingCornerHold));
            Assert.That(gesture.IsBorderVisible, Is.False);
            Assert.That(gesture.CornerHoldElapsed, Is.EqualTo(4.99f).Within(0.001f));

            gesture.UpdateCornerHold(TopLeftBottomRight, 5f);

            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.AwaitingTapSequence));
            Assert.That(gesture.CornerHoldElapsed, Is.EqualTo(5f));
            Assert.That(gesture.IsBorderVisible, Is.True);
        }

        [Test]
        public void UpdateCornerHold_RejectsAdjacentCornersAndResetsWhenTouchIsReleased()
        {
            var gesture = CreateGesture();
            var adjacentCorners = new DebugCallerCornerPair(
                DebugCallerCorner.TopLeft,
                DebugCallerCorner.TopRight
            );

            gesture.UpdateCornerHold(adjacentCorners, 0f);
            gesture.UpdateCornerHold(adjacentCorners, 5f);

            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.AwaitingCornerHold));
            Assert.That(gesture.IsBorderVisible, Is.False);

            gesture.UpdateCornerHold(TopLeftBottomRight, 5.1f);
            gesture.UpdateCornerHold(default, 9f);

            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.AwaitingCornerHold));
            Assert.That(gesture.IsBorderVisible, Is.False);
        }

        [Test]
        public void RegisterTap_SucceedsOnSeventhTapAtEitherHeldCorner()
        {
            AssertSuccessfulTapSequence(DebugCallerCorner.TopLeft);
            AssertSuccessfulTapSequence(DebugCallerCorner.BottomRight);
        }

        [Test]
        public void RegisterTap_CancelsWhenTapChangesCornerOrWindowExpires()
        {
            var changedCornerGesture = CreateGesture();
            StartTapSequence(changedCornerGesture);

            Assert.That(
                changedCornerGesture.RegisterTap(DebugCallerCorner.TopLeft, 5.1f),
                Is.False
            );
            Assert.That(
                changedCornerGesture.RegisterTap(DebugCallerCorner.BottomRight, 5.2f),
                Is.False
            );
            Assert.That(
                changedCornerGesture.Phase,
                Is.EqualTo(DebugCallerPhase.AwaitingCornerHold)
            );

            var expiredGesture = CreateGesture();
            StartTapSequence(expiredGesture);

            Assert.That(
                expiredGesture.RegisterTap(DebugCallerCorner.TopLeft, 10f),
                Is.False
            );
            Assert.That(
                expiredGesture.Phase,
                Is.EqualTo(DebugCallerPhase.AwaitingCornerHold)
            );
        }

        [Test]
        public void RegisterTap_CancelsWhenTapIsOutsideHeldCorners()
        {
            var gesture = CreateGesture();
            StartTapSequence(gesture);

            Assert.That(
                gesture.RegisterTap(DebugCallerCorner.TopRight, 5.1f),
                Is.False
            );
            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.AwaitingCornerHold));
        }

        private static void AssertSuccessfulTapSequence(DebugCallerCorner corner)
        {
            var gesture = CreateGesture();
            StartTapSequence(gesture);

            for (int i = 0; i < 6; i++)
            {
                Assert.That(
                    gesture.RegisterTap(corner, 5.1f + i * 0.1f),
                    Is.False
                );
            }

            Assert.That(gesture.RegisterTap(corner, 5.7f), Is.True);
            Assert.That(gesture.Phase, Is.EqualTo(DebugCallerPhase.Succeeded));
            Assert.That(gesture.IsBorderVisible, Is.False);
        }

        private static DebugCallerGesture CreateGesture()
        {
            return new DebugCallerGesture(5f, 5f, 7);
        }

        private static void StartTapSequence(DebugCallerGesture gesture)
        {
            gesture.UpdateCornerHold(TopLeftBottomRight, 0f);
            gesture.UpdateCornerHold(TopLeftBottomRight, 5f);
        }
    }
}
