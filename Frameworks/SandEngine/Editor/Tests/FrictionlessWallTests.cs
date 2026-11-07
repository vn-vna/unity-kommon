#if !PLA_CREATIVE_BUILD
using System;
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class FrictionlessWallTests
    {
        [Test]
        public void WallMaterialFlagDoesNotChangeTypesColorsOrSandVelocity()
        {
            int wall = SandCell.PackWall(true, 37);
            Assert.That(SandCell.GetElementType(wall), Is.EqualTo(ElementType.Wall));
            Assert.That(SandCell.GetColorByte(wall), Is.EqualTo(37));
            Assert.That(SandCell.IsFrictionlessWall(wall), Is.True);
            Assert.That(SandCell.PackWall(), Is.EqualTo(2));
            Assert.That(SandCell.IsFrictionlessWall(SandCell.Pack(ElementType.Wall, 0, 37)), Is.False);
            int sand = SandCell.Pack(ElementType.Sand, 1, 37);
            Assert.That(SandCell.IsFrictionlessWall(sand), Is.False);
            Assert.That(SandCell.GetVelocity(sand), Is.EqualTo(1));
            int sliding = SandCell.WithFrictionlessSliding(sand, true);
            Assert.That(SandCell.IsFrictionlessSliding(sliding), Is.True);
            Assert.That(SandCell.GetColorByte(sliding), Is.EqualTo(37));
            Assert.That(SandCell.GetVelocity(sliding), Is.EqualTo(1));
            Assert.That(SandCell.WithFrictionlessSliding(sliding, false), Is.EqualTo(sand));
        }

        [TestCase(false, 0, 1)]
        [TestCase(true, 0, 1)]
        [TestCase(false, 0, -1)]
        [TestCase(true, 0, -1)]
        [TestCase(false, 1, 0)]
        [TestCase(true, 1, 0)]
        [TestCase(false, -1, 0)]
        [TestCase(true, -1, 0)]
        public void FrictionlessSupportSlidesDespiteFullFrictionButNormalWallsDoNot(bool jobs, int gx, int gy)
        {
            const int width = 9, height = 9, source = 4 * width + 4;
            StepParams p = Parameters(new GridCoord(gx, gy));
            p.ConstantFallSpeed = 1;
            foreach (bool frictionless in new[] { false, true })
            {
                using ISandBackend backend = Create(jobs, width, height);
                int support = source + gy * width + gx;
                int wall = SandCell.PackWall(frictionless);
                backend.SetCell(support, wall);
                backend.SetCell(source, Grain());
                backend.Step(p);
                Assert.That(backend.GetCell(support), Is.EqualTo(wall));
                Assert.That(backend.CountSand(), Is.EqualTo(1));
                Assert.That(backend.GetCell(source) == 0, Is.EqualTo(frictionless));
                if (!frictionless) continue;
                int destination = FindSand(backend);
                int distance = (destination % width - 4) * gx + (destination / width - 4) * gy;
                Assert.That(distance, Is.EqualTo(1));
                Assert.That(SandCell.GetColorByte(backend.GetCell(destination)), Is.EqualTo(37));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RasterLedgeTraversalIsSpeedBoundedAndConservesSand(bool jobs, bool regional)
        {
            using ISandBackend backend = Create(jobs, 12, 10);
            SeedLedge(backend);
            StepParams p = Parameters(new GridCoord(0, 1));
            if (regional) p.GravityField = new SandGravityFieldSnapshot(new byte[120]);
            backend.Step(p);
            Assert.That(backend.GetCell(3 * 12 + 2), Is.Zero);
            Assert.That(SandCell.GetElementType(backend.GetCell(3 * 12 + 4)), Is.EqualTo(ElementType.Sand));
            Assert.That(SandCell.GetVelocity(backend.GetCell(3 * 12 + 4)), Is.EqualTo(2));
            for (int tick = 0; tick < 20; tick++) backend.Step(p);
            Assert.That(backend.CountSand(), Is.EqualTo(1));
            Assert.That(FindSand(backend) / 12, Is.GreaterThan(4));
            for (int x = 1; x <= 7; x++) Assert.That(SandCell.IsFrictionlessWall(backend.GetCell(4 * 12 + x)), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FrictionlessLedgeCanPassAboveSandToReachClearDownhillSpace(bool jobs, bool regional)
        {
            using ISandBackend backend = Create(jobs, 12, 10);
            SeedSandBridge(backend);
            StepParams p = Parameters(new GridCoord(0, 1));
            if (regional) p.GravityField = new SandGravityFieldSnapshot(new byte[120]);
            backend.Step(p);
            Assert.That(backend.GetCell(3 * 12 + 3), Is.Zero);
            Assert.That(SandCell.GetElementType(backend.GetCell(3 * 12 + 5)), Is.EqualTo(ElementType.Sand));
            Assert.That(SandCell.GetVelocity(backend.GetCell(3 * 12 + 5)), Is.EqualTo(2));
            Assert.That(SandCell.IsFrictionlessSliding(backend.GetCell(3 * 12 + 5)), Is.True);
            backend.Step(p);
            Assert.That(SandCell.IsFrictionlessSliding(backend.GetCell(3 * 12 + 6)), Is.True);
            backend.Step(p);
            Assert.That(SandCell.GetElementType(backend.GetCell(4 * 12 + 7)), Is.EqualTo(ElementType.Sand));
            Assert.That(SandCell.IsFrictionlessSliding(backend.GetCell(4 * 12 + 7)), Is.False);
            Assert.That(backend.CountSand(), Is.EqualTo(4));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FrictionlessLedgeCannotPassThroughAnOccupiedSidewaysPath(bool jobs)
        {
            using ISandBackend backend = Create(jobs, 12, 10);
            SeedSandBridge(backend);
            backend.SetCell(3 * 12 + 4, SandCell.PackWall());
            backend.Step(Parameters(new GridCoord(0, 1)));
            Assert.That(SandCell.GetElementType(backend.GetCell(3 * 12 + 3)), Is.EqualTo(ElementType.Sand));
            Assert.That(backend.GetCell(3 * 12 + 4), Is.EqualTo(SandCell.PackWall()));
            Assert.That(backend.CountSand(), Is.EqualTo(4));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void BlockedOutletDoesNotPropASparseChainUpTheFrictionlessSlope(bool jobs, bool mirror)
        {
            using ISandBackend backend = Create(jobs, 28, 16);
            SeedClosedSlopeChain(backend, mirror);
            StepParams p = Parameters(new GridCoord(0, 1));
            p.SlideChance = (int)(0.92f * 65536f);
            for (int tick = 0; tick < 300; tick++) backend.Step(p);
            Assert.That(backend.CountSand(), Is.EqualTo(14));
            for (int index = 0; index < backend.CellCount; index++)
            {
                if (SandCell.GetElementType(backend.GetCell(index)) != ElementType.Sand) continue;
                Assert.That(index / backend.Width, Is.GreaterThanOrEqualTo(9), "A sparse support chain must collapse into the bottom pocket, not pin a pile high on the border.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosedFlatFrictionlessFloorDoesNotCauseOscillationOrDeletion(bool jobs)
        {
            using ISandBackend backend = Create(jobs, 9, 7);
            for (int x = 0; x < 9; x++) backend.SetCell(4 * 9 + x, SandCell.PackWall(true));
            backend.SetCell(3 * 9 + 4, Grain());
            for (int tick = 0; tick < 10; tick++) backend.Step(Parameters(new GridCoord(0, 1)));
            Assert.That(SandCell.GetElementType(backend.GetCell(3 * 9 + 4)), Is.EqualTo(ElementType.Sand));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void OptionalPolicyDisablesInstantCompactionWithoutChangingLegacySettings(bool jobs, bool disable)
        {
            using ISandBackend backend = Create(jobs, 1, 12);
            ISandEngineSettings settings = disable ? new NoSettleSettings() : new LegacySettings();
            var engine = new SandEngine(backend, settings);
            backend.SetCell(0, SandCell.Pack(ElementType.Sand, 0, 37));
            engine.Step();
            Assert.That(FindSand(backend), Is.EqualTo(disable ? 2 : 11));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledInstantSettlingAlsoDisablesCollectionCompaction(bool disable)
        {
            using var backend = new SingleThreadBackend(1, 8);
            ISandEngineSettings settings = disable ? new NoSettleSettings() : new LegacySettings();
            var engine = new SandEngine(backend, settings);
            backend.SetCell(0, SandCell.Pack(ElementType.Sand, 0, 37));
            backend.SetCell(2, SandCell.Pack(ElementType.Sand, 0, 37));
            Assert.That(engine.CollectSand(0, 2, 0, 2, 1), Is.EqualTo(1));
            Assert.That(FindSand(backend), Is.EqualTo(disable ? 0 : 2));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GravityOnlyFallAcceleratesWithoutTeleportingToTheFloor(bool jobs)
        {
            using ISandBackend backend = Create(jobs, 1, 32);
            backend.SetCell(0, Grain());
            StepParams p = Parameters(new GridCoord(0, 1));
            p.DisableGravity = false;
            p.GravityPerStep = 2;
            p.MaxFallSpeed = 6;
            int[] expected = { 2, 6, 12, 18 };
            foreach (int position in expected)
            {
                backend.Step(p);
                Assert.That(FindSand(backend), Is.EqualTo(position));
                Assert.That(backend.CountSand(), Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedPlaLedgeMovesMatchNative(bool sandBridge)
        {
            using var native = new SingleThreadBackend(12, 10);
            if (sandBridge) SeedSandBridge(native);
            else SeedLedge(native);
            int[] current = native.CurrentGrid.ToArray(), next = new int[120], intents = new int[120], queue = new int[120];
            StepParams p = Parameters(new GridCoord(0, 1));
            for (int tick = 0; tick < 20; tick++)
            {
                native.Step(p);
                new Pla.SandPlaStep
                {
                    current = current, firstIntents = intents, secondIntents = next, reconciliationQueue = queue,
                    width = 12, height = 10, disableGravity = true, constantFallSpeed = 2,
                    gravityPerStep = 1, maxFallSpeed = 2, slideChance = 0,
                    preventTemporaryEdgeVacancies = true, enableLateralMixing = false,
                    lateralMixPasses = 1, simulationSeed = tick, mixingSeed = 239,
                    gravityX = 0, gravityY = 1, enableVerticalSettle = false
                }.Execute();
                int[] swap = current; current = next; next = swap;
                CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), current, "tick " + tick);
                if (sandBridge && tick == 0) Assert.That(current[3 * 12 + 3], Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GpuLedgeMovesMatchNativeWhenComputeAndReadbackAreSupported(bool sandBridge)
        {
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Plugins/Scheherazade/Frameworks/SandEngine/Runtime/Shaders/SandSimulation.compute");
            Assert.That(shader, Is.Not.Null);
            if (!GpuBackend.IsSupported(shader)) Assert.Ignore("This Editor/device does not support the GPU backend.");
            using var gpu = new GpuBackend(12, 10, shader);
            using var native = new SingleThreadBackend(12, 10);
            if (sandBridge) { SeedSandBridge(gpu); SeedSandBridge(native); }
            else { SeedLedge(gpu); SeedLedge(native); }
            StepParams p = Parameters(new GridCoord(0, 1));
            for (int tick = 0; tick < 10; tick++)
            {
                native.Step(p);
                gpu.Step(p);
                CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), gpu.CurrentGrid.ToArray(), "tick " + tick);
                if (sandBridge && tick == 0) Assert.That(gpu.GetCell(3 * 12 + 3), Is.Zero);
            }
            Assert.That(gpu.CountSand(), Is.EqualTo(sandBridge ? 4 : 1));
        }

        private static int Grain() => SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 37), true);
        private static ISandBackend Create(bool jobs, int width, int height) => jobs ? new JobBackend(width, height) : new SingleThreadBackend(width, height);
        private static StepParams Parameters(GridCoord direction) => new StepParams
        {
            GravityDirection = direction, DisableGravity = true, ConstantFallSpeed = 2,
            GravityPerStep = 1, MaxFallSpeed = 2, SlideChance = 0,
            PreventTemporaryEdgeVacancies = true, EnableVerticalSettle = false,
            UpdateBatchSize = 16, SettleBatchSize = 1, MixingSeed = 239
        };

        private static void SeedLedge(ISandBackend backend)
        {
            for (int y = 0; y < backend.Height; y++)
                for (int x = 0; x < backend.Width; x++)
                    if (x == 0 || x == backend.Width - 1 || y == 0 || y == backend.Height - 1)
                        backend.SetCell(y * backend.Width + x, SandCell.PackWall());
            for (int x = 1; x <= 7; x++) backend.SetCell(4 * 12 + x, SandCell.PackWall(true));
            backend.SetCell(3 * 12 + 2, Grain());
        }

        private static void SeedSandBridge(ISandBackend backend)
        {
            for (int y = 0; y < backend.Height; y++)
            {
                backend.SetCell(y * 12, SandCell.PackWall());
                backend.SetCell(y * 12 + 11, SandCell.PackWall());
            }
            for (int x = 1; x < 11; x++) backend.SetCell(5 * 12 + x, SandCell.PackWall(true));
            for (int x = 1; x <= 3; x++) backend.SetCell(4 * 12 + x, SandCell.PackWall(true));
            for (int x = 4; x <= 6; x++) backend.SetCell(4 * 12 + x, Grain());
            backend.SetCell(3 * 12 + 3, Grain());
        }

        private static void SeedClosedSlopeChain(ISandBackend backend, bool mirror)
        {
            // Crop of the screenshot's right-hand slope and capped outlet.
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 28; x++)
                {
                    int originalX = x + 78, originalY = y + 208;
                    int surface = 176 + Mathf.RoundToInt((159 - originalX) * 46f / 76f);
                    if (x == 0 || x == 27 || y == 15 || originalX >= 83 && originalY >= surface)
                        backend.SetCell(y * 28 + (mirror ? 27 - x : x), SandCell.PackWall(true));
                }
            }
            int[] originalXByRow = { 102, 101, 100, 98, 97, 95, 93, 92, 90, 88, 87, 85, 83, 82 };
            for (int row = 0; row < originalXByRow.Length; row++)
            {
                int x = originalXByRow[row] - 78;
                backend.SetCell((row + 1) * 28 + (mirror ? 27 - x : x), Grain());
            }
        }

        private static int FindSand(ISandBackend backend)
        {
            for (int index = 0; index < backend.CellCount; index++)
                if (SandCell.GetElementType(backend.GetCell(index)) == ElementType.Sand) return index;
            Assert.Fail("A grain was lost.");
            return -1;
        }

        private class LegacySettings : ISandEngineSettings
        {
            public bool EnableGravity => false;
            public int ConstantFallSpeed => 2;
            public int GravityPerStep => 1;
            public int MaxFallSpeed => 2;
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
            public int UpdateBatchSize => 16;
            public bool EnableBatchCollapse => false;
            public bool BatchCollapseAutoDetect => false;
            public int BatchCollapseScanInterval => 1;
            public int BatchCollapseMinDepth => 2;
            public int BatchCollapseRetainedStep => 1;
            public int BatchCollapseMaxGrainsPerStep => 0;
            public int BatchCollapseMaxGrainsPerPair => 0;
            public int BatchCollapsePassesPerStep => 1;
            public int BatchCollapseMaxSteps => 0;
            public int BatchCollapseRadius => 1;
            public int ColorIndex => 0;
            public void GetRandomVariantRange(int color, out int minimum, out int maximum) { minimum = 0; maximum = 15; }
        }

        private sealed class NoSettleSettings : LegacySettings, ISandSettlingSettings
        {
            public bool EnableImmediateSettling => false;
        }
    }
}
#endif
