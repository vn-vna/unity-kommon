#if !PLA_CREATIVE_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Com.Scheherazade.Common.Frameworks.GridSystem;
using Unity.Jobs;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    // Explicit diagnostic entry points, deliberately not automatic NUnit tests.
    // Invoke from the Editor after native compilation; no gameplay scene is changed.
    public static class NativeSandBackendBenchmarks
    {
        [Serializable]
        public sealed class Report
        {
            public string scope;
            public string unityVersion;
            public string platform;
            public bool burstEnabled;
            public int blocks;
            public int iterationsPerBlock;
            public List<Row> rows = new List<Row>();
        }

        [Serializable]
        public sealed class Row
        {
            public string workload;
            public string execution;
            public int cells;
            public int pools;
            public int updateBatch;
            public int settleBatch;
            public double medianMs;
            public double p95Ms;
            public double p99Ms;
            public long mainThreadAllocatedBytes;
            public int publicationRequests;
            public string finalStateHash;
        }

        public static Report RunViewRefresh()
        {
            const int blocks = 5;
            const int iterations = 200;
            var report = CreateReport("Editor native extraction only; excludes texture upload and device claims", blocks, iterations);
            int[,] sizes = { { 1, 1 }, { 32, 32 }, { 33, 33 }, { 48, 48 }, { 64, 64 }, { 126, 108 }, { 128, 128 }, { 256, 256 } };
            for (int size = 0; size < sizes.GetLength(0); size++)
            for (int visual = 0; visual < 2; visual++)
            {
                int width = sizes[size, 0], height = sizes[size, 1];
                using (var direct = new JobBackend(width, height))
                using (var scheduled = new JobBackend(width, height))
                {
                    int grain = SandCell.Pack(ElementType.Sand, 0, 37);
                    for (int index = 0; index < direct.CellCount; index++)
                    {
                        direct.SetCell(index, grain);
                        scheduled.SetCell(index, grain);
                    }
                    direct.RefreshViewBuffer(JobBackend.ViewRefreshExecution.Synchronous);
                    scheduled.RefreshViewBuffer(JobBackend.ViewRefreshExecution.Scheduled);
                    int first = visual == 0 ? grain | (1 << 8) : grain;
                    int second = visual == 0 ? grain | (2 << 8) : SandCell.WithColorByte(grain, 38);
                    long[] directTicks = new long[blocks * iterations];
                    long[] scheduledTicks = new long[blocks * iterations];
                    // Both entry points are warmed; neither measures cache-hit refreshes.
                    var warmup = Stopwatch.StartNew();
                    do
                    {
                        MeasureViews(direct, JobBackend.ViewRefreshExecution.Synchronous, first, second, directTicks, 0, 60, out _);
                        MeasureViews(scheduled, JobBackend.ViewRefreshExecution.Scheduled, first, second, scheduledTicks, 0, 60, out _);
                    } while (warmup.ElapsedMilliseconds < 250);

                    long directAllocation = 0, scheduledAllocation = 0;
                    int directPublications = 0, scheduledPublications = 0;
                    for (int block = 0; block < blocks; block++)
                    {
                        for (int order = 0; order < 2; order++)
                        {
                            bool useDirect = ((block + order) & 1) == 0;
                            long allocated = MeasureViews(useDirect ? direct : scheduled,
                                useDirect ? JobBackend.ViewRefreshExecution.Synchronous : JobBackend.ViewRefreshExecution.Scheduled,
                                first, second, useDirect ? directTicks : scheduledTicks, block * iterations, iterations,
                                out int publications);
                            if (useDirect) { directAllocation += allocated; directPublications += publications; }
                            else { scheduledAllocation += allocated; scheduledPublications += publications; }
                        }
                    }
                    int expected = visual == 0 ? 0 : blocks * iterations;
                    if (directPublications != expected || scheduledPublications != expected)
                        throw new InvalidOperationException("The benchmark did not exercise the intended dirty-state extraction workload.");
                    string workload = width + "x" + height + (visual == 0 ? " physics-only mutation" : " visual mutation");
                    report.rows.Add(Summarize(directTicks, workload, "Synchronous", direct.CellCount, 1, directAllocation, directPublications));
                    report.rows.Add(Summarize(scheduledTicks, workload, "Scheduled", scheduled.CellCount, 1, scheduledAllocation, scheduledPublications));
                }
            }
            return report;
        }

        public static Report RunBatchScheduling()
        {
            const int blocks = 5;
            const int iterations = 40;
            var report = CreateReport("Editor native backend barrier only; excludes collapse, collection and presentation", blocks, iterations);
            int[,] largest = { { 126, 108 } };
            int[,] many = { { 18, 36 }, { 36, 36 }, { 18, 36 }, { 18, 36 }, { 18, 36 }, { 36, 36 }, { 18, 36 }, { 18, 36 } };
            SweepBatches(report, largest, "single largest pool", blocks, iterations);
            SweepBatches(report, many, "eight campaign-sized pools", blocks, iterations);
            return report;
        }

        private static void SweepBatches(Report report, int[,] sizes, string workload, int blocks, int iterations)
        {
            int[] updates = { 128, 64, 256, 512 };
            Row best = null;
            string expectedHash = null;
            foreach (int update in updates)
            {
                Row row = MeasureBatches(sizes, workload, update, 10, blocks, iterations);
                expectedHash = CheckHash(expectedHash, row.finalStateHash);
                report.rows.Add(row);
                if (best == null || row.medianMs < best.medianMs) best = row;
            }
            foreach (int settle in new[] { 1, 4, 16 })
            {
                Row row = MeasureBatches(sizes, workload, best.updateBatch, settle, blocks, iterations);
                CheckHash(expectedHash, row.finalStateHash);
                report.rows.Add(row);
            }
        }

        private static Row MeasureBatches(int[,] sizes, string workload, int updateBatch, int settleBatch, int blocks, int iterations)
        {
            var backends = new JobBackend[sizes.GetLength(0)];
            long[] ticks = new long[blocks * iterations];
            int cells = 0;
            try
            {
                for (int pool = 0; pool < backends.Length; pool++)
                {
                    var backend = new JobBackend(sizes[pool, 0], sizes[pool, 1]);
                    backends[pool] = backend;
                    cells += backend.CellCount;
                    for (int index = 0; index < backend.CellCount; index++)
                        if (index / backend.Width >= backend.Height / 2)
                            backend.SetCell(index, SandCell.Pack(ElementType.Sand, 0, 37 + index % 8));
                }
                var parameters = new StepParams
                {
                    GravityDirection = new GridCoord(0, 1), DisableGravity = true, ConstantFallSpeed = 10,
                    EnableVerticalSettle = true, EnableLateralMixing = true, LateralMixPasses = 3,
                    LateralMixChance = 393, SlideChance = 65536, MixingSeed = 239,
                    UpdateBatchSize = updateBatch, SettleBatchSize = settleBatch
                };
                for (int warm = 0; warm < 40; warm++) StepAll(backends, parameters);
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < ticks.Length; sample++)
                {
                    // Identical disturbances across every candidate, outside barrier timing.
                    if (sample % 4 == 0)
                    {
                        for (int pool = 0; pool < backends.Length; pool++)
                        {
                            JobBackend backend = backends[pool];
                            int index = (sample * 17 + pool * 31) % backend.CellCount;
                            backend.SetCell(index, sample % 8 == 0 ? 0
                                : SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, 53), true));
                        }
                    }
                    long start = Stopwatch.GetTimestamp();
                    StepAll(backends, parameters);
                    ticks[sample] = Stopwatch.GetTimestamp() - start;
                }
                allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                Row row = Summarize(ticks, workload, "Overlapped Jobs", cells, backends.Length, allocation, 0);
                row.updateBatch = updateBatch;
                row.settleBatch = settleBatch;
                row.finalStateHash = Hash(backends);
                return row;
            }
            finally
            {
                foreach (JobBackend backend in backends) if (backend != null) backend.Dispose();
            }
        }

        private static long MeasureViews(JobBackend backend, JobBackend.ViewRefreshExecution execution,
            int first, int second, long[] samples, int offset, int count, out int publications)
        {
            publications = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < count; index++)
            {
                backend.SetCell(backend.CellCount - 1, (index & 1) == 0 ? first : second);
                long start = Stopwatch.GetTimestamp();
                bool changed = backend.RefreshViewBuffer(execution);
                samples[offset + index] = Stopwatch.GetTimestamp() - start;
                if (changed) publications++;
            }
            return GC.GetAllocatedBytesForCurrentThread() - allocated;
        }

        private static void StepAll(JobBackend[] backends, StepParams parameters)
        {
            try
            {
                foreach (JobBackend backend in backends) backend.ScheduleStep(parameters);
                JobHandle.ScheduleBatchedJobs();
            }
            finally
            {
                foreach (JobBackend backend in backends) backend.CompleteStep();
            }
        }

        private static Report CreateReport(string scope, int blocks, int iterations)
        {
            if (!Unity.Burst.BurstCompiler.IsEnabled) throw new InvalidOperationException("Enable Burst before running native benchmarks.");
            return new Report { scope = scope, unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), burstEnabled = true, blocks = blocks, iterationsPerBlock = iterations };
        }

        private static Row Summarize(long[] ticks, string workload, string execution, int cells, int pools, long allocated, int publications)
        {
            Array.Sort(ticks);
            double scale = 1000d / Stopwatch.Frequency;
            return new Row { workload = workload, execution = execution, cells = cells, pools = pools,
                medianMs = (ticks[(ticks.Length - 1) / 2] + ticks[ticks.Length / 2]) * 0.5d * scale,
                p95Ms = ticks[(int)Math.Ceiling(ticks.Length * 0.95d) - 1] * scale,
                p99Ms = ticks[(int)Math.Ceiling(ticks.Length * 0.99d) - 1] * scale,
                mainThreadAllocatedBytes = allocated, publicationRequests = publications };
        }

        private static string Hash(JobBackend[] backends)
        {
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                foreach (JobBackend backend in backends)
                {
                    var grid = backend.CurrentGrid;
                    for (int index = 0; index < grid.Length; index++)
                    {
                        hash ^= (uint)grid[index];
                        hash *= 1099511628211UL;
                    }
                }
            }
            return hash.ToString("X16");
        }

        private static string CheckHash(string expected, string actual)
        {
            if (expected != null && expected != actual) throw new InvalidOperationException("Batch-size candidates produced different final grids.");
            return actual;
        }
    }
}
#endif
