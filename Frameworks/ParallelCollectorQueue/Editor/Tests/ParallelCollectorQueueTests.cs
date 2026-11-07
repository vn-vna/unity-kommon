using System;
using System.Collections.Generic;
using NUnit.Framework;
using QueueModel = Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue.ParallelCollectorQueue;

namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue.Tests
{
    public sealed class ParallelCollectorQueueTests
    {
        [Test]
        public void StartupDistributesOnlyPrefixRoundRobin()
        {
            var queue = new QueueModel(Definitions(24));
            Assert.That(queue.ReserveCount, Is.EqualTo(12));
            Assert.That(queue.PendingEventCount, Is.Zero, "Initialization is queried, not replayed as notifications.");
            for (int lane = 0; lane < 4; lane++)
            {
                for (int position = 0; position < 3; position++)
                    Assert.That(queue.GetQueuedCollector(lane, position).Definition.Id, Is.EqualTo(lane + 4 * position));
            }
            Assert.That(queue.GetReserveCollector(0).Definition.Id, Is.EqualTo(12));
            AssertOwnership(queue);
        }

        [Test]
        public void TenCollectorsAllowUnevenQueuesAndNoReserve()
        {
            var queue = new QueueModel(Definitions(10));
            Assert.That(queue.ReserveCount, Is.Zero);
            Assert.That(queue.GetQueueCount(0), Is.EqualTo(3));
            Assert.That(queue.GetQueueCount(1), Is.EqualTo(3));
            Assert.That(queue.GetQueueCount(2), Is.EqualTo(2));
            Assert.That(queue.GetQueueCount(3), Is.EqualTo(2));
            AssertOwnership(queue);
        }

        [Test]
        public void EmptyInputIsCompletedWithoutInventingAnEvent()
        {
            var queue = new QueueModel(Array.Empty<CollectorDefinition>());
            Assert.That(queue.IsCompleted, Is.True);
            Assert.That(queue.RemainingCollectorCount, Is.Zero);
            Assert.That(queue.TotalRequiredUnits, Is.Zero);
            Assert.That(queue.PendingEventCount, Is.Zero);
            Assert.That(queue.TryGetFront(0, out CollectorSnapshot front), Is.False);
            Assert.That(front.Handle.IsValid, Is.False);
            Assert.That(front.IsFilled, Is.False);
            Assert.That(queue.TryCollect(0, default, 0, 1, out int units), Is.EqualTo(CollectorCollectionStatus.Empty));
            Assert.That(units, Is.Zero);
        }

        [Test]
        public void DefinitionsAreCopiedAndSnapshotsDoNotChangeAfterCollection()
        {
            CollectorDefinition[] definitions = Definitions(2);
            var queue = new QueueModel(definitions, queueCount: 1, targetDepth: 1);
            queue.TryGetFront(0, out CollectorSnapshot before);
            definitions[0] = new CollectorDefinition(99, 99, 99);
            Assert.That(queue.TryCollect(0, before.Handle, before.Definition.TypeId, 1, out _), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(before.RemainingUnits, Is.EqualTo(3));
            Assert.That(queue.GetCollectorAt(0).RemainingUnits, Is.EqualTo(2));
            Assert.That(queue.GetCollectorAt(0).Definition.Id, Is.Zero);
        }

        [Test]
        public void PartialCollectionCreditsExactlyAndEmitsNoLifecycleTransitions()
        {
            var queue = new QueueModel(new[] { new CollectorDefinition(7, 11, 10) });
            queue.TryGetFront(0, out CollectorSnapshot front);
            Assert.That(queue.TryCollect(0, front.Handle, 11, 4, out int accepted), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(accepted, Is.EqualTo(4));
            Assert.That(queue.CollectedUnits, Is.EqualTo(4));
            Assert.That(queue.GetCollectorAt(0).RemainingUnits, Is.EqualTo(6));
            Assert.That(queue.FilledCount, Is.Zero);
            Assert.That(queue.RemovedCount, Is.Zero);
            List<CollectorQueueEvent> events = Drain(queue);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(CollectorQueueEventKind.CargoCollected));
            Assert.That(events[0].Units, Is.EqualTo(4));
            Assert.That(events[0].Collector.CreditedUnits, Is.EqualTo(4));
        }

        [Test]
        public void ExcessOfferStopsAtNamedFrontAndDoesNotCreditSuccessor()
        {
            var queue = new QueueModel(new[] { new CollectorDefinition(10, 4, 2), new CollectorDefinition(20, 4, 5) },
                queueCount: 1, targetDepth: 1);
            queue.TryGetFront(0, out CollectorSnapshot oldFront);
            Assert.That(queue.TryCollect(0, oldFront.Handle, 4, int.MaxValue, out int accepted), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(accepted, Is.EqualTo(2));
            Assert.That(queue.CollectedUnits, Is.EqualTo(2));
            Assert.That(queue.GetCollectorAt(1).RemainingUnits, Is.EqualTo(5));
            Assert.That(queue.GetQueuedCollector(0, 0).Definition.Id, Is.EqualTo(20));
            Assert.That(queue.TryCollect(0, oldFront.Handle, 4, 1, out _), Is.EqualTo(CollectorCollectionStatus.StaleCollector));
            Assert.That(queue.CollectedUnits, Is.EqualTo(2));
        }

        [Test]
        public void RemovalRefillsBeforeFrontChangeAndNeverReportsFalseCompletion()
        {
            var queue = new QueueModel(Definitions(4), queueCount: 1, targetDepth: 1);
            CollectFront(queue, 0);
            List<CollectorQueueEvent> events = Drain(queue);
            CollectionAssert.AreEqual(new[]
            {
                CollectorQueueEventKind.CargoCollected, CollectorQueueEventKind.CollectorFilled,
                CollectorQueueEventKind.CollectorRemoved, CollectorQueueEventKind.CollectorQueued,
                CollectorQueueEventKind.FrontChanged
            }, events.ConvertAll(result => result.Kind));
            Assert.That(events[4].Collector.Definition.Id, Is.EqualTo(1));
            Assert.That(events[4].PreviousFront.Id, Is.Zero);
            Assert.That(queue.RemainingCollectorCount, Is.EqualTo(3));
            Assert.That(queue.IsCompleted, Is.False);
            AssertOwnership(queue);
        }

        [Test]
        public void FinalRemovalEmitsOneEmptyFrontAndOneCompletion()
        {
            var queue = new QueueModel(Definitions(1), queueCount: 1);
            CollectFront(queue, 0);
            List<CollectorQueueEvent> events = Drain(queue);
            Assert.That(events.FindAll(result => result.Kind == CollectorQueueEventKind.Completed).Count, Is.EqualTo(1));
            CollectorQueueEvent change = events.Find(result => result.Kind == CollectorQueueEventKind.FrontChanged);
            Assert.That(change.Collector.Handle.IsValid, Is.False);
            Assert.That(change.PreviousFront.IsValid, Is.True);
            Assert.That(events[events.Count - 1].Kind, Is.EqualTo(CollectorQueueEventKind.Completed));
            Assert.That(queue.IsCompleted, Is.True);
            Assert.That(queue.TryCollect(0, change.PreviousFront, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.Empty));
            Assert.That(queue.PendingEventCount, Is.Zero);
        }

        [Test]
        public void TypeBackRowBlockedAndForeignHandleOffersAreRejectedWithoutMutation()
        {
            var queue = new QueueModel(Definitions(8));
            var other = new QueueModel(Definitions(8));
            queue.TryGetFront(0, out CollectorSnapshot front);
            other.TryGetFront(0, out CollectorSnapshot foreign);
            Assert.That(queue.TryCollect(0, front.Handle, 999, 1, out _), Is.EqualTo(CollectorCollectionStatus.TypeMismatch));
            Assert.That(queue.TryCollect(0, queue.GetQueuedCollector(0, 1).Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.StaleCollector));
            Assert.That(queue.TryCollect(0, foreign.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.StaleCollector));
            Assert.That(queue.TryCollect(0, default, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.StaleCollector));
            queue.SetQueueBlocked(0, true);
            Assert.That(queue.IsQueueBlocked(0), Is.True);
            Assert.That(queue.TryCollect(0, front.Handle, front.Definition.TypeId, 1, out _), Is.EqualTo(CollectorCollectionStatus.Blocked));
            Assert.That(queue.CollectedUnits, Is.Zero);
            Assert.That(queue.PendingEventCount, Is.Zero);
            Assert.That(queue.RemovedCount, Is.Zero);
            queue.SetQueueBlocked(0, false);
            Assert.That(queue.TryCollect(0, front.Handle, front.Definition.TypeId, 1, out _), Is.EqualTo(CollectorCollectionStatus.Accepted));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonpositiveOfferDoesNotChangeAnything(int units)
        {
            var queue = new QueueModel(Definitions(1));
            queue.TryGetFront(0, out CollectorSnapshot front);
            Assert.That(queue.TryCollect(0, front.Handle, 0, units, out int accepted), Is.EqualTo(CollectorCollectionStatus.InvalidQuantity));
            Assert.That(accepted, Is.Zero);
            Assert.That(queue.CollectedUnits, Is.Zero);
            Assert.That(queue.PendingEventCount, Is.Zero);
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void InvalidQueueOfferIsTypedButInvalidQueryThrows(int lane)
        {
            var queue = new QueueModel(Definitions(1));
            Assert.That(queue.TryCollect(lane, default, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.InvalidQueue));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.GetQueueCount(lane));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.SetQueueBlocked(lane, true));
        }

        [Test]
        public void InvalidSnapshotQueriesAreExplicit()
        {
            var queue = new QueueModel(Definitions(2));
            Assert.That(queue.TryGetCollector(1234, out CollectorSnapshot missing), Is.False);
            Assert.That(missing.Handle.IsValid, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.GetCollectorAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.GetCollectorAt(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.GetReserveCollector(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.GetQueuedCollector(0, 1));
        }

        [Test]
        public void DefaultDuplicateAndInvalidSettingsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new QueueModel(null));
            Assert.Throws<ArgumentException>(() => new QueueModel(new[] { default(CollectorDefinition) }));
            Assert.Throws<ArgumentException>(() => new QueueModel(new[] { new CollectorDefinition(1, 0, 1), new CollectorDefinition(1, 1, 2) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueueModel(Definitions(1), queueCount: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueueModel(Definitions(1), targetDepth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueueModel(Definitions(1), eventCapacity: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueueModel(Definitions(1), eventCapacity: 12));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectorDefinition(-1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectorDefinition(0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectorDefinition(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectorDefinition(0, 0, 1, -2));
        }

        [Test]
        public void LinkedGroupsMustContainExactlyTwoMembersInMultipleQueues()
        {
            Assert.Throws<ArgumentException>(() => new QueueModel(new[] { new CollectorDefinition(0, 0, 1, 7) }));
            Assert.Throws<ArgumentException>(() => new QueueModel(new[]
            {
                new CollectorDefinition(0, 0, 1, 7), new CollectorDefinition(1, 0, 1, 7), new CollectorDefinition(2, 0, 1, 7)
            }));
            Assert.Throws<ArgumentException>(() => new QueueModel(new[]
            {
                new CollectorDefinition(0, 0, 1, 7), new CollectorDefinition(1, 0, 1, 7)
            }, queueCount: 1));
        }

        [Test]
        public void FilledPartnerStaysQueuedAndCannotReceiveMoreCargo()
        {
            var queue = new QueueModel(PairDefinitions(), queueCount: 2, targetDepth: 1);
            queue.TryGetFront(0, out CollectorSnapshot first);
            CollectFront(queue, 0);
            Assert.That(queue.FilledCount, Is.EqualTo(1));
            Assert.That(queue.RemovedCount, Is.Zero);
            Assert.That(queue.CollectedUnits, Is.EqualTo(2));
            Assert.That(queue.TryGetFront(0, out CollectorSnapshot held), Is.True);
            Assert.That(held.Handle, Is.EqualTo(first.Handle));
            Assert.That(held.IsHeld, Is.True);
            Assert.That(queue.TryCollect(0, held.Handle, 1, 1, out _), Is.EqualTo(CollectorCollectionStatus.FilledHeld));
            List<CollectorQueueEvent> events = Drain(queue);
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[1].Kind, Is.EqualTo(CollectorQueueEventKind.CollectorFilled));
            Assert.That(queue.RemainingCollectorCount, Is.EqualTo(4));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalPartnerReleasesBothInAuthoredOrderBeforeRefillEvents(bool reverse)
        {
            var queue = new QueueModel(PairDefinitions(), queueCount: 2, targetDepth: 1);
            CollectFront(queue, reverse ? 1 : 0);
            Drain(queue);
            CollectFront(queue, reverse ? 0 : 1);
            List<CollectorQueueEvent> events = Drain(queue);
            List<CollectorQueueEvent> removed = events.FindAll(result => result.Kind == CollectorQueueEventKind.CollectorRemoved);
            Assert.That(removed.Count, Is.EqualTo(2));
            Assert.That(removed[0].Collector.Definition.Id, Is.EqualTo(10));
            Assert.That(removed[1].Collector.Definition.Id, Is.EqualTo(20));
            Assert.That(removed[0].Collector.Location, Is.EqualTo(CollectorLocation.Removed));
            Assert.That(queue.FilledCount, Is.EqualTo(2));
            Assert.That(queue.RemovedCount, Is.EqualTo(2));
            Assert.That(queue.GetQueuedCollector(1, 0).Definition.Id, Is.EqualTo(30), "Refill is last queue to first.");
            Assert.That(queue.GetQueuedCollector(0, 0).Definition.Id, Is.EqualTo(40));
            Assert.That(queue.IsCompleted, Is.False);
            AssertOwnership(queue);
        }

        [Test]
        public void NonadjacentReservePartnerIsCoAdmittedAndDepthIsNotAHardCap()
        {
            CollectorDefinition[] definitions = Definitions(16);
            definitions[12] = new CollectorDefinition(12, 0, 1, 99);
            definitions[15] = new CollectorDefinition(15, 1, 1, 99);
            var queue = new QueueModel(definitions);
            CollectFront(queue, 0);
            Assert.That(queue.GetQueuedCollector(0, 2).Definition.Id, Is.EqualTo(12));
            Assert.That(queue.GetQueueCount(1), Is.EqualTo(4));
            Assert.That(queue.GetQueuedCollector(1, 3).Definition.Id, Is.EqualTo(15));
            Assert.That(queue.ReserveCount, Is.EqualTo(2));
            Assert.That(queue.GetReserveCollector(0).Definition.Id, Is.EqualTo(13));
            Assert.That(queue.GetReserveCollector(1).Definition.Id, Is.EqualTo(14));
            AssertOwnership(queue);
        }

        [Test]
        public void PairCrossingInitialPrefixIsNeverStrandedInReserve()
        {
            CollectorDefinition[] definitions = Definitions(14);
            definitions[11] = new CollectorDefinition(11, 1, 1, 5);
            definitions[13] = new CollectorDefinition(13, 2, 1, 5);
            var queue = new QueueModel(definitions);
            Assert.That(queue.GetCollectorAt(11).Location, Is.EqualTo(CollectorLocation.Queue));
            Assert.That(queue.GetCollectorAt(13).Location, Is.EqualTo(CollectorLocation.Queue));
            Assert.That(queue.GetCollectorAt(11).QueueIndex, Is.Not.EqualTo(queue.GetCollectorAt(13).QueueIndex));
            Assert.That(queue.GetQueueCount(0), Is.EqualTo(4));
            Assert.That(queue.GetReserveCollector(0).Definition.Id, Is.EqualTo(12));
            AssertOwnership(queue);
        }

        [Test]
        public void FullNotificationBufferRejectsBeforeMutationAndCanResumeAfterDrain()
        {
            var queue = new QueueModel(new[] { new CollectorDefinition(0, 0, 100) }, queueCount: 1, eventCapacity: 10);
            queue.TryGetFront(0, out CollectorSnapshot front);
            for (int i = 0; i < 10; i++)
                Assert.That(queue.TryCollect(0, front.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(queue.TryCollect(0, front.Handle, 0, 1, out int accepted), Is.EqualTo(CollectorCollectionStatus.EventBufferFull));
            Assert.That(accepted, Is.Zero);
            Assert.That(queue.CollectedUnits, Is.EqualTo(10));
            Assert.That(queue.PendingEventCount, Is.EqualTo(10));
            Assert.That(queue.TryDequeueEvent(out CollectorQueueEvent first), Is.True);
            Assert.That(first.Sequence, Is.EqualTo(1));
            Assert.That(queue.TryCollect(0, front.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.Accepted));
            List<CollectorQueueEvent> events = Drain(queue);
            for (int i = 0; i < events.Count; i++) Assert.That(events[i].Sequence, Is.EqualTo(i + 2));
            Assert.That(queue.CollectedUnits, Is.EqualTo(11));
        }

        [Test]
        public void CompletingOfferUsesConservativeEventBudgetWithoutPartialMutation()
        {
            var queue = new QueueModel(new[] { new CollectorDefinition(0, 0, 6) }, queueCount: 1, eventCapacity: 10);
            queue.TryGetFront(0, out CollectorSnapshot front);
            for (int i = 0; i < 5; i++) queue.TryCollect(0, front.Handle, 0, 1, out _);
            Assert.That(queue.TryCollect(0, front.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.EventBufferFull));
            Assert.That(queue.GetCollectorAt(0).RemainingUnits, Is.EqualTo(1));
            Assert.That(queue.FilledCount, Is.Zero);
            Drain(queue);
            CollectFront(queue, 0);
            Assert.That(queue.IsCompleted, Is.True);
        }

        [Test]
        public void DispatchObserversSeeStableStateAndCannotReenterMutationOrPolling()
        {
            var queue = new QueueModel(Definitions(3), queueCount: 1, targetDepth: 1);
            CollectFront(queue, 0);
            int observed = 0;
            queue.EventPublished += result =>
            {
                observed++;
                Assert.That(queue.GetQueuedCollector(0, 0).Definition.Id, Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => queue.SetQueueBlocked(0, true));
                Assert.Throws<InvalidOperationException>(() => queue.TryDequeueEvent(out _));
                Assert.Throws<InvalidOperationException>(() => queue.DispatchEvents());
                Assert.Throws<InvalidOperationException>(() => queue.TryCollect(0, result.Collector.Handle, 0, 1, out _));
            };
            Assert.That(observed, Is.Zero, "Collection never invokes an external observer inside commit.");
            Assert.That(queue.DispatchEvents(), Is.EqualTo(5));
            Assert.That(observed, Is.EqualTo(5));
            Assert.That(queue.PendingEventCount, Is.Zero);
            Assert.That(queue.IsQueueBlocked(0), Is.False);
        }

        [Test]
        public void ThrowingObserverRetainsCurrentEventAndReleasesDispatchGuard()
        {
            var queue = new QueueModel(Definitions(1));
            queue.TryGetFront(0, out CollectorSnapshot front);
            queue.TryCollect(0, front.Handle, 0, 1, out _);
            Action<CollectorQueueEvent> throwing = result => throw new InvalidOperationException("consumer failed");
            queue.EventPublished += throwing;
            Assert.Throws<InvalidOperationException>(() => queue.DispatchEvents());
            Assert.That(queue.PendingEventCount, Is.EqualTo(1));
            Assert.That(queue.CollectedUnits, Is.EqualTo(1));
            queue.EventPublished -= throwing;
            Assert.That(queue.DispatchEvents(), Is.Zero, "No subscribers does not silently discard facts.");
            Assert.That(queue.TryDequeueEvent(out CollectorQueueEvent result), Is.True);
            Assert.That(result.Sequence, Is.EqualTo(1));
            queue.SetQueueBlocked(0, true);
        }

        [Test]
        public void LargeQuantitiesAccumulateInLongCounters()
        {
            var queue = new QueueModel(new[]
            {
                new CollectorDefinition(1, 0, int.MaxValue), new CollectorDefinition(2, 1, int.MaxValue)
            }, queueCount: 2);
            Assert.That(queue.TotalRequiredUnits, Is.EqualTo(2L * int.MaxValue));
            CollectFront(queue, 0);
            Drain(queue);
            CollectFront(queue, 1);
            Assert.That(queue.CollectedUnits, Is.EqualTo(2L * int.MaxValue));
            Assert.That(queue.IsCompleted, Is.True);
        }

        [Test]
        public void WarmPartialCollectionAndEventPollingAllocateNoManagedMemory()
        {
            var queue = new QueueModel(new[] { new CollectorDefinition(0, 0, 10000) }, queueCount: 1);
            queue.TryGetFront(0, out CollectorSnapshot front);
            queue.TryCollect(0, front.Handle, 0, 1, out _);
            queue.TryDequeueEvent(out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                queue.TryCollect(0, front.Handle, 0, 1, out _);
                queue.TryDequeueEvent(out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Excludes construction, presentation and consumer logging.");
        }

        [TestCase(1)]
        [TestCase(17)]
        [TestCase(921)]
        public void SeededMixedPairsDrainWithoutCyclesAndConservePerType(int seed)
        {
            var random = new Random(seed);
            for (int iteration = 0; iteration < 15; iteration++)
            {
                var definitions = new List<CollectorDefinition>();
                for (int id = 0; id < 40; id++) definitions.Add(new CollectorDefinition(id, id % 12, random.Next(1, 8), id < 24 ? id / 2 : -1));
                for (int index = definitions.Count - 1; index > 0; index--)
                {
                    int other = random.Next(index + 1);
                    CollectorDefinition temporary = definitions[index];
                    definitions[index] = definitions[other];
                    definitions[other] = temporary;
                }
                var queue = new QueueModel(definitions, queueCount: 2 + random.Next(5), targetDepth: 1 + random.Next(4));
                int commands = 0;
                while (!queue.IsCompleted)
                {
                    int lane = FindEligibleFront(queue);
                    Assert.That(lane, Is.GreaterThanOrEqualTo(0), "Paired admission must not create crossed hold cycles.");
                    queue.TryGetFront(lane, out CollectorSnapshot front);
                    int offer = random.Next(1, 10);
                    Assert.That(queue.TryCollect(lane, front.Handle, front.Definition.TypeId, offer, out int accepted), Is.EqualTo(CollectorCollectionStatus.Accepted));
                    Assert.That(accepted, Is.InRange(1, Math.Min(offer, front.RemainingUnits)));
                    Assert.That(queue.PendingEventCount, Is.LessThanOrEqualTo(queue.EventCapacity));
                    Drain(queue);
                    AssertOwnership(queue);
                    Assert.That(++commands, Is.LessThan(400));
                }
                Assert.That(queue.FilledCount, Is.EqualTo(40));
                Assert.That(queue.RemovedCount, Is.EqualTo(40));
                Assert.That(queue.CollectedUnits, Is.EqualTo(queue.TotalRequiredUnits));
            }
        }

        private static CollectorDefinition[] Definitions(int count)
        {
            var result = new CollectorDefinition[count];
            for (int index = 0; index < count; index++) result[index] = new CollectorDefinition(index, index % 12, 3 + index % 5);
            return result;
        }

        private static CollectorDefinition[] PairDefinitions() => new[]
        {
            new CollectorDefinition(10, 1, 2, 9), new CollectorDefinition(20, 2, 3, 9),
            new CollectorDefinition(30, 1, 4), new CollectorDefinition(40, 2, 5)
        };

        private static void CollectFront(QueueModel queue, int lane)
        {
            Assert.That(queue.TryGetFront(lane, out CollectorSnapshot front), Is.True);
            Assert.That(queue.TryCollect(lane, front.Handle, front.Definition.TypeId, front.RemainingUnits, out int accepted), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(accepted, Is.EqualTo(front.RemainingUnits));
        }

        private static List<CollectorQueueEvent> Drain(QueueModel queue)
        {
            var results = new List<CollectorQueueEvent>();
            while (queue.TryDequeueEvent(out CollectorQueueEvent result)) results.Add(result);
            return results;
        }

        private static int FindEligibleFront(QueueModel queue)
        {
            for (int lane = 0; lane < queue.QueueCount; lane++)
            {
                if (queue.TryGetFront(lane, out CollectorSnapshot front) && !front.IsHeld) return lane;
            }
            return -1;
        }

        private static void AssertOwnership(QueueModel queue)
        {
            var ids = new HashSet<int>();
            for (int position = 0; position < queue.ReserveCount; position++)
            {
                CollectorSnapshot record = queue.GetReserveCollector(position);
                Assert.That(record.Location, Is.EqualTo(CollectorLocation.Reserve));
                Assert.That(record.QueueIndex, Is.EqualTo(-1));
                Assert.That(ids.Add(record.Definition.Id), Is.True);
            }
            for (int lane = 0; lane < queue.QueueCount; lane++)
            {
                for (int position = 0; position < queue.GetQueueCount(lane); position++)
                {
                    CollectorSnapshot record = queue.GetQueuedCollector(lane, position);
                    Assert.That(record.Location, Is.EqualTo(CollectorLocation.Queue));
                    Assert.That(record.QueueIndex, Is.EqualTo(lane));
                    Assert.That(ids.Add(record.Definition.Id), Is.True);
                }
            }
            long remaining = 0;
            var requiredByType = new Dictionary<int, long>();
            var creditedByType = new Dictionary<int, long>();
            var remainingByType = new Dictionary<int, long>();
            for (int index = 0; index < queue.CollectorCount; index++)
            {
                CollectorSnapshot record = queue.GetCollectorAt(index);
                Assert.That(record.RemainingUnits, Is.InRange(0, record.Definition.RequiredUnits));
                remaining += record.RemainingUnits;
                int type = record.Definition.TypeId;
                requiredByType.TryGetValue(type, out long required);
                creditedByType.TryGetValue(type, out long credited);
                remainingByType.TryGetValue(type, out long pending);
                requiredByType[type] = required + record.Definition.RequiredUnits;
                creditedByType[type] = credited + record.CreditedUnits;
                remainingByType[type] = pending + record.RemainingUnits;
                if (record.Location == CollectorLocation.Removed)
                {
                    Assert.That(record.IsFilled, Is.True);
                    Assert.That(ids.Add(record.Definition.Id), Is.True);
                }
            }
            Assert.That(ids.Count, Is.EqualTo(queue.CollectorCount));
            Assert.That(queue.CollectedUnits + remaining, Is.EqualTo(queue.TotalRequiredUnits));
            foreach (int type in requiredByType.Keys)
                Assert.That(creditedByType[type] + remainingByType[type], Is.EqualTo(requiredByType[type]));
        }
    }
}
