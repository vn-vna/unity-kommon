using System;
using System.Linq;
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;
using UnityEngine;
#if PLA_CREATIVE_BUILD && UNITY_LUNA
using RectInt = Com.Scheherazade.Common.Compatibility.CreativeRectInt;
#endif
#if PLA_CREATIVE_BUILD
using Backend = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBackend;
#else
using Backend = Com.Scheherazade.Common.Frameworks.SandEngine.SingleThreadBackend;
#endif

namespace Com.Scheherazade.Common.Frameworks.SandEngine.ConsumerTests
{
    // This assembly deliberately has no game reference or InternalsVisibleTo.
    // Its plain settings object demonstrates the actual second-consumer seam.
    public sealed class IndependentConsumerTests
    {
        [Test]
        public void PublicEngineSupportsIndependentSettingsGravityAndPublication()
        {
            using var backend = new Backend(2, 8);
            var settings = new ConsumerSettings();
            var engine = new SandEngine(backend, settings);
            Assert.That(engine.Settings, Is.SameAs(settings));
            Assert.That(engine.TryEmitSand(0, 37), Is.True);
            using var region = engine.RegisterGravityRegion("second consumer", new RectInt(0, 0, 2, 8), new GridCoord(0, 1));
            engine.ScheduleStep();
            engine.CompleteStep();
            Assert.That(engine.CountSand(), Is.EqualTo(1));
            Assert.That(engine.GetGravityDirection(0), Is.EqualTo(new GridCoord(0, 1)));
            Assert.That(((ISandViewBackend)backend).RefreshViewBuffer(), Is.True);
            Assert.That(((ISandViewBackend)backend).RefreshViewBuffer(), Is.False);
            settings.EnableGravity = false;
            engine.Step();
            Assert.That(engine.CountSand(), Is.EqualTo(1));
        }

        [Test]
        public void ReservationBatchSamplesOriginalGrainsBeforeSingleRepair()
        {
            using var backend = new Backend(1, 6);
            var engine = new SandEngine(backend, new ConsumerSettings());
            int first = SandCell.Pack(ElementType.Sand, 0, 32);
            int second = SandCell.Pack(ElementType.Sand, 0, 33);
            int third = SandCell.Pack(ElementType.Sand, 0, 34);
            engine.WriteCells(new[] { first, second, third });
            var samples = new SandRemovalSampleBuffer();
            samples.EnsureCapacity(2);
            samples.Reset(2, 17);
            int removed = engine.CommitReservedRemovals(new[]
            {
                new SandRemovalSample(2, third),
                new SandRemovalSample(1, second)
            }, samples);
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(samples.Count, Is.EqualTo(2));
            Assert.That(samples[0].CellIndex, Is.EqualTo(2));
            Assert.That(samples[0].PackedCell, Is.EqualTo(third));
            Assert.That(samples[1].CellIndex, Is.EqualTo(1));
            Assert.That(samples[1].PackedCell, Is.EqualTo(second));
            Assert.That(engine.CountSand(), Is.EqualTo(1));
            Assert.That(SandCell.GetColorByte(backend.GetCell(2)), Is.EqualTo(32));
        }

        [TestCase(17u, new int[] { 3, 5, 6 }, new int[] { 13, 10, 21 }, new int[] { 13, 10, 45 })]
        [TestCase(0u, new int[] { 4, 7, 3 }, new int[] { 31, 7, 23 }, new int[] { 31, 7, 23 })]
        public void ReservoirOverflowPreservesSeededSequenceAndReset(
            uint seed,
            int[] afterEight,
            int[] afterThirtyTwo,
            int[] afterSixtyFour
        )
        {
            var samples = new SandRemovalSampleBuffer();
            samples.EnsureCapacity(3);
            samples.Reset(3, seed);
            for (int index = 0; index < 3; index++)
            {
                samples.Consider(index, SandCell.Pack(ElementType.Sand, index, 37));
            }
            AssertReservoir(samples, new[] { 0, 1, 2 });

            // Golden checkpoints cover replacement and rejected draws. The first
            // three admissions consume no RNG; zero selects the fallback seed.
            for (int index = 3; index < 64; index++)
            {
                samples.Consider(index, SandCell.Pack(ElementType.Sand, index, 37));
                if (index == 7)
                {
                    AssertReservoir(samples, afterEight);
                }
                if (index == 31)
                {
                    AssertReservoir(samples, afterThirtyTwo);
                }
            }
            AssertReservoir(samples, afterSixtyFour);

            samples.Reset(3, seed);
            Assert.That(samples.Count, Is.Zero);
            for (int index = 0; index < 64; index++)
            {
                samples.Consider(index, SandCell.Pack(ElementType.Sand, index, 37));
            }
            AssertReservoir(samples, afterSixtyFour);
        }

        private static void AssertReservoir(SandRemovalSampleBuffer samples, int[] expectedIndices)
        {
            Assert.That(samples.Capacity, Is.EqualTo(3));
            Assert.That(samples.Count, Is.EqualTo(expectedIndices.Length));
            for (int slot = 0; slot < expectedIndices.Length; slot++)
            {
                int index = expectedIndices[slot];
                Assert.That(samples[slot].CellIndex, Is.EqualTo(index), "reservoir slot " + slot);
                Assert.That(
                    samples[slot].PackedCell,
                    Is.EqualTo(SandCell.Pack(ElementType.Sand, index, 37)),
                    "packed cell at slot " + slot
                );
            }
        }

        [TestCase(-1)]
        [TestCase(6)]
        [TestCase(0)]
        [TestCase(1)]
        public void InvalidReservationBatchIsRejectedBeforeMutationAndSampling(int invalidIndex)
        {
            using var backend = new Backend(1, 6);
            var engine = new SandEngine(backend, new ConsumerSettings());
            int grain = SandCell.Pack(ElementType.Sand, 0, 37);
            backend.SetCell(0, grain);
            var samples = new SandRemovalSampleBuffer();
            samples.EnsureCapacity(2);
            samples.Reset(2, 17);
            var batch = new[]
            {
                new SandRemovalSample(0, grain),
                new SandRemovalSample(invalidIndex, grain)
            };
            Assert.Throws<ArgumentException>(() => engine.CommitReservedRemovals(batch, samples));
            Assert.That(backend.GetCell(0), Is.EqualTo(grain));
            Assert.That(engine.CountSand(), Is.EqualTo(1));
            Assert.That(samples.Count, Is.Zero);
        }

        [Test]
        public void RuntimeAndConsumerAssembliesHaveNoGameDependency()
        {
            var assemblies = new[] { typeof(SandEngine).Assembly, GetType().Assembly };
            foreach (var assembly in assemblies)
            {
                Assert.That(assembly.GetReferencedAssemblies().Any(reference =>
                    reference.Name.StartsWith("Com.PMA.", StringComparison.Ordinal)), Is.False, assembly.FullName);
            }
        }

        private sealed class ConsumerSettings : ISandEngineSettings
        {
            public bool EnableGravity { get; set; } = true;
            public int ConstantFallSpeed => 1;
            public int GravityPerStep => 1;
            public int MaxFallSpeed => 1;
            public float SlideFriction => 1f;
            public GridCoord GravityDirection => new GridCoord(0, 1);
            public int SettleBatchSize => 1;
            public bool PreventTemporaryEdgeVacancies => true;
            public bool EnableLateralMixing => false;
            public float LateralMixChance => 0f;
            public int LateralMixPasses => 1;
            public bool OverrideCollectionFallMixing => false;
            public float CollectionFallMixChance => 0f;
            public int CollectionFallMixRadius => 1;
            public int CollectionFallMixSamples => 1;
            public int MixingSeed => 239;
            public int UpdateBatchSize => 32;
            public bool EnableBatchCollapse => false;
            public bool BatchCollapseAutoDetect => false;
            public int BatchCollapseScanInterval => 1;
            public int BatchCollapseMinDepth => 1;
            public int BatchCollapseRetainedStep => 1;
            public int BatchCollapseMaxGrainsPerStep => 1;
            public int BatchCollapseMaxGrainsPerPair => 1;
            public int BatchCollapsePassesPerStep => 1;
            public int BatchCollapseMaxSteps => 1;
            public int BatchCollapseRadius => 1;
            public int ColorIndex => 2;

            public void GetRandomVariantRange(int logicalColor, out int minimum, out int maximum)
            {
                minimum = 3;
                maximum = 7;
            }
        }
    }
}
