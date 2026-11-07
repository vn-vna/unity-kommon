#if !PLA_CREATIVE_BUILD
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class SupportedSlideTests
    {
        private const int Width = 13;
        private const int Center = 6;
        private const int Source = Center * Width + Center;

        [Test]
        public void SupportedFlowTraversesShallowLedgeAndRespectsEachContactGate(
            [Values(false, true)] bool jobs, [Values(false, true)] bool regional,
            [Values(0, 1, 2, 3)] int support, [Values(false, true)] bool allowed)
        {
            foreach (var gravity in Directions())
            {
                using ISandBackend backend = jobs ? new JobBackend(Width, Width) : new SingleThreadBackend(Width, Width);
                Seed(backend, support, gravity);
                var parameters = Parameters(support, allowed, gravity);
                if (regional) parameters.GravityField = new SandGravityFieldSnapshot(new byte[Width * Width]);
                backend.Step(parameters);
                Assert.That(backend.GetCell(Source) == 0, Is.EqualTo(allowed));
                if (allowed) Assert.That(SandCell.GetColorByte(backend.GetCell(At(gravity, 0, 3))), Is.EqualTo(37));
                Assert.That(backend.CountSand(), Is.EqualTo(support == 0 ? 9 : 1));
            }
        }

        [TestCase("disabled")]
        [TestCase("shortSearch")]
        [TestCase("obstacle")]
        [TestCase("noExit")]
        [TestCase("zeroSlipCrossing")]
        public void SupportTraversalNeverTunnelsOrMovesWithoutAReachablePermittedDrop(string scenario)
        {
            var gravity = new GridCoord(0, 1);
            using var backend = new SingleThreadBackend(Width, Width);
            Seed(backend, 2, gravity);
            var parameters = Parameters(2, true, gravity);
            if (scenario == "disabled") parameters.ContactSlip = new SandContactSlip(true, 65536, 65536, 65536, 65536);
            if (scenario == "shortSearch") parameters.ContactSlip = new SandContactSlip(true, 65536, 65536, 65536, 65536, 3, 2);
            if (scenario == "obstacle") backend.SetCell(At(gravity, 0, 2), SandCell.PackWall());
            if (scenario == "noExit")
                for (int side = 4; side <= 6; side++) backend.SetCell(At(gravity, 1, side), SandCell.PackWall());
            if (scenario == "zeroSlipCrossing")
            {
                parameters.ContactSlip = new SandContactSlip(true, 65536, 0, 65536, 65536, 3, 8);
                backend.SetCell(At(gravity, 1, 2), SandCell.PackFixedSandWall(9));
            }
            int before = backend.GetCell(Source);
            backend.Step(parameters);
            Assert.That(SandCell.GetColorByte(backend.GetCell(Source)), Is.EqualTo(SandCell.GetColorByte(before)));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [Test]
        public void MinimumSupportedSpeedDoesNotAccelerateFreeFall()
        {
            using var backend = new SingleThreadBackend(Width, Width);
            backend.SetCell(Source, SandCell.Pack(ElementType.Sand, 0, 37));
            backend.Step(Parameters(2, true, new GridCoord(0, 1)));
            Assert.That(SandCell.GetColorByte(backend.GetCell(Source + Width)), Is.EqualTo(37));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [Test]
        public void SupportedSlideUsesItsBudgetAcrossDiagonalThenVerticalSegments()
        {
            using var backend = new SingleThreadBackend(Width, Width);
            backend.SetCell(Source, SandCell.Pack(ElementType.Sand, 0, 37));
            backend.SetCell(Source + Width, SandCell.PackWall());
            backend.SetCell(Source + Width - 1, SandCell.PackWall());
            backend.Step(Parameters(2, true, new GridCoord(0, 1)));
            Assert.That(backend.GetCell(Source), Is.Zero);
            Assert.That(SandCell.GetColorByte(backend.GetCell(Source + Width * 3 + 1)), Is.EqualTo(37));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SupportedSlideContinuesAcrossHorizontalRasterSteps(bool jobs)
        {
            using ISandBackend backend = jobs ? new JobBackend(Width, Width) : new SingleThreadBackend(Width, Width);
            var gravity = new GridCoord(0, 1);
            Seed(backend, 4, gravity);
            backend.Step(Parameters(2, true, gravity));
            Assert.That(backend.GetCell(Source), Is.Zero);
            Assert.That(SandCell.GetColorByte(backend.GetCell(Source + Width * 2 + 3)), Is.EqualTo(37));
            Assert.That(backend.CountSand(), Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void GpuAndGeneratedPlaMatchNativeSupportedFlowAcrossGravityDirections(int support)
        {
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Plugins/Scheherazade/Frameworks/SandEngine/Runtime/Shaders/SandSimulation.compute");
            if (!GpuBackend.IsSupported(shader)) Assert.Ignore("GPU compute/readback is unavailable.");
            foreach (var gravity in Directions())
            {
                using var native = new SingleThreadBackend(Width, Width);
                using var gpu = new GpuBackend(Width, Width, shader);
                Seed(native, support, gravity);
                Seed(gpu, support, gravity);
                var parameters = Parameters(support == 4 ? 2 : support, true, gravity);
                int[] plaCurrent = native.CurrentGrid.ToArray();
                for (int tick = 0; tick < 6; tick++)
                {
                    var next = new int[Width * Width];
                    new Pla.SandPlaStep
                    {
                        current = plaCurrent, firstIntents = new int[Width * Width], secondIntents = next,
                        reconciliationQueue = new int[Width * Width], width = Width, height = Width,
                        disableGravity = true, constantFallSpeed = 1, gravityPerStep = 1, maxFallSpeed = 1,
                        slideChance = 0, contactSlip = parameters.ContactSlip, gravityX = gravity.x,
                        gravityY = gravity.y, mixingSeed = 239, simulationSeed = tick
                    }.Execute();
                    native.Step(parameters);
                    gpu.Step(parameters);
                    CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), next, "PLA tick " + tick);
                    CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), gpu.CurrentGrid.ToArray(), "GPU tick " + tick);
                    plaCurrent = next;
                }
            }
        }

        private static StepParams Parameters(int support, bool allowed, GridCoord gravity)
        {
            int selected = allowed ? 65536 : 0, other = allowed ? 0 : 65536;
            return new StepParams
            {
                DisableGravity = true, ConstantFallSpeed = 1, GravityPerStep = 1, MaxFallSpeed = 1,
                GravityDirection = gravity, UpdateBatchSize = 16, SettleBatchSize = 1, MixingSeed = 239,
                ContactSlip = new SandContactSlip(true, support == 0 ? selected : other, support == 1 ? selected : other,
                    support == 2 ? selected : other, support == 3 ? selected : other, 3, 8)
            };
        }

        private static void Seed(ISandBackend backend, int support, GridCoord gravity)
        {
            if (support == 4)
            {
                backend.SetCell(At(gravity, 1, 0), SandCell.PackWall());
                backend.SetCell(At(gravity, 1, -1), SandCell.PackWall());
                backend.SetCell(At(gravity, 2, 1), SandCell.PackWall());
                backend.SetCell(At(gravity, 2, 2), SandCell.PackWall());
                backend.SetCell(Source, SandCell.Pack(ElementType.Sand, 0, 37));
                return;
            }
            for (int side = -6; side <= 6; side++) backend.SetCell(At(gravity, 2, side), SandCell.PackWall());
            int supportCell = support == 0 ? SandCell.Pack(ElementType.Sand, 0, 9)
                : support == 1 ? SandCell.PackFixedSandWall(9) : SandCell.PackWall(support == 3, 9);
            for (int side = -4; side <= 3; side++) backend.SetCell(At(gravity, 1, side), supportCell);
            backend.SetCell(At(gravity, 0, -1), SandCell.PackWall());
            backend.SetCell(Source, SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 37), true));
        }

        private static int At(GridCoord gravity, int down, int side) =>
            (Center + gravity.y * down + gravity.x * side) * Width + Center + gravity.x * down + gravity.y * side;

        private static GridCoord[] Directions() => new[]
        {
            new GridCoord(0, 1), new GridCoord(0, -1), new GridCoord(1, 0), new GridCoord(-1, 0)
        };
    }
}
#endif
