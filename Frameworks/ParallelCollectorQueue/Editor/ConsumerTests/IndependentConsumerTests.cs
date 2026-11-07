using System;
using NUnit.Framework;
using QueueModel = Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue.ParallelCollectorQueue;

namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue.ConsumerTests
{
    // Public API only: no friend access, Unity object, scene, game or other framework reference.
    public sealed class IndependentConsumerTests
    {
        [Test]
        public void TapStyleConsumerClearsTenBoxLevelAndReconcilesNotifications()
        {
            var definitions = new CollectorDefinition[10];
            for (int index = 0; index < definitions.Length; index++) definitions[index] = new CollectorDefinition(100 + index, index % 3, 10 + index);
            var queue = new QueueModel(definitions);
            long cargoOwnedByConsumer = queue.TotalRequiredUnits;
            long notifiedCredits = 0;
            int removedEvents = 0;
            int completions = 0;
            queue.EventPublished += result =>
            {
                if (result.Kind == CollectorQueueEventKind.CargoCollected) notifiedCredits += result.Units;
                if (result.Kind == CollectorQueueEventKind.CollectorRemoved) removedEvents++;
                if (result.Kind == CollectorQueueEventKind.Completed) completions++;
            };
            for (int tap = 0; tap < 10; tap++)
            {
                int lane = 0;
                while (!queue.TryGetFront(lane, out _)) lane++;
                queue.TryGetFront(lane, out CollectorSnapshot front);
                CollectorCollectionStatus status = queue.TryCollect(lane, front.Handle, front.Definition.TypeId,
                    front.RemainingUnits, out int accepted);
                Assert.That(status, Is.EqualTo(CollectorCollectionStatus.Accepted));
                cargoOwnedByConsumer -= accepted;
                // Reconcile the caller's owner BEFORE observers see queue facts.
                queue.DispatchEvents();
                Assert.That(cargoOwnedByConsumer + queue.CollectedUnits, Is.EqualTo(queue.TotalRequiredUnits));
            }
            Assert.That(queue.IsCompleted, Is.True);
            Assert.That(notifiedCredits, Is.EqualTo(queue.TotalRequiredUnits));
            Assert.That(cargoOwnedByConsumer, Is.Zero);
            Assert.That(removedEvents, Is.EqualTo(10));
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void SameDefinitionInTwoInstancesHasDifferentHandlesAndNoSharedState()
        {
            var definition = new[] { new CollectorDefinition(0, 0, 2) };
            var first = new QueueModel(definition);
            var second = new QueueModel(definition);
            first.TryGetFront(0, out CollectorSnapshot a);
            second.TryGetFront(0, out CollectorSnapshot b);
            Assert.That(a.Handle, Is.Not.EqualTo(b.Handle));
            Assert.That(a.Handle == b.Handle, Is.False);
            Assert.That(a.Handle != b.Handle, Is.True);
            Assert.That(a.Handle.Equals((object)b.Handle), Is.False);
            Assert.That(a.Handle.Equals((object)a.Handle), Is.True);
            Assert.That(a.Handle.GetHashCode(), Is.EqualTo(a.Handle.GetHashCode()));
            Assert.That(first.TryCollect(0, a.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.Accepted));
            Assert.That(second.TryCollect(0, a.Handle, 0, 1, out _), Is.EqualTo(CollectorCollectionStatus.StaleCollector));
            Assert.That(second.GetCollectorAt(0).RemainingUnits, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeHasNoUnityGameOrOtherScheherazadeDependencies()
        {
            foreach (System.Reflection.AssemblyName reference in typeof(QueueModel).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
                Assert.That(reference.Name.StartsWith("Com.", StringComparison.Ordinal), Is.False, reference.FullName);
                Assert.That(reference.Name, Is.Not.EqualTo("Assembly-CSharp"));
            }
            foreach (System.Reflection.AssemblyName reference in GetType().Assembly.GetReferencedAssemblies())
            {
                if (!reference.Name.StartsWith("Com.", StringComparison.Ordinal)) continue;
                Assert.That(reference.Name, Is.EqualTo("Com.Scheherazade.ParallelCollectorQueue"));
            }
        }
    }
}
