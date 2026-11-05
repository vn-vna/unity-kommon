using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.DebugCaller.Editor.Tests
{
    public sealed class VersionDebugUnlockGestureTests
    {
        [Test]
        public void Hold_ArmsOnlyAfterTenContinuousSeconds()
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            Hold(gesture, 9.999f);
            Assert.That(gesture.IsArmed, Is.False);
            Hold(gesture, 10f);
            Assert.That(gesture.IsArmed, Is.True);
            Assert.That(gesture.TapCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void Hold_RequiresExactlyTwoTouches(int count)
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            gesture.UpdateTouches(count, Held(1), Held(2), 9f);
            Hold(gesture, 10f);
            Assert.That(gesture.IsArmed, Is.False);
            Hold(gesture, 20f);
            Assert.That(gesture.IsArmed, Is.True);
        }

        [TestCase(TouchPhase.Ended, true)]
        [TestCase(TouchPhase.Canceled, true)]
        [TestCase(TouchPhase.Moved, false)]
        public void Hold_InterruptedTouchRestartsTimer(TouchPhase phase, bool overVersion)
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            gesture.UpdateTouches(2, Held(1), new VersionDebugTouch(2, phase, overVersion), 9f);
            Hold(gesture, 10f);
            Assert.That(gesture.IsArmed, Is.False);
        }

        [Test]
        public void Hold_ArrayOrderCanChangeButReplacingAFingerRestartsTimer()
        {
            var reordered = new VersionDebugUnlockGesture();
            Hold(reordered, 0f);
            reordered.UpdateTouches(2, Held(2), Held(1), 10f);
            Assert.That(reordered.IsArmed, Is.True);

            var replaced = new VersionDebugUnlockGesture();
            Hold(replaced, 0f);
            replaced.UpdateTouches(2, Held(1), Held(3), 9f);
            replaced.UpdateTouches(2, Held(1), Held(3), 10f);
            Assert.That(replaced.IsArmed, Is.False);
            replaced.UpdateTouches(2, Held(1), Held(3), 19f);
            Assert.That(replaced.IsArmed, Is.True);
        }

        [Test]
        public void Hold_OneFingerCannotBeCountedTwice()
        {
            var gesture = new VersionDebugUnlockGesture();
            gesture.UpdateTouches(2, Held(1), Held(1), 0f);
            gesture.UpdateTouches(2, Held(1), Held(1), 10f);
            Assert.That(gesture.IsArmed, Is.False);
        }

        [Test]
        public void Taps_RequireHoldAndDoNotCountTheHoldReleases()
        {
            var gesture = new VersionDebugUnlockGesture();
            for (int i = 0; i < 10; i++)
            {
                Assert.That(Tap(gesture, i), Is.False);
            }
            Assert.That(gesture.TapCount, Is.Zero);

            Hold(gesture, 20f);
            Hold(gesture, 30f);
            gesture.UpdateTouches(2, new VersionDebugTouch(1, TouchPhase.Ended, true), Held(2), 30.1f);
            gesture.UpdateTouches(1, new VersionDebugTouch(2, TouchPhase.Ended, true), default, 30.2f);
            Assert.That(gesture.IsArmed, Is.True);
            Assert.That(gesture.TapCount, Is.Zero);
            for (int i = 0; i < 9; i++)
            {
                Assert.That(Tap(gesture, 31f + i), Is.False);
            }
            Assert.That(gesture.TapCount, Is.EqualTo(9));
            Assert.That(Tap(gesture, 40f), Is.True);
            Assert.That(gesture.IsArmed, Is.False);
            Assert.That(Tap(gesture, 41f), Is.False, "Completion is emitted only once.");
        }

        [Test]
        public void Release_RejectsNewTouchBeforeTheHeldPairHasLifted()
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            Hold(gesture, 10f);
            gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Began, true), default, 10.1f);
            Assert.That(gesture.IsArmed, Is.False);
        }

        [TestCase(TouchPhase.Canceled, true)]
        [TestCase(TouchPhase.Ended, false)]
        public void Release_CancellationOrLeavingVersionDisarms(TouchPhase phase, bool overVersion)
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            Hold(gesture, 10f);
            gesture.UpdateTouches(1, new VersionDebugTouch(1, phase, overVersion), default, 10.1f);
            Assert.That(gesture.IsArmed, Is.False);
        }

        [Test]
        public void Taps_OutsideTouchBreaksTheConsecutiveSequence()
        {
            var gesture = ArmedGesture();
            Tap(gesture, 11f);
            gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Began, false), default, 12f);
            Assert.That(gesture.IsArmed, Is.False);
            Assert.That(gesture.TapCount, Is.Zero);
            Assert.That(Tap(gesture, 13f), Is.False);
        }

        [TestCase(2, TouchPhase.Began, true)]
        [TestCase(1, TouchPhase.Canceled, true)]
        [TestCase(1, TouchPhase.Moved, false)]
        [TestCase(0, TouchPhase.Stationary, true)]
        public void Taps_InvalidOrLostTouchCancelsProgress(int count, TouchPhase phase, bool overVersion)
        {
            var gesture = ArmedGesture();
            Tap(gesture, 11f);
            gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Began, true), default, 12f);
            gesture.UpdateTouches(count, new VersionDebugTouch(3, phase, overVersion), Held(4), 12.1f);
            Assert.That(gesture.IsArmed, Is.False);
            Assert.That(gesture.TapCount, Is.Zero);
        }

        [Test]
        public void Taps_LongPressIsNotATap()
        {
            var gesture = ArmedGesture();
            gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Began, true), default, 11f);
            Assert.That(gesture.UpdateTouches(1,
                new VersionDebugTouch(3, TouchPhase.Ended, true), default, 11.6f), Is.False);
            Assert.That(gesture.IsArmed, Is.False);
        }

        [Test]
        public void Taps_IdleTimeDoesNotAddAnUnrequestedSequenceDeadline()
        {
            var gesture = ArmedGesture();
            gesture.UpdateTouches(0, default, default, 100f);
            Assert.That(gesture.IsArmed, Is.True);
            Assert.That(Tap(gesture, 101f), Is.False);
            Assert.That(gesture.TapCount, Is.EqualTo(1));
        }

        [Test]
        public void Reset_ClearsArmingAndPartialTapCount()
        {
            var gesture = ArmedGesture();
            Tap(gesture, 11f);
            gesture.Reset();
            Assert.That(gesture.IsArmed, Is.False);
            Assert.That(gesture.TapCount, Is.Zero);
            Assert.That(Tap(gesture, 12f), Is.False);
        }

        private static VersionDebugUnlockGesture ArmedGesture()
        {
            var gesture = new VersionDebugUnlockGesture();
            Hold(gesture, 0f);
            Hold(gesture, 10f);
            gesture.UpdateTouches(0, default, default, 10.1f);
            return gesture;
        }

        private static VersionDebugTouch Held(int id)
        {
            return new VersionDebugTouch(id, TouchPhase.Stationary, true);
        }

        private static void Hold(VersionDebugUnlockGesture gesture, float time)
        {
            gesture.UpdateTouches(2, Held(1), Held(2), time);
        }

        private static bool Tap(VersionDebugUnlockGesture gesture, float time)
        {
            gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Began, true), default, time);
            return gesture.UpdateTouches(1, new VersionDebugTouch(3, TouchPhase.Ended, true), default, time + 0.1f);
        }
    }
}
