using System;
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;
#if PLA_CREATIVE_BUILD
using SingleThreadBackend = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBackend;
using JobBackend = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBackend;
#endif

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class NativeBackendCompatibilityTests
    {
#if !PLA_CREATIVE_BUILD
        [TestCase(0, 1, 0, false, false)]
        [TestCase(0, -1, 1, true, false)]
        [TestCase(1, 0, 3, true, false)]
        [TestCase(-1, 0, 3, true, false)]
        [TestCase(0, 1, 3, true, false)]
        [TestCase(0, 1, 3, true, true)]
        [TestCase(0, -1, 1, true, true)]
        [TestCase(1, 0, 3, true, true)]
        [TestCase(-1, 0, 3, true, true)]
        public void GeneratedPlaKernelsMatchNativeThroughMutationAndGravity(
            int gx,
            int gy,
            int mixingPasses,
            bool settle,
            bool regional
        )
        {
            const int width = 12, height = 16;
            using var native = new SingleThreadBackend(width, height);
            var current = new int[width * height];
            var next = new int[current.Length];
            var intents = new int[current.Length];
            var queue = new int[current.Length];
            var gravity = new byte[current.Length];
            var random = new System.Random(239);
            for (int index = 0; index < current.Length; index++)
            {
                int roll = random.Next(5);
                int cell = roll == 0 ? 2 : roll == 1 ? 0
                    : SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 32 + index % 16), index % 7 == 0);
                native.SetCell(index, cell);
                current[index] = native.GetCell(index);
                if (index % width >= 6) gravity[index] = (byte)(1 + index % 4);
            }
            StepParams parameters = Settings(new GridCoord(gx, gy));
            parameters.EnableVerticalSettle = settle;
            parameters.EnableLateralMixing = mixingPasses > 0;
            parameters.LateralMixPasses = Math.Max(1, mixingPasses);
            parameters.LateralMixChance = 32768;
            for (int tick = 0; tick < 40; tick++)
            {
                if (tick % 5 == 0)
                {
                    int index = random.Next(current.Length);
                    native.SetCell(index, tick % 10 == 0 ? 0 : SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 53), true));
                    current[index] = native.GetCell(index);
                }
                if (regional) parameters.GravityField = new SandGravityFieldSnapshot(gravity);
                SandStepSettings settings = SandAlgorithmCore.NormalizeStep(parameters, width, height, tick);
                native.Step(parameters);
                if (regional)
                {
                    new Pla.SandPlaRegionalStep
                    {
                        current = current, next = next, intents = intents,
                        claims = queue, gravity = gravity, settings = settings
                    }.Execute();
                    var swap = current; current = next; next = swap;
                }
                else
                {
                    if (settle)
                    {
                        for (int line = 0; line < SandAlgorithmCore.GetGravityLineCount(settings); line++)
                        {
                            Pla.SandPlaAlgorithmCore.CompactEligibleLine(current, line, settings);
                        }
                    }
                    new Pla.SandPlaStep
                    {
                        current = current, firstIntents = intents, secondIntents = next, reconciliationQueue = queue,
                        width = width, height = height, disableGravity = settings.disableGravity,
                        constantFallSpeed = settings.constantFallSpeed, gravityPerStep = settings.gravityPerStep,
                        maxFallSpeed = settings.maxFallSpeed, slideChance = settings.slideChance,
                        preventTemporaryEdgeVacancies = settings.preventTemporaryEdgeVacancies,
                        enableLateralMixing = settings.enableLateralMixing, lateralMixChance = settings.lateralMixChance,
                        lateralMixPasses = settings.lateralMixPasses, mixingSeed = settings.mixingSeed,
                        simulationSeed = settings.simulationSeed, gravityX = gx, gravityY = gy,
                        enableVerticalSettle = settle
                    }.Execute();
                    if ((mixingPasses + (settle ? 1 : 0)) % 2 == 0)
                    {
                        var swap = current;
                        current = next;
                        next = swap;
                    }
                    else
                    {
                        var swap = current;
                        current = intents;
                        intents = next;
                        next = swap;
                    }
                }
                CollectionAssert.AreEqual(native.CurrentGrid.ToArray(), current, "tick " + tick);
            }
        }
#endif

        [TestCase(false)]
        [TestCase(true)]
        public void RenderPublicationIgnoresPhysicsBitsButTracksVisualChangesAndResize(bool jobs)
        {
            using ISandBackend backend = Factory(jobs)(33, 33);
            var view = (ISandViewBackend)backend;
            Assert.That(view.RefreshViewBuffer(), Is.True, "A new empty view still needs its first upload.");
            Assert.That(view.RefreshViewBuffer(), Is.False);
            int index = backend.CellCount - 1;
            int grain = SandCell.Pack(ElementType.Sand, 0, 37);
            backend.SetCell(index, grain);
            Assert.That(view.RefreshViewBuffer(), Is.True);
            backend.SetCell(index, SandCell.WithUnsettled(grain | (3 << 8), true));
            Assert.That(view.RefreshViewBuffer(), Is.False, "Velocity and unsettled state are not rendered.");
            backend.SetCell(index, SandCell.Pack(ElementType.Sand, 0, 38));
            Assert.That(view.RefreshViewBuffer(), Is.True);
            Assert.That(backend.ViewBuffer[index].packedColorVariant, Is.EqualTo(38));
            backend.Step(Settings(new GridCoord(0, 1)));
            Assert.That(view.RefreshViewBuffer(), Is.False, "A supported stationary grain must not cause another upload.");
            backend.ClearGrid();
            Assert.That(view.RefreshViewBuffer(), Is.True);
            backend.Resize(1, 1);
            Assert.That(view.RefreshViewBuffer(), Is.True, "New dimensions require publication even when all pixels are zero.");
            Assert.That(view.RefreshViewBuffer(), Is.False);
        }

        [Test]
        public void GridAccessAndDisposalCompleteOutstandingJobs()
        {
            var backend = new JobBackend(1, 8);
            try
            {
                backend.SetCell(0, SandCell.Pack(ElementType.Sand, 0, 37));
                backend.ScheduleStep(Settings(new GridCoord(0, 1)));
                Assert.That(SandCell.GetElementType(backend.GetCell(7)), Is.EqualTo(ElementType.Sand));
                backend.ScheduleStep(Settings(new GridCoord(0, 1)));
                backend.Resize(2, 8);
                Assert.That(backend.CountSand(), Is.EqualTo(2));
                backend.ScheduleStep(Settings(new GridCoord(0, 1)));
                backend.ClearGrid();
                Assert.That(backend.CountSand(), Is.Zero);
                backend.SetCell(0, 1);
                backend.ScheduleStep(Settings(new GridCoord(0, 1)));
            }
            finally
            {
                backend.Dispose();
            }
            Assert.DoesNotThrow(backend.Dispose);
            Assert.Throws<ObjectDisposedException>(() => backend.SetCell(0, 1));
        }

        private static Func<int, int, ISandBackend> Factory(bool jobs) =>
            (width, height) => jobs ? new JobBackend(width, height) : new SingleThreadBackend(width, height);

        private static StepParams Settings(GridCoord gravity) => new StepParams
        {
            GravityDirection = gravity, EnableVerticalSettle = true,
            ConstantFallSpeed = 1, GravityPerStep = 1, MaxFallSpeed = 1,
            UpdateBatchSize = 32, SettleBatchSize = 1
        };
    }
}
