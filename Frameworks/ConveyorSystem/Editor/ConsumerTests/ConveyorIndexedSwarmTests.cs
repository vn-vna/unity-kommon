using System;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.ConsumerTests
{
    public sealed class ConveyorIndexedSwarmTests
    {
        private ConveyorWorld _world;
        [SetUp] public void Setup() { _world = new ConveyorWorld(4100, 16384); }
        [TearDown] public void Cleanup() { ConveyorRuntimeTests.Drain(_world); _world.Shutdown(ConveyorShutdownDisposition.Discard); }

        [Test]
        public void ReservedCellSnapsAtSeamAndCancellationReturnsIt()
        {
            ConveyorRuntime runtime = Create("a");
            var request = ConveyorRuntimeTests.Request(runtime, 1, 0, 15.9);
            Assert.That(_world.TryReserveIngress(runtime, request, out ConveyorIngressReservation token), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            Assert.That(_world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(2), 0, 0.1, out _), Is.EqualTo(ConveyorAdmissionStatus.Full));
            Assert.That(_world.CancelIngress(token), Is.True);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, runtime, 3, 0, 0.1);
            Assert.That(_world.CommitIngress(token, out _), Is.False);
            _world.TryGetState(handle, out ConveyorTransportState state);
            Assert.That(state.Distance, Is.Zero.Within(0.00001));
        }

        [Test]
        public void MovingPhaseAndLaneIdentityDetermineNearestCell()
        {
            ConveyorRuntime runtime = Create("a", lanes: 2);
            ConveyorPayloadHandle first = ConveyorRuntimeTests.Insert(_world, runtime, 1, 0, 0.1);
            _world.Tick(0.4);
            Assert.That(_world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(2), 0, 0.6, out _), Is.EqualTo(ConveyorAdmissionStatus.Full));
            ConveyorPayloadHandle next = ConveyorRuntimeTests.Insert(_world, runtime, 3, 0, 1.45);
            ConveyorRuntimeTests.Insert(_world, runtime, 4, 1, 0.6);
            _world.TryGetState(next, out ConveyorTransportState state);
            Assert.That(state.Distance, Is.EqualTo(1.4).Within(0.00001));
            Assert.That(_world.TryRemove(first), Is.True);
            Assert.That(_world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(5), 0, 0.6, out _), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
        }

        [Test]
        public void FourThousandUniqueCellsFillAndWarmTicksDoNotAllocate()
        {
            ConveyorPath path = ConveyorRuntimeTests.Square();
            ConveyorRuntime runtime = _world.CreateConveyor("swarm", path,
                new LogicalLaneTransportProvider(1, path.Length / 250.00001, indexedAdmission: true), 16, 2.4f);
            double spacing = path.Length / 250;
            for (int cell = 0; cell < 250; cell++)
                for (int lane = 0; lane < 16; lane++) ConveyorRuntimeTests.Insert(_world, runtime, cell * 16 + lane, lane, cell * spacing);
            Assert.That(_world.Count, Is.EqualTo(4000));
            Assert.That(_world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(9001), 5, 5.0, out _), Is.EqualTo(ConveyorAdmissionStatus.Full));
            ConveyorRuntimeTests.Drain(_world);
            for (int tick = 0; tick < 16; tick++) _world.Tick(1.0 / 60);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 32; tick++) _world.Tick(1.0 / 60);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero);
            Assert.That(runtime.Frame.Count, Is.EqualTo(4000));
            Assert.That(_world.Count, Is.EqualTo(_world.AcceptedCount - _world.RemovedCount - _world.DeliveredCount - _world.VaporizedCount));
        }

        [Test]
        public void IndexedProfilesRejectMultiCellFootprintsWithoutChangingDefaultMode()
        {
            var profiles = new[] { new LogicalPayloadProfile(0, 1), new LogicalPayloadProfile(1, 2) };
            ConveyorRuntime indexed = _world.CreateConveyor("indexed", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, profiles, true));
            ConveyorRuntime legacy = _world.CreateConveyor("legacy", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, profiles));
            var request = new ConveyorPayloadDescriptor(1, 1);
            Assert.That(_world.TryInsertAt(indexed, request, 0, 1, out _), Is.EqualTo(ConveyorAdmissionStatus.Unsupported));
            Assert.That(_world.TryInsertAt(legacy, request, 0, 1, out _), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
        }

        [Test]
        public void SameLoopDestinationQuantizingOntoSourceIsNotVaporizingOverflow()
        {
            var policy = new CountingVaporize();
            ConveyorRuntime runtime = _world.CreateConveyor("a", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, indexedAdmission: true),
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("a", 0.25, 0.27, policy) });
            ConveyorRuntimeTests.Insert(_world, runtime, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(runtime.GetBlockReason(0), Is.EqualTo(ConveyorBlockReason.Unsupported));
            Assert.That(policy.Calls, Is.Zero);
            Assert.That(_world.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedTeleportRetainsBlockingAndVaporizingSemantics(bool vaporize)
        {
            IConveyorTransferPolicy policy = vaporize ? (IConveyorTransferPolicy)VaporizingTransferPolicy.Instance : BlockingTransferPolicy.Instance;
            ConveyorRuntime source = _world.CreateConveyor("a", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, indexedAdmission: true), 2,
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("b", 0.25, 0.5, policy) });
            ConveyorRuntime target = Create("b", lanes: 2);
            for (int cell = 0; cell < 16; cell++) ConveyorRuntimeTests.Insert(_world, target, cell, 0, cell);
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 101, 0, 3);
            ConveyorRuntimeTests.Insert(_world, source, 102, 1, 3);
            _world.Tick(1); _world.Tick(0);
            Assert.That(_world.TransferCount, Is.EqualTo(1));
            Assert.That(_world.VaporizedCount, Is.EqualTo(vaporize ? 1 : 0));
            Assert.That(_world.TryGetPayload(handle, out _, out _), Is.EqualTo(!vaporize));
            Assert.That(source.GetBlockReason(0), Is.EqualTo(vaporize ? ConveyorBlockReason.None : ConveyorBlockReason.Full));
            Assert.That(source.GetBlockReason(1), Is.EqualTo(ConveyorBlockReason.None));
        }

        [Test]
        public void IndexedModePreservesIdentityOnSuccessfulSameLoopTransfer()
        {
            ConveyorRuntime source = _world.CreateConveyor("a", ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, indexedAdmission: true),
                modules: new IConveyorModuleFactory[] { new TeleportModuleFactory("a", 0.25, 0.5) });
            ConveyorPayloadHandle handle = ConveyorRuntimeTests.Insert(_world, source, 1, 0, 3);
            _world.Tick(1); _world.Tick(0);
            _world.TryGetState(handle, out ConveyorTransportState state);
            Assert.That(state.Distance, Is.EqualTo(8).Within(0.00001));
            Assert.That(_world.TransferCount, Is.EqualTo(1));
            Assert.That(_world.Count, Is.EqualTo(1));
        }

        private ConveyorRuntime Create(string id, int lanes = 1)
            => _world.CreateConveyor(id, ConveyorRuntimeTests.Square(), new LogicalLaneTransportProvider(1, 1, indexedAdmission: true), lanes);

        private sealed class CountingVaporize : IConveyorTransferPolicy
        {
            internal int Calls;
            public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) { Calls++; return ConveyorOverflowDisposition.Vaporize; }
        }
    }
}
