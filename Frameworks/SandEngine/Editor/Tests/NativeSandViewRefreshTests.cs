#if !PLA_CREATIVE_BUILD
using Com.Scheherazade.Common.Frameworks.GridSystem;
using NUnit.Framework;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class NativeSandViewRefreshTests
    {
        [TestCase(1, 1)]
        [TestCase(1023, 1)]
        [TestCase(1024, 1)]
        [TestCase(1025, 1)]
        [TestCase(33, 33)]
        [TestCase(64, 64)]
        [TestCase(126, 108)]
        [TestCase(256, 256)]
        public void BothExecutionsPreserveNetVisualChangesClearAndResize(int width, int height)
        {
            using (var direct = new JobBackend(width, height))
            using (var scheduled = new JobBackend(width, height))
            {
                AssertRefresh(direct, scheduled, true);
                AssertRefresh(direct, scheduled, false);
                int index = direct.CellCount - 1;
                int grain = SandCell.Pack(ElementType.Sand, 0, 37);
                SetBoth(direct, scheduled, index, grain);
                AssertRefresh(direct, scheduled, true);
                SetBoth(direct, scheduled, index, grain);
                AssertRefresh(direct, scheduled, false);
                SetBoth(direct, scheduled, index, SandCell.WithUnsettled(grain | (3 << 8), true));
                AssertRefresh(direct, scheduled, false);
                SetBoth(direct, scheduled, index, SandCell.WithColorByte(grain, 53));
                SetBoth(direct, scheduled, index, grain);
                AssertRefresh(direct, scheduled, false, "A visual mutation restored before publication is a net no-op.");
                SetBoth(direct, scheduled, index, SandCell.WithColorByte(grain, 38));
                AssertRefresh(direct, scheduled, true);
                direct.ClearGrid();
                scheduled.ClearGrid();
                AssertRefresh(direct, scheduled, true);
                AssertRefresh(direct, scheduled, false);
                direct.Resize(2, 3);
                scheduled.Resize(2, 3);
                AssertRefresh(direct, scheduled, true, "Resized zero views still need publication.");
                AssertRefresh(direct, scheduled, false);
                direct.Resize(1, 1);
                scheduled.Resize(1, 1);
                AssertRefresh(direct, scheduled, true);
            }
        }

        [TestCase(0, 1)]
        [TestCase(0, -1)]
        [TestCase(1, 0)]
        [TestCase(-1, 0)]
        public void RefreshCompletesPendingWorkAndMatchesReferenceAcrossBatchSizes(int gx, int gy)
        {
            using (var direct = new JobBackend(12, 16))
            using (var scheduled = new JobBackend(12, 16))
            using (var reference = new SingleThreadBackend(12, 16))
            {
                var random = new System.Random(173);
                for (int index = 0; index < direct.CellCount; index++)
                {
                    int cell = random.Next(5) == 0 ? 2 : random.Next(3) == 0
                        ? SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 37), true) : 0;
                    SetBoth(direct, scheduled, index, cell);
                    reference.SetCell(index, cell);
                }
                var first = Parameters(gx, gy, 64, 1);
                var second = Parameters(gx, gy, 512, 16);
                for (int tick = 0; tick < 24; tick++)
                {
                    if (tick % 4 == 0)
                    {
                        int index = random.Next(direct.CellCount);
                        int cell = tick % 8 == 0 ? 0 : SandCell.Pack(ElementType.Sand, 0, 53);
                        SetBoth(direct, scheduled, index, cell);
                        reference.SetCell(index, cell);
                    }
                    direct.ScheduleStep(first);
                    scheduled.ScheduleStep(second);
                    reference.Step(first);
                    bool expectedPublication = ((ISandViewBackend)reference).RefreshViewBuffer();
                    // Refresh itself is responsible for finishing the outstanding tick.
                    AssertRefresh(direct, scheduled, expectedPublication, "tick " + tick);
                    CollectionAssert.AreEqual(reference.CurrentGrid.ToArray(), direct.CurrentGrid.ToArray(), "direct tick " + tick);
                    CollectionAssert.AreEqual(reference.CurrentGrid.ToArray(), scheduled.CurrentGrid.ToArray(), "scheduled tick " + tick);
                    CollectionAssert.AreEqual(reference.ViewBuffer.ToArray(), direct.ViewBuffer.ToArray(), "view tick " + tick);
                }
            }
        }

        [Test]
        public void MultipleTicksBeforePublicationAndAutomaticPolicyMatchScheduledExecution()
        {
            using (var automatic = new JobBackend(33, 33))
            using (var scheduled = new JobBackend(33, 33))
            {
                automatic.SetCell(0, SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 37), true));
                scheduled.SetCell(0, automatic.GetCell(0));
                StepParams parameters = Parameters(0, 1, 128, 10);
                for (int tick = 0; tick < 8; tick++)
                {
                    automatic.Step(parameters);
                    scheduled.Step(parameters);
                }
                Assert.That(automatic.RefreshViewBuffer(), Is.EqualTo(scheduled.RefreshViewBuffer(JobBackend.ViewRefreshExecution.Scheduled)));
                CollectionAssert.AreEqual(scheduled.ViewBuffer.ToArray(), automatic.ViewBuffer.ToArray());
                Assert.That(automatic.RefreshViewBuffer(), Is.False);
            }
        }

        private static StepParams Parameters(int gx, int gy, int updateBatch, int settleBatch) => new StepParams
        {
            GravityDirection = new GridCoord(gx, gy),
            DisableGravity = true,
            ConstantFallSpeed = 4,
            EnableVerticalSettle = true,
            EnableLateralMixing = true,
            LateralMixChance = 32768,
            LateralMixPasses = 3,
            MixingSeed = 239,
            UpdateBatchSize = updateBatch,
            SettleBatchSize = settleBatch
        };

        private static void SetBoth(JobBackend first, JobBackend second, int index, int cell)
        {
            first.SetCell(index, cell);
            second.SetCell(index, cell);
        }

        private static void AssertRefresh(JobBackend direct, JobBackend scheduled, bool expected, string message = null)
        {
            Assert.That(direct.RefreshViewBuffer(JobBackend.ViewRefreshExecution.Synchronous), Is.EqualTo(expected), message);
            Assert.That(scheduled.RefreshViewBuffer(JobBackend.ViewRefreshExecution.Scheduled), Is.EqualTo(expected), message);
            CollectionAssert.AreEqual(scheduled.ViewBuffer.ToArray(), direct.ViewBuffer.ToArray(), message);
        }
    }
}
#endif
