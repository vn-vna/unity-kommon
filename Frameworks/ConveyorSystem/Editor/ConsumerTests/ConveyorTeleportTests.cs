using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.ConsumerTests
{
    public sealed class ConveyorTeleportTests
    {
        private ConveyorWorld _world;

        [SetUp] public void Setup() { _world = new ConveyorWorld(64, 256); }
        [TearDown] public void Cleanup() { ConveyorRuntimeTests.Drain(_world); _world.Shutdown(ConveyorShutdownDisposition.Discard); }

        [Test]
        public void HighSpeedGateClampsAndNextTickTransfersSameHandleExactlyOnce()
        {
            ConveyorRuntime source = Source("a", "b", 100);
            ConveyorRuntime destination = Target("b");
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 90, 0, 1);
            _world.Tick(10);
            _world.TryGetState(handle, out ConveyorTransportState arrived);
            Assert.That(arrived.Distance, Is.EqualTo(4).Within(0.00001));
            Assert.That(arrived.WaitingGate, Is.GreaterThanOrEqualTo(0));
            Assert.That(_world.TransferCount, Is.Zero);
            _world.Tick(0.5);
            _world.TryGetPayload(handle, out _, out ConveyorRuntime owner);
            _world.TryGetState(handle, out ConveyorTransportState moved);
            Assert.That(owner, Is.SameAs(destination));
            Assert.That(moved.Distance, Is.EqualTo(8.5).Within(0.00001));
            Assert.That(_world.TransferCount, Is.EqualTo(1));
            Assert.That(_world.Count, Is.EqualTo(1));
        }

        [Test]
        public void FullDestinationBlocksOnlyAffectedLaneAndDoesNotLoseCargo()
        {
            ConveyorRuntime source = Source("a", "b", 1, 2);
            ConveyorRuntime destination = Target("b", 2);
            Fill(destination, 0);
            ConveyorPayloadHandle held = ConveyorRuntimeTests.Insert(_world, source, 100, 0, 3);
            ConveyorRuntimeTests.Insert(_world, source, 101, 1, 3);
            ConveyorPayloadHandle follower = ConveyorRuntimeTests.Insert(_world, source, 102, 1, 0);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(1);
            _world.Tick(1);
            Assert.That(source.IsLaneHeld(0), Is.True);
            Assert.That(source.IsLaneHeld(1), Is.False);
            Assert.That(source.GetBlockReason(0), Is.EqualTo(ConveyorBlockReason.Full));
            Assert.That(source.GetBlockedTicks(0), Is.GreaterThanOrEqualTo(1));
            _world.TryGetPayload(held, out _, out ConveyorRuntime owner);
            Assert.That(owner, Is.SameAs(source));
            _world.TryGetState(follower, out ConveyorTransportState moving);
            Assert.That(moving.Distance, Is.EqualTo(2).Within(0.00001));
            Assert.That(_world.VaporizedCount, Is.Zero);
            Assert.That(_world.TransferCount, Is.EqualTo(1));
        }

        [Test]
        public void VaporizingIsOnePermanentRemovalWithConsumerIdentity()
        {
            ConveyorRuntime source = Source("a", "b", 1, policy: VaporizingTransferPolicy.Instance);
            ConveyorRuntime destination = Target("b");
            Fill(destination, 0);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 700, 0, 3);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(1);
            _world.Tick(0);
            Assert.That(_world.TryGetPayload(handle, out _, out _), Is.False);
            Assert.That(_world.VaporizedCount, Is.EqualTo(1));
            Assert.That(_world.TransferCount, Is.Zero);
            Assert.That(_world.TryDequeueResult(out ConveyorResult result), Is.True);
            Assert.That(result.Kind, Is.EqualTo(ConveyorResultKind.Removed));
            Assert.That(result.Payload.ConsumerKey, Is.EqualTo(700));
            Assert.That(result.RemovalReason, Is.EqualTo(ConveyorRemovalReason.Vaporized));
            _world.Tick(0);
            Assert.That(_world.VaporizedCount, Is.EqualTo(1));
        }

        [Test]
        public void MissingOrSuspendedExitNeverCallsOverflowPolicy()
        {
            var policy = new CountingVaporPolicy();
            ConveyorRuntime source = Source("a", "missing", 1, policy: policy);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(source.GetBlockReason(0), Is.EqualTo(ConveyorBlockReason.Unavailable));
            Assert.That(policy.Calls, Is.Zero);
            ConveyorRuntime destination = Target("missing");
            _world.SetSuspended(destination, true);
            _world.Tick(0);
            Assert.That(policy.Calls, Is.Zero);
            _world.SetSuspended(destination, false);
            _world.Tick(0);
            _world.TryGetPayload(handle, out _, out ConveyorRuntime owner);
            Assert.That(owner, Is.SameAs(destination));
        }

        [Test]
        public void MissingDestinationLaneIsUnsupportedRatherThanCapacityOverflow()
        {
            var policy = new CountingVaporPolicy();
            ConveyorRuntime source = Source("a", "b", 1, 2, policy);
            Target("b", 1);
            ConveyorRuntimeTests.Insert(_world, source, 1, 1, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(source.GetBlockReason(1), Is.EqualTo(ConveyorBlockReason.Unsupported));
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(_world.Count, Is.EqualTo(1));
        }

        [Test]
        public void SameLoopTransferConservesCountAndDoesNotImmediatelyRetrigger()
        {
            ConveyorRuntime source = Source("a", "a", 1);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0.25);
            _world.TryGetState(handle, out ConveyorTransportState state);
            Assert.That(state.Distance, Is.EqualTo(8.25).Within(0.00001));
            Assert.That(_world.Count, Is.EqualTo(1));
            Assert.That(_world.TransferCount, Is.EqualTo(1));
            _world.Tick(0);
            Assert.That(_world.TransferCount, Is.EqualTo(1));
        }

        [Test]
        public void ReservationBatchCompetitionUsesStableSourceOrder()
        {
            // Deliberately register b before a. Both compete for the same destination position.
            ConveyorRuntime second = Source("b", "destination", 1);
            ConveyorRuntime first = Source("a", "destination", 1);
            ConveyorRuntime destination = Target("destination");
            ConveyorPayloadHandle a = ConveyorRuntimeTests.Insert(_world, first, 1, 0, 3);
            ConveyorPayloadHandle b = ConveyorRuntimeTests.Insert(_world, second, 2, 0, 3);
            _world.Tick(1); _world.Tick(0);
            _world.TryGetPayload(a, out _, out ConveyorRuntime aOwner);
            _world.TryGetPayload(b, out _, out ConveyorRuntime bOwner);
            Assert.That(aOwner, Is.SameAs(destination));
            Assert.That(bOwner, Is.SameAs(second));
            Assert.That(_world.TransferCount, Is.EqualTo(1));
        }

        [Test]
        public void FullCyclicPairsBlockWithoutInventingCapacityOrLoss()
        {
            ConveyorRuntime a = Source("a", "b", 1);
            ConveyorRuntime b = Source("b", "a", 1);
            Fill(a, 0); Fill(b, 0);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(1); _world.Tick(0);
            Assert.That(a.IsLaneHeld(0), Is.True);
            Assert.That(b.IsLaneHeld(0), Is.True);
            Assert.That(_world.Count, Is.EqualTo(32));
            Assert.That(_world.TransferCount + _world.VaporizedCount, Is.Zero);
        }

        [Test]
        public void BlockedAndReleasedNotificationsAreTransitionsNotRetrySpam()
        {
            ConveyorRuntime source = Source("a", "b", 1);
            ConveyorRuntime destination = Target("b");
            Fill(destination, 0);
            ConveyorRuntimeTests.Insert(_world, source, 1000, 0, 3);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(1); _world.Tick(0); _world.Tick(0); _world.Tick(0);
            int blocked = 0;
            while (_world.TryDequeueResult(out ConveyorResult result)) if (result.Kind == ConveyorResultKind.LaneBlocked) blocked++;
            Assert.That(blocked, Is.EqualTo(1));
            ConveyorFrameItem[] copy = new ConveyorFrameItem[64];
            destination.Frame.CopyTo(copy);
            for (int index = 0; index < destination.Frame.Count; index++) _world.TryRemove(copy[index].Handle);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(0);
            int released = 0;
            while (_world.TryDequeueResult(out ConveyorResult result)) if (result.Kind == ConveyorResultKind.LaneReleased) released++;
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void DetachingTeleportRemovesItsGatesAndReleasesPendingMotion()
        {
            ConveyorRuntime source = Target("a");
            IConveyorModule module = _world.AttachModule(source, new TeleportModuleFactory("missing", 0.25, 0.5));
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            _world.DetachModule(source, module);
            _world.Tick(1);
            _world.TryGetState(handle, out ConveyorTransportState state);
            Assert.That(state.Distance, Is.EqualTo(5).Within(0.00001));
            Assert.That(state.WaitingGate, Is.EqualTo(-1));
        }

        [Test]
        public void DuplicateEntranceAndSameLoopExactOverlapRejectTransactionally()
        {
            ConveyorRuntime source = Source("a", "b", 1);
            Assert.Throws<ArgumentException>(() => _world.AttachModule(source, new TeleportModuleFactory("c", 0.25, 0.5)));
            Assert.Throws<ArgumentException>(() => _world.CreateConveyor("invalid", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(),
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("invalid", 0.25, 0.25) }));
            Assert.That(_world.TryGetConveyor("invalid", out _), Is.False);
            Assert.That(_world.IsFaulted, Is.False);
        }

        [Test]
        public void SameLoopOverlappingFootprintIsUnsupportedNotVaporizingOverflow()
        {
            var policy = new CountingVaporPolicy();
            ConveyorRuntime source = _world.CreateConveyor("a", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1),
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("a", 0.25, 0.28, policy) });
            ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(source.GetBlockReason(0), Is.EqualTo(ConveyorBlockReason.Unsupported));
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(_world.Count, Is.EqualTo(1));
        }

        [Test]
        public void EntranceCrossingAtLoopSeamIsNotSkipped()
        {
            ConveyorRuntime source = _world.CreateConveyor("a", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(10, 1),
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("b", 0.0625, 0.5) });
            Target("b");
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 15);
            _world.Tick(1);
            _world.TryGetState(handle, out ConveyorTransportState arrived);
            Assert.That(arrived.Distance, Is.EqualTo(1).Within(0.00001));
            _world.Tick(0);
            Assert.That(_world.TransferCount, Is.EqualTo(1));
        }

        [Test]
        public void FailingPolicyCancelsOtherPreparedTransfersAndReturnsResultCapacity()
        {
            ConveyorRuntime first = Source("a", "free", 1);
            ConveyorRuntime second = Source("b", "full", 1, policy: new ThrowingPolicy());
            ConveyorRuntime free = Target("free");
            ConveyorRuntime full = Target("full"); Fill(full, 0);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, first, 1, 0, 3);
            ConveyorRuntimeTests.Insert(_world, second, 2, 0, 3);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(1);
            Assert.Throws<InvalidOperationException>(() => _world.Tick(0));
            Assert.That(_world.IsFaulted, Is.True);
            Assert.That(_world.AvailableResultCapacity, Is.EqualTo(256));
            Assert.That(free.Count, Is.Zero);
            _world.TryGetPayload(handle, out _, out ConveyorRuntime owner);
            Assert.That(owner, Is.SameAs(first));
        }

        [Test]
        public void TransferWaitsForResultCapacityWithoutReleasingSource()
        {
            ConveyorRuntimeTests.Drain(_world); _world.Dispose();
            _world = new ConveyorWorld(4, 1);
            ConveyorRuntime source = Source("a", "b", 1);
            ConveyorRuntime destination = Target("b");
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(source.Count, Is.EqualTo(1));
            Assert.That(destination.Count, Is.Zero);
            ConveyorRuntimeTests.Drain(_world);
            _world.Tick(0);
            _world.TryGetPayload(handle, out _, out ConveyorRuntime owner);
            Assert.That(owner, Is.SameAs(destination));
        }

        private ConveyorRuntime Source(string id, string destination, double speed, int lanes = 1, IConveyorTransferPolicy policy = null)
            => _world.CreateConveyor(id, ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(speed, 1), lanes,
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory(destination, 0.25, 0.5, policy) });

        private ConveyorRuntime Target(string id, int lanes = 1)
            => _world.CreateConveyor(id, ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1), lanes);

        private void Fill(ConveyorRuntime runtime, int lane)
        {
            for (int distance = 0; distance < 16; distance++) ConveyorRuntimeTests.Insert(_world, runtime, 100 + distance, lane, distance);
        }

        private sealed class CountingVaporPolicy : IConveyorTransferPolicy
        {
            internal int Calls;
            public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) { Calls++; return ConveyorOverflowDisposition.Vaporize; }
        }

        private sealed class ThrowingPolicy : IConveyorTransferPolicy
        {
            public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context)
                => throw new InvalidOperationException("deliberate policy failure");
        }
    }
}
