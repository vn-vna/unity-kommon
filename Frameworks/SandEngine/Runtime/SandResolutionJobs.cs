#if !PLA_CREATIVE_BUILD
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using SandIntBuffer = Unity.Collections.NativeArray<int>;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>Clears tick-local movement intents and target winners.</summary>
    [BurstCompile]
    public struct ClearMovementEngineJob : IJobParallelFor
    {
        [WriteOnly] public SandIntBuffer engine;

        public void Execute(int index)
        {
            engine[index] = 0;
        }
    }

    /// <summary>Resolves source intents and deterministic destination ownership.</summary>
    [BurstCompile]
    public struct ResolveSandMovementJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [NativeDisableParallelForRestriction] public SandIntBuffer engine;

        public int width;
        public int height;
        public bool disableGravity;
        public bool enableVerticalSettle;
        public int constantFallSpeed;
        public int gravityPerStep;
        public int maxFallSpeed;
        public int slideChance;
        public SandContactSlip contactSlip;
        public bool preventTemporaryEdgeVacancies;
        public bool enableLateralMixing;
        public int lateralMixChance;
        public int lateralMixPasses;
        public int mixingSeed;
        public int simulationSeed;
        public int gravityX;
        public int gravityY;

        public void Execute(int index)
        {
            SandStepSettings settings = CreateSettings();
            if (!SandAlgorithmCore.TryResolveTarget(
                current,
                index,
                settings,
                out int targetIndex
            ))
            {
                return;
            }

            engine[index] = targetIndex + 1;
            ClaimTarget(index, targetIndex, settings);
        }

        private unsafe void ClaimTarget(
            int sourceIndex,
            int targetIndex,
            SandStepSettings settings
        )
        {
            int* enginePointer = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(engine);
            int candidate = sourceIndex + 1;
            int incumbent = Interlocked.CompareExchange(
                ref enginePointer[targetIndex],
                0,
                0
            );

            while (incumbent != candidate)
            {
                if (incumbent != 0
                    && !SandAlgorithmCore.Outranks(
                        sourceIndex,
                        incumbent - 1,
                        targetIndex,
                        settings
                    ))
                {
                    return;
                }

                int observed = Interlocked.CompareExchange(
                    ref enginePointer[targetIndex],
                    candidate,
                    incumbent
                );
                if (observed == incumbent)
                {
                    return;
                }

                incumbent = observed;
            }
        }

        private SandStepSettings CreateSettings()
        {
            return new SandStepSettings(
                width,
                height,
                disableGravity,
                constantFallSpeed,
                gravityPerStep,
                maxFallSpeed,
                slideChance,
                preventTemporaryEdgeVacancies,
                enableLateralMixing,
                lateralMixChance,
                lateralMixPasses,
                mixingSeed,
                gravityX,
                gravityY,
                simulationSeed,
                enableVerticalSettle,
                contactSlip
            );
        }
    }

    /// <summary>Commits resolved movement without reading the destination buffer.</summary>
    [BurstCompile]
    public struct CommitSandMovementJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [ReadOnly] public SandIntBuffer engine;
        [WriteOnly] public SandIntBuffer next;

        public int width;
        public int height;
        public int gravityX;
        public int gravityY;

        public void Execute(int index)
        {
            SandStepSettings settings = new SandStepSettings(
                width,
                height,
                false,
                1,
                1,
                1,
                0,
                false,
                false,
                0,
                1,
                0,
                gravityX,
                gravityY
            );
            next[index] = SandAlgorithmCore.CommitCell(
                current,
                engine,
                index,
                settings
            );
        }
    }

    /// <summary>Resolves one level-synchronous lateral dependency pass.</summary>
    [BurstCompile]
    public struct ResolveLateralMixingJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [ReadOnly] public SandIntBuffer inputIntents;
        [WriteOnly] public SandIntBuffer outputIntents;

        public int width;
        public int height;
        public int lateralMixChance;
        public int lateralMixPasses;
        public int mixingSeed;
        public int simulationSeed;
        public bool preventTemporaryEdgeVacancies;
        public int gravityX;
        public int gravityY;

        public void Execute(int sourceIndex)
        {
            SandStepSettings settings = CreateSettings();
            outputIntents[sourceIndex] =
                SandAlgorithmCore.ResolveLateralIntentForSource(
                    current,
                    inputIntents,
                    sourceIndex,
                    settings
                );
        }

        private SandStepSettings CreateSettings()
        {
            return new SandStepSettings(
                width,
                height,
                false,
                1,
                1,
                1,
                0,
                preventTemporaryEdgeVacancies,
                true,
                lateralMixChance,
                lateralMixPasses,
                mixingSeed,
                gravityX,
                gravityY,
                simulationSeed
            );
        }
    }

    /// <summary>
    /// Resolves gravity-line vacancies and can preserve earlier valid moves.
    /// </summary>
    [BurstCompile]
    public struct ResolveSandCascadeJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [ReadOnly] public SandIntBuffer inputIntents;
        [NativeDisableParallelForRestriction]
        public SandIntBuffer outputIntents;

        public int width;
        public int height;
        public int gravityX;
        public int gravityY;
        public bool preserveExistingMoves;

        public void Execute(int line)
        {
            SandStepSettings settings = new SandStepSettings(
                width,
                height,
                false,
                1,
                1,
                1,
                0,
                false,
                false,
                0,
                1,
                0,
                gravityX,
                gravityY
            );
            SandAlgorithmCore.ResolveCascadeLine(
                current,
                inputIntents,
                outputIntents,
                line,
                settings,
                preserveExistingMoves
            );
        }
    }

    [BurstCompile]
    public struct BuildSandProjectedStatesJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [ReadOnly] public SandIntBuffer intents;
        [WriteOnly] public SandIntBuffer projectedStates;

        public int width;
        public int height;
        public int gravityX;
        public int gravityY;

        public void Execute(int index)
        {
            SandStepSettings settings = CreateSettings();
            projectedStates[index] = SandAlgorithmCore.ProjectCellState(
                current,
                intents,
                index,
                settings
            );
        }

        private SandStepSettings CreateSettings()
        {
            return new SandStepSettings(
                width,
                height,
                false,
                1,
                1,
                1,
                0,
                false,
                false,
                0,
                1,
                0,
                gravityX,
                gravityY
            );
        }
    }

    [BurstCompile]
    public struct DetectSandReconciliationRiskJob : IJobParallelFor
    {
        [ReadOnly] public SandIntBuffer current;
        [ReadOnly] public SandIntBuffer projectedStates;
        [NativeDisableParallelForRestriction]
        public SandIntBuffer riskByLine;

        public int width;
        public int height;
        public int gravityX;
        public int gravityY;

        public void Execute(int line)
        {
            SandStepSettings settings = new SandStepSettings(
                width,
                height,
                false,
                1,
                1,
                1,
                0,
                false,
                false,
                0,
                1,
                0,
                gravityX,
                gravityY
            );
            riskByLine[line] = SandAlgorithmCore.HasProjectedLineRisk(
                current,
                projectedStates,
                line,
                settings
            )
                ? 1
                : 0;
        }
    }

    [BurstCompile]
    public struct ReconcileSandIntentsJob : IJob
    {
        [ReadOnly] public SandIntBuffer current;
        public SandIntBuffer intents;
        public SandIntBuffer projectedStates;
        public SandIntBuffer floodQueue;

        public int width;
        public int height;
        public int gravityX;
        public int gravityY;
        internal SandStepSettings stepSettings;

        public void Execute()
        {
            SandStepSettings settings = stepSettings.width > 0 ? stepSettings : new SandStepSettings(
                width, height, false, 1, 1, 1, 0, false, false, 0, 1, 0, gravityX, gravityY
            );
            SandAlgorithmCore.ReconcileIntents(
                current,
                intents,
                projectedStates,
                floodQueue,
                settings
            );
        }
    }

    /// <summary>Burst-compiled deterministic step executed synchronously by Web backends.</summary>
    [BurstCompile]
    internal struct SingleThreadSandStepJob : IJob
    {
        [ReadOnly] public SandIntBuffer current;
        public SandIntBuffer firstIntents;
        public SandIntBuffer secondIntents;
        public SandIntBuffer reconciliationQueue;

        public int width;
        public int height;
        public bool disableGravity;
        public int constantFallSpeed;
        public int gravityPerStep;
        public int maxFallSpeed;
        public int slideChance;
        public SandContactSlip contactSlip;
        public bool preventTemporaryEdgeVacancies;
        public bool enableLateralMixing;
        public int lateralMixChance;
        public int lateralMixPasses;
        public int mixingSeed;
        public int simulationSeed;
        public int gravityX;
        public int gravityY;
        public bool enableVerticalSettle;

        public void Execute()
        {
            SandStepSettings settings = new SandStepSettings(
                width,
                height,
                disableGravity,
                constantFallSpeed,
                gravityPerStep,
                maxFallSpeed,
                slideChance,
                preventTemporaryEdgeVacancies,
                enableLateralMixing,
                lateralMixChance,
                lateralMixPasses,
                mixingSeed,
                gravityX,
                gravityY,
                simulationSeed,
                enableVerticalSettle,
                contactSlip
            );

            for (int index = 0; index < firstIntents.Length; index++)
            {
                firstIntents[index] = 0;
            }

            for (int sourceIndex = 0; sourceIndex < current.Length; sourceIndex++)
            {
                ResolveSource(firstIntents, sourceIndex, settings);
            }

            SandIntBuffer inputIntents = firstIntents;
            SandIntBuffer outputIntents = secondIntents;
            int mixingPassCount = SandAlgorithmCore.GetLateralMixPassCount(
                settings
            );
            for (int pass = 0; pass < mixingPassCount; pass++)
            {
                ResolveLateralMixing(inputIntents, outputIntents, settings);
                Swap(ref inputIntents, ref outputIntents);
            }

            if (enableVerticalSettle)
            {
                ResolveCascade(
                    inputIntents,
                    outputIntents,
                    settings,
                    settings.enableLateralMixing
                );
                Swap(ref inputIntents, ref outputIntents);
                BuildProjectedStates(
                    inputIntents,
                    outputIntents,
                    settings
                );
                DetectReconciliationRisk(outputIntents, settings);
                SandAlgorithmCore.ReconcileIntents(
                    current,
                    inputIntents,
                    outputIntents,
                    reconciliationQueue,
                    settings
                );
            }

            Commit(inputIntents, outputIntents, settings);
        }

        private void BuildProjectedStates(
            SandIntBuffer intents,
            SandIntBuffer projectedStates,
            SandStepSettings settings
        )
        {
            for (int index = 0; index < current.Length; index++)
            {
                projectedStates[index] = SandAlgorithmCore.ProjectCellState(
                    current,
                    intents,
                    index,
                    settings
                );
            }
        }

        private void DetectReconciliationRisk(
            SandIntBuffer projectedStates,
            SandStepSettings settings
        )
        {
            int lineCount = SandAlgorithmCore.GetGravityLineCount(settings);
            for (int line = 0; line < lineCount; line++)
            {
                reconciliationQueue[line] =
                    SandAlgorithmCore.HasProjectedLineRisk(
                        current,
                        projectedStates,
                        line,
                        settings
                    )
                        ? 1
                        : 0;
            }
        }

        private void ResolveSource(
            SandIntBuffer intents,
            int sourceIndex,
            SandStepSettings settings
        )
        {
            if (!SandAlgorithmCore.TryResolveTarget(
                current,
                sourceIndex,
                settings,
                out int targetIndex
            ))
            {
                return;
            }

            intents[sourceIndex] = targetIndex + 1;
            int incumbentPlusOne = intents[targetIndex];
            if (incumbentPlusOne == 0
                || SandAlgorithmCore.Outranks(
                    sourceIndex,
                    incumbentPlusOne - 1,
                    targetIndex,
                    settings
                ))
            {
                intents[targetIndex] = sourceIndex + 1;
            }
        }

        private void ResolveLateralMixing(
            SandIntBuffer inputIntents,
            SandIntBuffer outputIntents,
            SandStepSettings settings
        )
        {
            for (int index = 0; index < current.Length; index++)
            {
                outputIntents[index] =
                    SandAlgorithmCore.ResolveLateralIntentForSource(
                        current,
                        inputIntents,
                        index,
                        settings
                    );
            }
        }

        private void ResolveCascade(
            SandIntBuffer inputIntents,
            SandIntBuffer outputIntents,
            SandStepSettings settings,
            bool preserveExistingMoves
        )
        {
            int lineCount = SandAlgorithmCore.GetGravityLineCount(settings);
            for (int line = 0; line < lineCount; line++)
            {
                SandAlgorithmCore.ResolveCascadeLine(
                    current,
                    inputIntents,
                    outputIntents,
                    line,
                    settings,
                    preserveExistingMoves
                );
            }
        }

        private void Commit(
            SandIntBuffer inputIntents,
            SandIntBuffer destination,
            SandStepSettings settings
        )
        {
            for (int index = 0; index < current.Length; index++)
            {
                destination[index] = SandAlgorithmCore.CommitCell(
                    current,
                    inputIntents,
                    index,
                    settings
                );
            }
        }

        private static void Swap(
            ref SandIntBuffer first,
            ref SandIntBuffer second
        )
        {
            SandIntBuffer swap = first;
            first = second;
            second = swap;
        }
    }
}
#endif
