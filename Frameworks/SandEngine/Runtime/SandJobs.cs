#if !PLA_CREATIVE_BUILD
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using SandIntBuffer = Unity.Collections.NativeArray<int>;
using SandByteBuffer = Unity.Collections.NativeArray<byte>;
using SandViewBuffer = Unity.Collections.NativeArray<Com.Scheherazade.Common.Frameworks.SandEngine.SandViewCell>;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    [BurstCompile]
    internal struct CompactEligibleSandJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public SandIntBuffer grid;
        public SandStepSettings settings;
        public void Execute(int line)=>SandAlgorithmCore.CompactEligibleLine(grid,line,settings);
    }
    /// <summary>
    /// Resets the destination buffer, claim array and the vacated markers so every
    /// cell starts the step empty/unclaimed.
    /// </summary>
    [BurstCompile]
    public struct ClearGridJob : IJobParallelFor
    {
        [WriteOnly] public SandIntBuffer next;
        [WriteOnly] public SandIntBuffer claim;
        [WriteOnly] public SandByteBuffer vacated;

        public void Execute(int index)
        {
            next[index] = 0;
            claim[index] = 0;
            vacated[index] = 0;
        }
    }

    /// <summary>
    /// Single-pass sand update: reads <see cref="current"/> and writes <see cref="next"/>
    /// (double buffering). Gravity accumulates in the velocity byte (bits 8-15) every step
    /// and a grain free-runs that many cells straight down (capped at the terminal speed);
    /// when the drop is blocked it slides diagonally down slopes at the same fall speed,
    /// gated by friction, and stops at the pile edge to fall straight again. Moving
    /// particles atomically claim their destination cell via <see cref="claim"/>, so the
    /// whole grid is safe to process fully in parallel. A mover only targets cells that are
    /// empty in <see cref="current"/>, whose own workers write nothing, and an occupied
    /// source cell is never a valid claim target, which keeps every write to
    /// <see cref="next"/> race-free. Cells are 32-bit ints; the full value (type +
    /// velocity + payload) travels with a moving particle.
    /// </summary>
    [BurstCompile]
    public struct SandUpdateJob : IJobParallelFor
    {
        private const uint HashMulX = 73856093u;
        private const uint HashMulY = 19349663u;
        private const uint HashScramble = 2246822519u;
        private const int HashShiftXor = 16;
        private const int HashShiftFinal = 13;
        private const int TypeMask = 0xFF;
        private const int VelocityShift = 8;
        private const int VelocityMask = 0x0000FF00;

        [ReadOnly] public SandIntBuffer current;
        [WriteOnly] [NativeDisableParallelForRestriction] public SandIntBuffer next;
        [NativeDisableParallelForRestriction] public SandIntBuffer claim;
        [WriteOnly] [NativeDisableParallelForRestriction] public SandByteBuffer vacated;

        public int width;
        public int height;

        /// <summary>Acceleration applied to falling sand each step, in cells per step squared.</summary>
        public int gravityPerStep;

        /// <summary>Terminal fall speed cap for sand, in cells per step.</summary>
        public int maxFallSpeed;

        /// <summary>
        /// Chance (0..65536) that a grain blocked from falling slides diagonally
        /// instead of resting. Derived from the caller's slide friction.
        /// </summary>
        public int slideChance;

        /// <summary>Cardinal gravity direction: (0,1) = down (default), (0,-1) = up, (1,0) = right, (-1,0) = left.</summary>
        public int gx;
        public int gy;

        public unsafe void Execute(int index)
        {
            int cell = current[index];
            if (cell == 0)
            {
                return;
            }

            int element = cell & TypeMask;
            if (element == (int)ElementType.Wall)
            {
                next[index] = cell;
                return;
            }

            int x = index % width;
            int y = index / width;

            // Gravity: add the per-step acceleration to the stored fall speed and
            // clamp to the terminal velocity. The velocity byte (bits 8-15) travels
            // with the cell, so a falling grain keeps accelerating across ticks.
            int fallSpeed = GetVelocity(cell) + gravityPerStep;
            if (fallSpeed > maxFallSpeed)
            {
                fallSpeed = maxFallSpeed;
            }

            // Straight free run along the gravity direction: count the contiguous
            // empty cells ahead of this grain, capped at fallSpeed. The scan reads
            // `current`, so it stops at the first occupied cell - a fast grain can
            // never jump over an obstacle or a resting pile.
            int freeRun = 0;
            int probeX = x + gx;
            int probeY = y + gy;
            while (freeRun < fallSpeed && InBounds(probeX, probeY) && current[probeY * width + probeX] == 0)
            {
                freeRun++;
                probeX += gx;
                probeY += gy;
            }

            int target = -1;
            int targetX = x;
            int targetY = y;
            int traveled;

            if (freeRun > 0)
            {
                // Straight fall along gravity: land after the free run, keep accelerating.
                targetX = x + gx * freeRun;
                targetY = y + gy * freeRun;
                target = targetY * width + targetX;
                traveled = freeRun;
            }
            else if (TrySlide(x, y, fallSpeed, out targetX, out targetY))
            {
                // Diagonal slide down a slope: moves up to fallSpeed cells, so
                // gravity also controls how fast sand runs off pile edges.
                target = targetY * width + targetX;
                traveled = (targetX - x) * gx + (targetY - y) * gy;
            }
            else
            {
                // Resting: forget the accumulated speed so a freed grain
                // re-accelerates from zero.
                next[index] = WithVelocity(cell, 0);
                return;
            }

            // A target cell can be contested by the grains that want to land on it:
            // the straight faller above it (possibly several rows up, since it
            // free-runs up to maxFallSpeed cells in one tick) and the diagonal
            // sliders approaching from both sides. Whichever worker executes first
            // used to win the claim, and batches run in ascending index order, so
            // the right-slider always won and sand flowed right. The winner is
            // decided by a fixed priority: the direct faller always wins (sand
            // falls straight before it slides diagonally, which keeps streams and
            // piles solid - a grain above empty space never stalls), and when no
            // faller exists a slider approaching from the left outranks one from
            // the right. Every contender runs the same comparison over the same
            // candidate set, so the outcome is independent of execution order and
            // scheduling.
            if (!CanWinClaim(x, y, targetX, targetY))
            {
                next[index] = cell;
                return;
            }

            int* claimPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(claim);
            if (Interlocked.CompareExchange(ref claimPtr[target], element, 0) != 0)
            {
                next[index] = cell;
                return;
            }

            // Persist the distance actually traveled in the velocity byte so the
            // fall keeps accelerating on the next step.
            next[target] = WithVelocity(cell, traveled);
            next[index] = 0;
            vacated[index] = 1;
        }

        /// <summary>
        /// Decides whether this cell may claim its target. A straight faller whose
        /// free run reaches the target always wins, so a cell with empty space
        /// along gravity can never be blocked from falling by a diagonal slider.
        /// The faller can be several cells away (it free-runs up to maxFallSpeed
        /// cells in one tick), so sliders scan back along gravity from the target
        /// for an inbound grain; a wall beyond the target blocks every faller
        /// behind it. Without a faller, a slider approaching from the negative
        /// perpendicular side outranks one from the positive side (sliders on the
        /// same side can never contend, since a deeper one's path is blocked by
        /// the shallower one). Every contender runs the same comparisons over the
        /// same candidate set, so exactly one wins regardless of scheduling.
        /// </summary>
        private bool CanWinClaim(int x, int y, int targetX, int targetY)
        {
            // Straight faller: the move is parallel to gravity (same line along the
            // gravity axis) -> this cell is a direct faller and always wins.
            if ((targetX - x) * gy == (targetY - y) * gx)
            {
                return true;
            }

            int px = gy;
            int py = gx;

            // Scan back along gravity from the target for an inbound faller: the
            // first occupied cell within maxFallSpeed cells (the furthest any grain
            // can free-run this tick) decides. Sand means a faller will claim the
            // target, so this slide defers; a wall stops every faller behind it,
            // so the slide proceeds.
            for (int step = 1; step <= maxFallSpeed; step++)
            {
                int scanX = targetX - gx * step;
                int scanY = targetY - gy * step;
                if (!InBounds(scanX, scanY))
                {
                    break;
                }

                int scanCell = current[scanY * width + scanX];
                if (scanCell == 0)
                {
                    continue;
                }

                if ((scanCell & TypeMask) == (int)ElementType.Sand)
                {
                    return false;
                }

                break;
            }

            // A slider approaching from the opposite perpendicular side can contend
            // for the target, so scan the other diagonal within maxFallSpeed cells.
            // Sand there means we only win when coming from the negative side (a
            // positive-approaching slider defers). A wall stops any slider behind it.
            int dir = ((targetX - x) * px + (targetY - y) * py) > 0 ? 1 : -1;
            for (int step = 1; step <= maxFallSpeed; step++)
            {
                int otherX = targetX + dir * px * step - gx * step;
                int otherY = targetY + dir * py * step - gy * step;
                if (!InBounds(otherX, otherY))
                {
                    break;
                }

                int otherCell = current[otherY * width + otherX];
                if (otherCell == 0)
                {
                    continue;
                }

                return dir == 1;
            }

            return true;
        }

        /// <summary>
        /// Attempts a velocity-based diagonal slide along a slope. The grain walks
        /// up to fallSpeed cells along gravity while the slope holds beside it; at
        /// the pile edge (open space ahead) it stops and falls straight on the next
        /// tick. Friction gates the start: a failed position-hash roll rests the
        /// grain instead, forming the angle of repose. "Down"/"left"/"right" are all
        /// relative to the gravity direction.
        /// </summary>
        private bool TrySlide(int x, int y, int fallSpeed, out int targetX, out int targetY)
        {
            targetX = x;
            targetY = y;

            // Perpendicular to gravity (rotated 90°).
            int px = gy;
            int py = gx;

            // One step along gravity from the current cell.
            int downX = x + gx;
            int downY = y + gy;

            bool canSlideA = InBounds(downX + px, downY + py)
                && current[(downY + py) * width + (downX + px)] == 0;
            bool canSlideB = InBounds(downX - px, downY - py)
                && current[(downY - py) * width + (downX - px)] == 0;
            if ((!canSlideA && !canSlideB) || !CanSlide(x, y))
            {
                return false;
            }

            int d = canSlideA && canSlideB
                ? ((Hash(x, y) & 1u) == 0 ? -1 : 1)
                : (canSlideB ? -1 : 1);

            int slideSteps = 0;
            int tx = downX + d * px;
            int ty = downY + d * py;
            for (int step = 1; step <= fallSpeed && InBounds(tx, ty)
                && current[ty * width + tx] == 0; step++)
            {
                slideSteps = step;

                int edgeX = tx + gx;
                int edgeY = ty + gy;
                if (InBounds(edgeX, edgeY) && current[edgeY * width + edgeX] == 0)
                {
                    // Pile edge: the slope ends, stop here and fall next tick.
                    break;
                }

                tx += gx + d * px;
                ty += gy + d * py;
            }

            if (slideSteps == 0)
            {
                return false;
            }

            targetX = x + (gx + d * px) * slideSteps;
            targetY = y + (gy + d * py) * slideSteps;
            return true;
        }

        /// <summary>Reads the fall speed stored in the velocity byte.</summary>
        private static int GetVelocity(int cell)
        {
            return (cell & VelocityMask) >> VelocityShift;
        }

        private bool InBounds(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        /// <summary>
        /// Friction roll: a grain blocked from falling only slides when its
        /// position hash passes the slide chance. Position-based, so the outcome
        /// is stable across ticks and independent of job scheduling.
        /// </summary>
        private bool CanSlide(int x, int y)
        {
            return (Hash(x, y) & 0xFFFFu) < (uint)slideChance;
        }

        /// <summary>Replaces the velocity byte, keeping the element type and payload.</summary>
        private static int WithVelocity(int cell, int velocity)
        {
            return (cell & ~VelocityMask) | ((velocity & 0xFF) << VelocityShift);
        }

        private static uint Hash(int x, int y)
        {
            // Position-only: the outcome for a cell is stable across ticks, so grains
            // keep a consistent slide direction instead of jittering every step.
            uint value = (uint)x * HashMulX ^ (uint)y * HashMulY;
            value ^= value >> HashShiftXor;
            value *= HashScramble;
            value ^= value >> HashShiftFinal;
            return value;
        }
    }

    /// <summary>
    /// Single-pass settle along the gravity axis. After the movement pass, grains
    /// behind any void that was created this tick move one cell along gravity at a
    /// time. This cascades the void in one step while preserving the relative order
    /// of grains and keeping air pockets that are not connected to a fresh void.
    /// Walls stop the cascade; only sand is pulled, never walls.
    /// Each line perpendicular to gravity is processed independently, so the job is
    /// fully parallel across lines with no atomic operations needed.
    /// </summary>
    [BurstCompile]
    public struct FillVoidsJob : IJobParallelFor
    {
        private const int TypeMask = 0xFF;

        [NativeDisableParallelForRestriction] public SandIntBuffer next;
        [ReadOnly] public SandByteBuffer vacated;

        public int width;
        public int height;
        public int gx;
        public int gy;

        public void Execute(int line)
        {
            bool inVoid = false;

            if (gx == 0)
            {
                // Vertical gravity: one pass per column (line = x), scan along y.
                int x = line;
                bool ascending = gy > 0;
                int start = ascending ? 0 : height - 1;
                int end = ascending ? height : -1;
                int step = ascending ? 1 : -1;
                for (int y = start; y != end; y += step)
                {
                    int index = y * width + x;
                    int cell = next[index];
                    bool isVacated = vacated[index] != 0;
                    if (cell == 0 && (isVacated || inVoid))
                    {
                        inVoid = true;
                        int behind = (y - step) * width + x;
                        if (y - step >= 0 && y - step < height)
                        {
                            int behindCell = next[behind];
                            if ((behindCell & TypeMask) == (int)ElementType.Sand)
                            {
                                next[index] = behindCell;
                                next[behind] = 0;
                            }
                        }
                    }
                    else
                    {
                        inVoid = false;
                        if (cell != 0)
                        {
                            next[index] = cell;
                        }
                    }
                }
            }
            else
            {
                // Horizontal gravity: one pass per row (line = y), scan along x.
                int y = line;
                bool ascending = gx > 0;
                int start = ascending ? 0 : width - 1;
                int end = ascending ? width : -1;
                int step = ascending ? 1 : -1;
                for (int x = start; x != end; x += step)
                {
                    int index = y * width + x;
                    int cell = next[index];
                    bool isVacated = vacated[index] != 0;
                    if (cell == 0 && (isVacated || inVoid))
                    {
                        inVoid = true;
                        int behind = y * width + (x - step);
                        if (x - step >= 0 && x - step < width)
                        {
                            int behindCell = next[behind];
                            if ((behindCell & TypeMask) == (int)ElementType.Sand)
                            {
                                next[index] = behindCell;
                                next[behind] = 0;
                            }
                        }
                    }
                    else
                    {
                        inVoid = false;
                        if (cell != 0)
                        {
                            next[index] = cell;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Extracts element type and packed color/variant into a two-byte view.</summary>
    [BurstCompile]
    public struct ExtractViewCellsJob : IJobParallelFor
    {
        private const int TypeMask = 0xFF;
        private const int PayloadShift = 16;

        [ReadOnly] public SandIntBuffer source;
        [WriteOnly] public SandViewBuffer dest;

        public void Execute(int index)
        {
            int cell = source[index];
            dest[index] = new SandViewCell
            {
                elementType = (byte)(cell & TypeMask),
                packedColorVariant = (byte)((cell >> PayloadShift) & 0xFF)
            };
        }
    }

    /// <summary>
    /// Extracts and compares render cells in disjoint chunks. One flag per chunk
    /// avoids a contended atomic operation per changed grain. Physics-only bits
    /// do not invalidate the rendered texture.
    /// </summary>
    [BurstCompile]
    internal struct RefreshSandViewJob : IJobParallelFor
    {
        private const int ChunkSize = 1024;
        [ReadOnly] public SandIntBuffer source;
        [NativeDisableParallelForRestriction] public SandViewBuffer dest;
        [WriteOnly] public SandIntBuffer changedChunks;

        public static int GetChunkCount(int cellCount) => (cellCount - 1) / ChunkSize + 1;

        public void Execute(int chunk)
        {
            int start = chunk * ChunkSize;
            int end = start + System.Math.Min(ChunkSize, source.Length - start);
            bool changed = false;
            for (int index = start; index < end; index++)
            {
                int cell = source[index];
                byte element = (byte)(cell & 0xFF);
                byte color = (byte)((cell >> 16) & 0xFF);
                SandViewCell previous = dest[index];
                if (previous.elementType == element && previous.packedColorVariant == color) continue;
                dest[index] = new SandViewCell { elementType = element, packedColorVariant = color };
                changed = true;
            }
            changedChunks[chunk] = changed ? 1 : 0;
        }
    }

    /// <summary>Counts non-empty cells with an interlocked increment (exact regardless of order).</summary>
    [BurstCompile]
    public struct CountElementsJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer grid;
        [NativeDisableParallelForRestriction] public SandIntBuffer count;

        public unsafe void Execute(int index)
        {
            if (grid[index] == 0)
            {
                return;
            }

            int* countPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(count);
            Interlocked.Increment(ref countPtr[0]);
        }
    }

    /// <summary>Counts only cells whose element type is sand.</summary>
    [BurstCompile]
    public struct CountSandElementsJob : IJobParallelFor
    {
        private const int TypeMask = 0xFF;

        [ReadOnly] public SandIntBuffer grid;
        [NativeDisableParallelForRestriction] public SandIntBuffer count;

        public unsafe void Execute(int index)
        {
            if ((grid[index] & TypeMask) != (int)ElementType.Sand)
            {
                return;
            }

            int* countPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(count);
            Interlocked.Increment(ref countPtr[0]);
        }
    }
}
#endif
