#if !PLA_CREATIVE_BUILD
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class ContactSlipTests
    {
        [Test]
        public void FrozenMaterialIsDistinctWithoutChangingSandVelocityOrWallPalette()
        {
            int fixedSand = SandCell.PackFixedSandWall(37);
            Assert.That(SandCell.IsFixedSandWall(fixedSand), Is.True);
            Assert.That(SandCell.IsFrictionlessWall(fixedSand), Is.False);
            Assert.That(SandCell.GetElementType(fixedSand), Is.EqualTo(ElementType.Wall));
            Assert.That(SandCell.GetColorByte(fixedSand), Is.EqualTo(37));
            Assert.That(SandCell.IsFixedSandWall(SandCell.PackWall(true, 37)), Is.False);
            int sand = SandCell.Pack(ElementType.Sand, 2, 37);
            Assert.That(SandCell.IsFixedSandWall(sand), Is.False);
            Assert.That(SandCell.GetVelocity(sand), Is.EqualTo(2));
        }

        [Test]
        public void IndependentContactLimitsWorkAcrossJobsScalarRegionalAndAllGravityDirections(
            [Values(false, true)] bool jobs, [Values(0, 1, 2, 3)] int support,
            [Values(false, true)] bool allowed, [Values(false, true)] bool regional)
        {
            foreach (var gravity in new[] { new GridCoord(0, 1), new GridCoord(0, -1), new GridCoord(1, 0), new GridCoord(-1, 0) })
            {
                using ISandBackend backend = jobs ? new JobBackend(9, 9) : new SingleThreadBackend(9, 9);
                Seed(backend, support, gravity);
                StepParams parameters = Parameters(support, allowed, gravity);
                if (regional) parameters.GravityField = new SandGravityFieldSnapshot(new byte[81]);
                backend.Step(parameters);
                Assert.That(backend.GetCell(40) == 0, Is.EqualTo(allowed), "gravity " + gravity.x + "," + gravity.y);
                Assert.That(backend.CountSand(), Is.EqualTo(support == 0 ? 2 : 1));
                bool found = false;
                for (int index = 0; index < 81; index++)
                    if (SandCell.GetElementType(backend.GetCell(index)) == ElementType.Sand
                        && SandCell.GetColorByte(backend.GetCell(index)) == 37) found = true;
                Assert.That(found, Is.True);
            }
        }

        [Test]
        public void GpuContactPolicyMatchesNative([Values(0, 1, 2, 3)] int support, [Values(false, true)] bool allowed)
        {
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Plugins/Scheherazade/Frameworks/SandEngine/Runtime/Shaders/SandSimulation.compute");
            if (!GpuBackend.IsSupported(shader)) Assert.Ignore("GPU compute/readback is unavailable on this Editor/device.");
            using var gpu = new GpuBackend(9, 9, shader);
            using var native = new SingleThreadBackend(9, 9);
            var gravity = new GridCoord(0, 1);
            Seed(gpu, support, gravity);
            Seed(native, support, gravity);
            var parameters = Parameters(support, allowed, gravity);
            gpu.Step(parameters);
            native.Step(parameters);
            CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), gpu.CurrentGrid.ToArray());
        }

        [Test]
        public void GeneratedPlaContactPolicyMatchesNative([Values(0, 1, 2, 3)] int support, [Values(false, true)] bool allowed)
        {
            using var native = new SingleThreadBackend(9, 9);
            Seed(native, support, new GridCoord(0, 1));
            int[] current = native.CurrentGrid.ToArray();
            var next = new int[81];
            var parameters = Parameters(support, allowed, new GridCoord(0, 1));
            native.Step(parameters);
            new Pla.SandPlaStep
            {
                current = current, firstIntents = new int[81], secondIntents = next, reconciliationQueue = new int[81],
                width = 9, height = 9, disableGravity = true, constantFallSpeed = 1, gravityPerStep = 1,
                maxFallSpeed = 1, slideChance = 0, contactSlip = parameters.ContactSlip,
                gravityX = 0, gravityY = 1, mixingSeed = 239
            }.Execute();
            CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), next);
        }

        private static StepParams Parameters(int support, bool allowed, GridCoord gravity)
        {
            int selected = allowed ? 65536 : 0, other = allowed ? 0 : 65536;
            return new StepParams
            {
                GravityDirection = gravity, DisableGravity = true, ConstantFallSpeed = 1, GravityPerStep = 1,
                MaxFallSpeed = 1, SlideChance = 0, UpdateBatchSize = 16, SettleBatchSize = 1, MixingSeed = 239,
                ContactSlip = new SandContactSlip(true, support == 0 ? selected : other,
                    support == 1 ? selected : other, support == 2 ? selected : other, support == 3 ? selected : other)
            };
        }

        private static void Seed(ISandBackend backend, int support, GridCoord gravity)
        {
            int px = gravity.y, py = gravity.x;
            for (int side = -4; side <= 4; side++)
            {
                int x = 4 + gravity.x * 2 + side * px;
                int y = 4 + gravity.y * 2 + side * py;
                backend.SetCell(y * 9 + x, SandCell.PackWall());
            }
            int supportCell = support == 0 ? SandCell.Pack(ElementType.Sand, 0, 9)
                : support == 1 ? SandCell.PackFixedSandWall(9) : SandCell.PackWall(support == 3, 9);
            backend.SetCell((4 + gravity.y) * 9 + 4 + gravity.x, supportCell);
            backend.SetCell(40, SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 37), true));
        }
    }
}
#endif
