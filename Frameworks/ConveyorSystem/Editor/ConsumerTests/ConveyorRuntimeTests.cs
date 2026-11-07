using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.ConsumerTests
{
    // Public contracts only: no friend access, production game, Common, or Editor assembly reference.
    public sealed class ConveyorRuntimeTests
    {
        private readonly List<ConveyorWorld> _worlds = new List<ConveyorWorld>();

        [TearDown]
        public void Cleanup()
        {
            foreach (ConveyorWorld world in _worlds)
            {
                Drain(world);
                world.Shutdown(ConveyorShutdownDisposition.Discard);
            }
            _worlds.Clear();
        }

        [Test]
        public void AdmissionReservationOwnsCapacityWithoutTakingProducerOwnership()
        {
            ConveyorWorld world = World(4);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            var request = Request(runtime, 123, 0, 1);
            Assert.That(world.TryReserveIngress(runtime, request, out ConveyorIngressReservation reservation), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            Assert.That(world.Count, Is.Zero);
            Assert.That(world.PendingAdmissions, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.1));
            Assert.That(world.IsFaulted, Is.False);
            Assert.That(world.CommitIngress(reservation, out ConveyorPayloadHandle handle), Is.True);
            Assert.That(world.CommitIngress(reservation, out _), Is.False);
            Assert.That(world.TryGetPayload(handle, out ConveyorPayloadDescriptor payload, out ConveyorRuntime owner), Is.True);
            Assert.That(payload.ConsumerKey, Is.EqualTo(123));
            Assert.That(owner, Is.SameAs(runtime));
        }

        [Test]
        public void CancelledForeignAndStaleReservationsCannotCommit()
        {
            ConveyorWorld first = World(1);
            ConveyorWorld second = World(1);
            ConveyorRuntime runtime = first.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            Assert.That(first.TryReserveIngress(runtime, Request(runtime, 1, 0, 1), out ConveyorIngressReservation reservation), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            Assert.That(second.CommitIngress(reservation, out _), Is.False);
            Assert.That(first.CancelIngress(reservation), Is.True);
            Assert.That(first.CancelIngress(reservation), Is.False);
            Assert.That(first.CommitIngress(reservation, out _), Is.False);
            Assert.That(Insert(first, runtime, 2, 0, 1).IsValid, Is.True);
        }

        [Test]
        public void GenerationAndWorldIdentityRejectReusedHandles()
        {
            ConveyorWorld world = World(1);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            ConveyorPayloadHandle old = Insert(world, runtime, 9, 0, 1);
            Assert.That(world.TryRemove(old), Is.True);
            ConveyorPayloadHandle current = Insert(world, runtime, 10, 0, 1);
            Assert.That(current.Slot, Is.EqualTo(old.Slot));
            Assert.That(current.Generation, Is.Not.EqualTo(old.Generation));
            Assert.That(world.TryGetPayload(old, out _, out _), Is.False);
            Assert.That(world.TryRemove(old), Is.False);
            Assert.That(World(1).TryRemove(current), Is.False);
        }

        [Test]
        public void MultiSlotFootprintsWrapWithoutOverlap()
        {
            ConveyorWorld world = World(8);
            var provider = new LogicalLaneTransportProvider(1, 1, new[] { new LogicalPayloadProfile(0, 1), new LogicalPayloadProfile(1, 3) });
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), provider);
            var large = new ConveyorPayloadDescriptor(1, 1);
            Assert.That(world.TryInsertAt(runtime, large, 0, 15.5, out ConveyorPayloadHandle handle), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            Assert.That(world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(2), 0, 0.5, out _), Is.EqualTo(ConveyorAdmissionStatus.Full));
            Assert.That(world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(3), 0, 2.0, out _), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            Assert.That(world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(4, 999), 0, 8, out _), Is.EqualTo(ConveyorAdmissionStatus.Unsupported));
            Assert.That(world.TryRemove(handle), Is.True);
        }

        [Test]
        public void StepProgressUsesTimeAndIndependentWorldState()
        {
            ConveyorWorld world = World(8);
            var provider = new LogicalLaneTransportProvider(2, 1);
            ConveyorRuntime first = world.CreateConveyor("a", Square(), provider);
            ConveyorRuntime second = world.CreateConveyor("b", Square(), provider);
            ConveyorPayloadHandle a = Insert(world, first, 1, 0, 1);
            ConveyorPayloadHandle b = Insert(world, second, 2, 0, 1);
            world.SetSpeed(first, 3);
            world.SetSuspended(second, true);
            world.Tick(0.5);
            world.TryGetState(a, out ConveyorTransportState stateA);
            world.TryGetState(b, out ConveyorTransportState stateB);
            Assert.That(stateA.Distance, Is.EqualTo(2.5).Within(0.000001));
            Assert.That(stateB.Distance, Is.EqualTo(1).Within(0.000001));
            Assert.Throws<InvalidOperationException>(() => world.CreateConveyor("a", Square(), provider, replaceEmpty: true));
        }

        [Test]
        public void BorrowedFramesExpireAndExplicitCopiesRemainValid()
        {
            ConveyorWorld world = World(4);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            Insert(world, runtime, 45, 0, 1);
            world.Publish();
            ConveyorTransportFrame frame = runtime.Frame;
            var copy = new ConveyorFrameItem[4];
            frame.CopyTo(copy);
            world.Tick(0.1);
            Assert.Throws<InvalidOperationException>(() => { _ = frame.Count; });
            Assert.That(copy[0].Payload.ConsumerKey, Is.EqualTo(45));
            Assert.That(runtime.Frame.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResultsBackpressurePreventsUnreportedInsertionAndRemoval()
        {
            ConveyorWorld world = World(2, 1);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            ConveyorPayloadHandle handle = Insert(world, runtime, 77, 0, 1);
            Assert.That(world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(78), 0, 3, out _), Is.EqualTo(ConveyorAdmissionStatus.Full));
            Assert.That(world.TryRemove(handle), Is.False);
            Assert.That(world.Count, Is.EqualTo(1));
            Assert.That(world.TryDequeueResult(out ConveyorResult inserted), Is.True);
            Assert.That(inserted.Kind, Is.EqualTo(ConveyorResultKind.Inserted));
            Assert.That(world.TryRemove(handle, ConveyorRemovalReason.Delivered), Is.True);
            Assert.That(world.TryDequeueResult(out ConveyorResult removed), Is.True);
            Assert.That(removed.Payload.ConsumerKey, Is.EqualTo(77));
            Assert.That(removed.RemovalReason, Is.EqualTo(ConveyorRemovalReason.Delivered));
            Assert.That(world.DeliveredCount, Is.EqualTo(1));
        }

        [Test]
        public void CallbacksCannotReenterButCanQueueBoundedNextTickCommands()
        {
            ConveyorWorld world = World(4);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            ConveyorPayloadHandle handle = Insert(world, runtime, 1, 0, 1);
            world.DispatchResults(result =>
            {
                Assert.Throws<InvalidOperationException>(() => world.TryRemove(handle));
                Assert.Throws<InvalidOperationException>(() => world.Tick(0));
                Assert.That(world.TryEnqueue(next => next.TryRemove(handle)), Is.True);
            });
            Assert.That(world.Count, Is.EqualTo(1));
            world.Tick(0);
            Assert.That(world.Count, Is.Zero);
            Assert.That(world.RemovedCount, Is.EqualTo(1));
        }

        [Test]
        public void ObserverFailureRetainsCurrentResultAndDoesNotFaultSimulation()
        {
            ConveyorWorld world = World(2);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            Insert(world, runtime, 91, 0, 1);
            Assert.Throws<Exception>(() => world.DispatchResults(result => throw new Exception("consumer failure")));
            Assert.That(world.ResultCount, Is.EqualTo(1));
            Assert.That(world.IsFaulted, Is.False);
            Assert.That(world.TryDequeueResult(out ConveyorResult preserved), Is.True);
            Assert.That(preserved.Payload.ConsumerKey, Is.EqualTo(91));
        }

        [Test]
        public void ClockOwnerPreventsAnAccidentalSecondDriver()
        {
            ConveyorWorld world = World(1);
            object owner = new object();
            world.ClaimClock(owner);
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.1));
            Assert.Throws<InvalidOperationException>(() => world.ClaimClock(new object()));
            world.BeginStep(0.1, owner);
            Assert.Throws<InvalidOperationException>(() => world.CompleteStep(new object()));
            world.CompleteStep(owner);
            Assert.That(world.TickIndex, Is.EqualTo(1));
            world.ReleaseClock(owner);
            world.Tick(0.1);
        }

        [Test]
        public void ExternalBackendRequiresCompletionBarrierAndPublishesAfterIt()
        {
            ConveyorWorld world = World(4);
            var provider = new TrackingProvider(ConveyorStepDriver.External);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), provider);
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.1));
            long version = runtime.Frame.Version;
            world.BeginStep(0.1);
            Assert.That(provider.Created.PrepareCount, Is.EqualTo(1));
            Assert.That(provider.Created.CompleteCount, Is.Zero);
            Assert.That(runtime.Frame.Version, Is.EqualTo(version));
            Assert.Throws<InvalidOperationException>(() => world.Publish());
            world.CompleteStep();
            Assert.That(provider.Created.CompleteCount, Is.EqualTo(1));
            Assert.That(runtime.Frame.Version, Is.GreaterThan(version));
            world.RemoveConveyor(runtime);
            Assert.That(provider.Created.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void UnsupportedModuleIsDisposedWithoutRegisteringOrMutatingWorld()
        {
            ConveyorWorld world = World(4);
            var provider = new TrackingProvider();
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), provider);
            Assert.Throws<NotSupportedException>(() => world.AttachModule(runtime, new TeleportModuleFactory("b", 0.25, 0.5)));
            Assert.That(runtime.Count, Is.Zero);
            Assert.That(world.IsFaulted, Is.False);
            world.Tick(0.1);
        }

        [Test]
        public void CommandQueueIsBoundedAndCommandsQueuedDuringExecutionWaitOneTick()
        {
            ConveyorWorld world = World(2, commands: 1);
            int executed = 0;
            Assert.That(world.TryEnqueue(current =>
            {
                executed++;
                Assert.Throws<InvalidOperationException>(() => current.Tick(0));
                Assert.That(current.TryEnqueue(next => executed++), Is.True);
            }), Is.True);
            Assert.That(world.TryEnqueue(current => executed += 100), Is.False);
            world.Tick(0);
            Assert.That(executed, Is.EqualTo(1));
            world.Tick(0);
            Assert.That(executed, Is.EqualTo(2));
        }

        [Test]
        public void ReusedBackendFactoryCannotDisposeAnotherConveyorsLiveBackend()
        {
            ConveyorWorld world = World(4);
            var provider = new TrackingProvider();
            ConveyorRuntime first = world.CreateConveyor("a", Square(), provider);
            ConveyorPayloadHandle handle = Insert(world, first, 1, 0, 1);
            Assert.Throws<InvalidOperationException>(() => world.CreateConveyor("b", Square(), new ExistingBackendProvider(provider.Created)));
            Assert.That(provider.Created.DisposeCount, Is.Zero);
            world.Tick(0.1);
            Assert.That(first.Count, Is.EqualTo(1));
            world.TryRemove(handle);
            world.RemoveConveyor(first);
            Assert.That(provider.Created.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void QueuedExternalBackendCannotBeCompletedBySynchronousTick()
        {
            ConveyorWorld world = World(2);
            var provider = new TrackingProvider(ConveyorStepDriver.External);
            world.TryEnqueue(current => current.CreateConveyor("external", Square(), provider));
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.1));
            Assert.That(provider.Created.PrepareCount, Is.Zero);
            Assert.That(provider.Created.CompleteCount, Is.Zero);
            Assert.That(world.IsFaulted, Is.True);
        }

        [Test]
        public void ShutdownRequiresDispositionAndPreservesTerminalIdentity()
        {
            ConveyorWorld world = World(2);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider());
            Insert(world, runtime, 501, 0, 1);
            Drain(world);
            Assert.Throws<InvalidOperationException>(() => world.Dispose());
            world.Shutdown(ConveyorShutdownDisposition.Discard);
            Assert.That(world.IsDisposed, Is.True);
            Assert.That(world.TryDequeueResult(out ConveyorResult result), Is.True);
            Assert.That(result.Payload.ConsumerKey, Is.EqualTo(501));
            Assert.That(result.RemovalReason, Is.EqualTo(ConveyorRemovalReason.Shutdown));
            Assert.Throws<ObjectDisposedException>(() => world.Tick(0));
        }

        [Test]
        public void RandomizedInsertRemoveAndWrapConservesPayloads()
        {
            ConveyorWorld world = World(32);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider(1, 1), 2);
            var handles = new List<ConveyorPayloadHandle>();
            var random = new System.Random(4209);
            for (int operation = 0; operation < 1500; operation++)
            {
                if (handles.Count > 0 && random.Next(3) == 0)
                {
                    int index = random.Next(handles.Count);
                    Assert.That(world.TryRemove(handles[index]), Is.True);
                    handles.RemoveAt(index);
                }
                else if (world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(operation), random.Next(2), random.NextDouble() * 16,
                    out ConveyorPayloadHandle handle) == ConveyorAdmissionStatus.Accepted) handles.Add(handle);
                Drain(world);
                world.Tick(0.125);
                Assert.That(world.Count, Is.EqualTo(handles.Count));
                Assert.That(world.Count, Is.EqualTo(world.AcceptedCount - world.RemovedCount - world.DeliveredCount - world.VaporizedCount));
            }
        }

        [Test]
        public void WarmedHeadlessTicksReuseTheirManagedBuffers()
        {
            ConveyorWorld world = World(32);
            ConveyorRuntime runtime = world.CreateConveyor("a", Square(), new LogicalLaneTransportProvider(1, 1), 2);
            for (int channel = 0; channel < 2; channel++)
                for (int slot = 0; slot < 8; slot++) Insert(world, runtime, channel * 8 + slot, channel, slot * 2);
            Drain(world);
            for (int index = 0; index < 16; index++) world.Tick(1.0 / 60);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 64; index++) world.Tick(1.0 / 60);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero, "Steady-state logical movement/publication must reuse preallocated managed buffers.");
        }

        private ConveyorWorld World(int capacity, int results = 256, int commands = 128)
        {
            var world = new ConveyorWorld(capacity, results, commands);
            _worlds.Add(world);
            return world;
        }

        public static ConveyorPath Square(float side = 4, float xOffset = 0) => new ConveyorPath(new[]
        {
            new Vector3(xOffset, 0, 0), new Vector3(xOffset + side, 0, 0),
            new Vector3(xOffset + side, 0, side), new Vector3(xOffset, 0, side)
        }, Vector3.up);

        public static ConveyorAdmissionRequest Request(ConveyorRuntime runtime, long key, int channel, double distance)
            => new ConveyorAdmissionRequest(new ConveyorPayloadDescriptor(key), runtime.Path.EvaluateLane(distance, channel, runtime.Channels, runtime.Width), channel, distance);

        public static ConveyorPayloadHandle Insert(ConveyorWorld world, ConveyorRuntime runtime, long key, int channel, double distance)
        {
            Assert.That(world.TryInsert(runtime, Request(runtime, key, channel, distance), out ConveyorPayloadHandle handle), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            return handle;
        }

        public static void Drain(ConveyorWorld world) { while (world.TryDequeueResult(out _)) { } }

        private sealed class TrackingProvider : IConveyorTransportProvider
        {
            private readonly ConveyorStepDriver _driver;
            internal TrackingBackend Created;
            internal TrackingProvider(ConveyorStepDriver driver = ConveyorStepDriver.Synchronous) { _driver = driver; }
            public IConveyorTransportBackend Create(in ConveyorTransportContext context)
            {
                Created = new TrackingBackend(new LogicalLaneTransportProvider().Create(context), _driver);
                return Created;
            }
        }

        private sealed class ExistingBackendProvider : IConveyorTransportProvider
        {
            private readonly IConveyorTransportBackend _backend;
            internal ExistingBackendProvider(IConveyorTransportBackend backend) { _backend = backend; }
            public IConveyorTransportBackend Create(in ConveyorTransportContext context) => _backend;
        }

        private sealed class TrackingBackend : IConveyorTransportBackend
        {
            private readonly IConveyorTransportBackend _inner;
            public ConveyorTransportCapabilities Capabilities => ConveyorTransportCapabilities.PosePublication | ConveyorTransportCapabilities.AdmissionReservations;
            public ConveyorStepDriver StepDriver { get; }
            public int Count => _inner.Count;
            internal int PrepareCount, CompleteCount, DisposeCount;
            internal TrackingBackend(IConveyorTransportBackend inner, ConveyorStepDriver driver) { _inner = inner; StepDriver = driver; }
            public ConveyorAdmissionStatus TryReserve(in ConveyorAdmissionRequest request, out ConveyorBackendReservation reservation) => _inner.TryReserve(request, out reservation);
            public bool IsReservationValid(in ConveyorBackendReservation reservation) => _inner.IsReservationValid(reservation);
            public void CancelReservation(in ConveyorBackendReservation reservation) => _inner.CancelReservation(reservation);
            public void CommitAdmission(in ConveyorBackendReservation reservation, ConveyorPayloadHandle handle) => _inner.CommitAdmission(reservation, handle);
            public void Remove(ConveyorPayloadHandle handle) => _inner.Remove(handle);
            public bool TryGetState(ConveyorPayloadHandle handle, out ConveyorTransportState state) => _inner.TryGetState(handle, out state);
            public int CopyStates(ConveyorTransportState[] destination) => _inner.CopyStates(destination);
            public void PrepareStep(double deltaTime) { PrepareCount++; _inner.PrepareStep(deltaTime); }
            public void CompleteStep() { CompleteCount++; _inner.CompleteStep(); }
            public void SetSuspended(bool suspended) => _inner.SetSuspended(suspended);
            public void Dispose() { DisposeCount++; _inner.Dispose(); }
        }
    }
}
