using System.Collections.Generic;
using Com.Scheherazade.Common.Frameworks.GridSystem;
#if PLA_CREATIVE_BUILD
using SandAlgorithmCore = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaAlgorithmCore;
using SandBatchCollapse = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBatchCollapse;
using SandViewBuffer = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBuffer<Com.Scheherazade.Common.Frameworks.SandEngine.SandViewCell>;
#else
using Unity.Collections;
using SandIntBuffer = Unity.Collections.NativeArray<int>;
using SandViewBuffer = Unity.Collections.NativeArray<Com.Scheherazade.Common.Frameworks.SandEngine.SandViewCell>;
#endif
#if !PLA_CREATIVE_BUILD
using Unity.Profiling;
#endif
#if PLA_CREATIVE_BUILD && UNITY_LUNA
using RectInt = Com.Scheherazade.Common.Compatibility.CreativeRectInt;
#endif
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public readonly struct SandCollectionRegion
    {
        public int MinX { get; }
        public int MinY { get; }
        public int MaxX { get; }
        public int MaxY { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float CellWidth { get; }
        public float CellHeight { get; }
        private readonly float _diagonalRange;
        private readonly float _diagonalMarginX;
        private readonly float _diagonalMarginY;

        public SandCollectionRegion(int minX, int minY, int maxX, int maxY)
            : this(
                minX,
                minY,
                maxX,
                maxY,
                (minX + maxX + 1) * 0.5f,
                (minY + maxY + 1) * 0.5f,
                1f,
                1f,
                1f,
                0f,
                0f
            )
        { }

        private SandCollectionRegion(
            int minX,
            int minY,
            int maxX,
            int maxY,
            float centerX,
            float centerY,
            float cellWidth,
            float cellHeight,
            float diagonalRange,
            float diagonalMarginX,
            float diagonalMarginY
        )
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            CenterX = centerX;
            CenterY = centerY;
            CellWidth = Mathf.Max(0.001f, cellWidth);
            CellHeight = Mathf.Max(0.001f, cellHeight);
            _diagonalRange = float.IsNaN(diagonalRange)
                ? 1f
                : Mathf.Clamp01(diagonalRange);
            _diagonalMarginX = Mathf.Max(0f, diagonalMarginX);
            _diagonalMarginY = Mathf.Max(0f, diagonalMarginY);
        }

        public static SandCollectionRegion FromExpandedCell(
            float centerX,
            float centerY,
            float cellWidth,
            float cellHeight,
            int marginPixels,
            float marginCellUnits = 0f,
            bool cardinalOnly = false
        )
        {
            return FromExpandedCell(
                centerX,
                centerY,
                cellWidth,
                cellHeight,
                marginPixels,
                marginCellUnits,
                cardinalOnly ? 0f : 1f
            );
        }

        public static SandCollectionRegion FromExpandedCell(
            float centerX,
            float centerY,
            float cellWidth,
            float cellHeight,
            int marginPixels,
            float marginCellUnits,
            float diagonalRange
        )
        {
            float margin = Mathf.Max(0, marginPixels);
            float cells = float.IsNaN(marginCellUnits) || float.IsInfinity(marginCellUnits)
                ? 0f : Mathf.Max(0f, marginCellUnits);
            float marginX = margin + Mathf.Ceil(cells * cellWidth);
            float marginY = margin + Mathf.Ceil(cells * cellHeight);
            float halfCellWidth = cellWidth * 0.5f;
            float halfCellHeight = cellHeight * 0.5f;
            return new SandCollectionRegion(
                ClampPixelBound(Mathf.Ceil(
                    centerX - halfCellWidth - marginX - 0.5f)
                ),
                ClampPixelBound(Mathf.Ceil(
                    centerY - halfCellHeight - marginY - 0.5f)
                ),
                ClampPixelBound(Mathf.Floor(
                    centerX + halfCellWidth + marginX - 0.5f)
                ),
                ClampPixelBound(Mathf.Floor(
                    centerY + halfCellHeight + marginY - 0.5f)
                ),
                centerX,
                centerY,
                cellWidth,
                cellHeight,
                diagonalRange,
                marginX,
                marginY
            );
        }

        public bool Contains(int x, int y)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
            if (_diagonalRange >= 1f) return true;

            float outsideX = Mathf.Abs(x + 0.5f - CenterX)
                - CellWidth * 0.5f;
            float outsideY = Mathf.Abs(y + 0.5f - CenterY)
                - CellHeight * 0.5f;
            // Keep the footprint and its horizontal/vertical strips at their full
            // reach. In a corner, scale the extra reach on both axes so 0 is the
            // legacy cardinal cross and 1 is the full expanded rectangle.
            return outsideX <= 0f
                || outsideY <= 0f
                || (outsideX <= _diagonalMarginX * _diagonalRange
                    && outsideY <= _diagonalMarginY * _diagonalRange);
        }

        public bool TryGetHorizontalSpan(
            int y,
            out int spanMinX,
            out int spanMaxX
        )
        {
            spanMinX = 0;
            spanMaxX = -1;
            if (y < MinY || y > MaxY)
            {
                return false;
            }

            if (_diagonalRange >= 1f)
            {
                spanMinX = MinX;
                spanMaxX = MaxX;
                return true;
            }

            float outsideY = Mathf.Abs(y + 0.5f - CenterY)
                - CellHeight * 0.5f;
            if (float.IsNaN(outsideY))
            {
                return false;
            }

            if (outsideY <= 0f)
            {
                spanMinX = MinX;
                spanMaxX = MaxX;
                return true;
            }

            float allowedOutsideX = outsideY
                <= _diagonalMarginY * _diagonalRange
                    ? _diagonalMarginX * _diagonalRange
                    : 0f;
            float halfCellWidth = CellWidth * 0.5f;
            spanMinX = Mathf.Max(
                MinX,
                ClampPixelBound(Mathf.Ceil(
                    CenterX - halfCellWidth - allowedOutsideX - 0.5f
                ))
            );
            spanMaxX = Mathf.Min(
                MaxX,
                ClampPixelBound(Mathf.Floor(
                    CenterX + halfCellWidth + allowedOutsideX - 0.5f
                ))
            );
            return spanMinX <= spanMaxX;
        }

        private static int ClampPixelBound(float value)
        {
            if (value <= int.MinValue) return int.MinValue;
            if (value >= int.MaxValue) return int.MaxValue;
            return (int)value;
        }

        public float DistanceSquaredToFootprint(int x, int y)
        {
            float outsideX = Mathf.Max(
                0f,
                Mathf.Abs(x + 0.5f - CenterX) - CellWidth * 0.5f
            );
            float outsideY = Mathf.Max(
                0f,
                Mathf.Abs(y + 0.5f - CenterY) - CellHeight * 0.5f
            );
            float normalizedX = outsideX / CellWidth;
            float normalizedY = outsideY / CellHeight;
            return normalizedX * normalizedX + normalizedY * normalizedY;
        }
    }

    /// <summary>
    /// Pure C# orchestrator between the backend (compute) and the simulator
    /// (configuration). No MonoBehaviour, no Unity lifecycle. The renderer owns
    /// this object; input/HUD call its public API.
    /// </summary>
    public partial class SandEngine
    {
        private const uint InitialVariantVariationState = 0xA341316Cu;
#if !PLA_CREATIVE_BUILD
        protected static readonly ProfilerMarker CollectionScanMarker =
            new ProfilerMarker("SandCollection.CandidateScan");
        private static readonly ProfilerMarker CollectionRepairMarker =
            new ProfilerMarker("SandCollection.VacancyRepair");
#endif

        public ISandBackend Backend { get; }
        /// <summary>Borrowed live settings; backend ticks receive value snapshots.</summary>
        public ISandEngineSettings Settings { get; }

        protected readonly List<CollectionCandidate> _nearestCollectionCandidates =
            new List<CollectionCandidate>();
        private readonly List<CollectionRowSpan> _collectionRowSpans =
            new List<CollectionRowSpan>();
        private readonly List<int> _collectionRepairVacancies = new List<int>();
        private readonly CollectionVacancyComparer _collectionVacancyComparer =
            new CollectionVacancyComparer();
        private GridCoord _collectionGravityDirection;
        private bool _repairCollectionVacancies;
        private uint _collectionMixSequence;
        private readonly SandBatchCollapse _batchCollapse = new SandBatchCollapse();
        private SandGravityMap _gravityMap;
        private SandBatchCollapse[] _regionalCollapses;
        private readonly List<int> _regionalVacancies = new();
        private byte[] _repairGravityField;
        private bool _repairDirectionalGravity;
        private readonly List<byte> _collectionVacancyDirections = new();
        private byte[] _emptyGravityField;
        private int _regionalCollapseMoved;
        private int _regionalCollapseReads;
        private int _regionalCollapseCursor;

        public bool HasRegionalGravity => _gravityMap != null && _gravityMap.Count > 0;
        public bool HasDirectionalGravity => HasRegionalGravity || Backend is IRegionalSandBackend regional && regional.HasPersistentGravity;
        internal byte[] GravityField => _gravityMap?.GetCells();
        public int GravityVisualRevision => _gravityMap?.Revision ?? 0;
        public bool CopyGravityVisualOccluders(string ownerId, List<RectInt> destination)
        {
            destination.Clear();
            return _gravityMap != null && _gravityMap.CopyOccluders(ownerId, destination);
        }
        private bool UsesMixedGravity => HasDirectionalGravity;

        public SandGravityRegion RegisterGravityRegion(string ownerId, RectInt bounds, GridCoord direction, int priority = 0)
        {
            if (!(Backend is IRegionalSandBackend)) throw new System.NotSupportedException("This sand backend does not support regional gravity.");
            CompleteStep();
            _gravityMap ??= new SandGravityMap(this);
            return _gravityMap.Add(ownerId, bounds, direction, priority);
        }

        /// <summary>Effective cardinal gravity at an engine-local Y-down cell.</summary>
        public GridCoord GetGravityDirection(int index)
        {
            if (index < 0 || index >= Backend.CellCount) throw new System.ArgumentOutOfRangeException(nameof(index));
            int cell = Backend.GetCell(index);
            byte[] field = GravityField;
            return ResolveGravityDirection(index, cell, field, SandAlgorithmCore.NormalizeGravityDirection(Settings.GravityDirection));
        }

        private static GridCoord ResolveGravityDirection(int index, int cell, byte[] field, GridCoord baseDirection)
        {
            byte code = field != null && field[index] != 0 ? field[index]
                : SandCell.ResolveGravityCode(cell, SandGravityMap.Encode(baseDirection));
            return SandGravityMap.Decode(code);
        }

        public bool IsBatchCollapsePending => UsesMixedGravity
            ? _regionalCollapses != null && System.Array.Exists(_regionalCollapses, collapse => collapse.IsPending)
            : _batchCollapse.IsPending;
        public int LastBatchCollapseMovedGrains => UsesMixedGravity ? _regionalCollapseMoved : _batchCollapse.LastMovedGrains;
        public int LastBatchCollapseCellReads => UsesMixedGravity ? _regionalCollapseReads : _batchCollapse.LastCellReads;

        /// <summary>
        /// Queues a bounded surface collapse near changed grid cells. Collection
        /// and erase stamps call this automatically. Other grid editors can call
        /// it after removing support; moving a view alone needs no notification.
        /// </summary>
        public void RequestBatchCollapse(int minX, int minY, int maxX, int maxY)
        {
            if (UsesMixedGravity)
            {
                EnsureRegionalCollapses();
                for (byte code = 1; code <= 4; code++)
                    _regionalCollapses[code - 1].Request(Backend, Settings, minX, minY, maxX, maxY, SandGravityMap.Decode(code));
                return;
            }
            _batchCollapse.Request(Backend, Settings, minX, minY, maxX, maxY);
        }

        private void EnsureRegionalCollapses()
        {
            _regionalCollapses ??= new[] { new SandBatchCollapse(), new SandBatchCollapse(), new SandBatchCollapse(), new SandBatchCollapse() };
        }

        private void StepBatchCollapse(byte[] field, GridCoord direction)
        {
            if (field == null)
            {
                _batchCollapse.Step(Backend, Settings, gravityOverride: direction);
                return;
            }
            _regionalCollapseMoved = 0;
            _regionalCollapseReads = 0;
            if (!Settings.EnableBatchCollapse) return;
            EnsureRegionalCollapses();
            for (int offset = 0; offset < 4; offset++)
            {
                int index = (_regionalCollapseCursor + offset) % 4;
                SandBatchCollapse collapse = _regionalCollapses[index];
                collapse.Step(Backend, Settings, field, SandGravityMap.Decode((byte)(index + 1)),
                    Mathf.Max(0, Mathf.Clamp(Settings.BatchCollapseMaxGrainsPerStep, 0, 8192) - _regionalCollapseMoved));
                _regionalCollapseMoved += collapse.LastMovedGrains;
                _regionalCollapseReads += collapse.LastCellReads;
            }
            _regionalCollapseCursor = (_regionalCollapseCursor + 1) % 4;
        }

        public SandEngine(ISandBackend backend, ISandEngineSettings config)
        {
            Backend = backend;
            Settings = config;
        }

        public int Width => Backend.Width;
        public int Height => Backend.Height;
        public SandViewBuffer ViewBuffer => Backend.ViewBuffer;
        public int Particles => Backend.ParticleCount;
        public double TickMs => Backend.TickMs;

        /// <summary>Runs one simulation tick using the current config values.</summary>
        public void Step()
        {
            ScheduleStep();
            CompleteStep();
        }

        /// <summary>
        /// Prepares pool-local collapse and starts a tick. Backends without the
        /// scheduling capability run synchronously. Complete before observers run.
        /// </summary>
        public void ScheduleStep()
        {
            CompleteStep();
            // Run before the normal tick so it resolves the batch's new surface,
            // and GPU collection can reuse its already synchronized CPU view.
            byte[] field = GravityField;
            GridCoord direction = SandAlgorithmCore.NormalizeGravityDirection(Settings.GravityDirection);
            if (field == null && HasDirectionalGravity)
            {
                if (_emptyGravityField == null || _emptyGravityField.Length != Backend.CellCount)
                    _emptyGravityField = new byte[Backend.CellCount];
                field = _emptyGravityField;
            }
            StepBatchCollapse(field, direction);
            StepParams parameters = new StepParams
            {
                GravityField = new SandGravityFieldSnapshot(field),
                DisableGravity = !Settings.EnableGravity,
                ConstantFallSpeed = Settings.ConstantFallSpeed,
                GravityPerStep = Settings.GravityPerStep,
                MaxFallSpeed = Settings.MaxFallSpeed,
                SlideChance = (int)((1f - Settings.SlideFriction) * 65536f),
                EnableVerticalSettle = true,
                SettleBatchSize = Settings.SettleBatchSize,
                PreventTemporaryEdgeVacancies =
                    Settings.PreventTemporaryEdgeVacancies,
                EnableLateralMixing = Settings.EnableLateralMixing,
                LateralMixChance = Mathf.RoundToInt(
                    Mathf.Clamp01(Settings.LateralMixChance) * 65536f
                ),
                LateralMixPasses = Settings.LateralMixPasses,
                MixingSeed = Settings.MixingSeed,
                UpdateBatchSize = Settings.UpdateBatchSize,
                GravityDirection = direction
            };
            if (Backend is IScheduledSandBackend scheduled)
            {
                scheduled.ScheduleStep(parameters);
            }
            else
            {
                Backend.Step(parameters);
            }
        }

        public void CompleteStep()
        {
            if (Backend is IScheduledSandBackend scheduled)
            {
                scheduled.CompleteStep();
            }
        }

        /// <summary>
        /// Seeds the grid from packed cell data (row-major, index = y * Width + x).
        /// Extra cells beyond the grid are ignored.
        /// </summary>
        public void WriteCells(int[] cells)
        {
            ResetBatchCollapse();
            int count = Mathf.Min(cells != null ? cells.Length : 0, Backend.CellCount);
            for (int i = 0; i < count; i++)
            {
                Backend.SetCell(i, cells[i]);
            }
        }

        /// <summary>
        /// Seeds packed cells while replacing each sand grain's visual variant with a
        /// deterministic value from the configured range. Color indices and all
        /// non-render cell data are preserved.
        /// </summary>
        public void WriteCellsWithVariantVariation(int[] cells)
        {
            ResetBatchCollapse();
            int count = Mathf.Min(
                cells != null ? cells.Length : 0,
                Backend.CellCount
            );
            uint randomState = CreateVariantVariationState(count);

            for (int index = 0; index < count; index++)
            {
                int cell = cells[index];
                if (SandCell.GetElementType(cell) == ElementType.Sand)
                {
                    GetRandomVariantRange(
                        SandCell.GetColorIndex(cell),
                        out int variantMin,
                        out int variantMax
                    );
                    int variant = NextVariant(
                        ref randomState,
                        variantMin,
                        variantMax - variantMin + 1
                    );
                    int colorByte =
                        (SandCell.GetColorByte(cell) & ~SandCell.VariantMask)
                        | variant;
                    cell = SandCell.WithColorByte(cell, colorByte);
                }

                Backend.SetCell(index, cell);
            }
        }

        [System.Obsolete("Use WriteCellsWithVariantVariation.")]
        public void WriteCellsWithLuminanceVariation(int[] cells)
        {
            WriteCellsWithVariantVariation(cells);
        }

        /// <summary>
        /// Removes sand grains whose 4-bit color index matches
        /// <paramref name="matchColorIndex"/> within a disc of
        /// <paramref name="radiusCells"/> around (centerX, centerY). Out-of-bounds
        /// cells are skipped; collection stops once <paramref name="maxCount"/>
        /// grains have been removed. Returns the number of grains collected.
        /// </summary>
        public int CollectSand(
            int centerX,
            int centerY,
            int radiusCells,
            int matchColorIndex,
            int maxCount = int.MaxValue
        )
        {
            return CollectSand(
                centerX,
                centerY,
                radiusCells,
                matchColorIndex,
                maxCount,
                null
            );
        }

        public int CollectSand(
            int centerX,
            int centerY,
            int radiusCells,
            int matchColorIndex,
            int maxCount,
            SandRemovalSampleBuffer sampleBuffer
        )
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            BeginCollectionVacancyRepair();
            int collected = 0;
            int radiusSquared = radiusCells * radiusCells;

            for (int dy = -radiusCells; dy <= radiusCells; dy++)
            {
                int y = centerY + dy;
                if (y < 0 || y >= Backend.Height)
                {
                    continue;
                }

                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                {
                    if (dx * dx + dy * dy > radiusSquared)
                    {
                        continue;
                    }

                    int x = centerX + dx;
                    if (x < 0 || x >= Backend.Width)
                    {
                        continue;
                    }

                    int index = y * Backend.Width + x;
                    int cell = Backend.GetCell(index);
                    if (SandCell.GetElementType(cell) != ElementType.Sand)
                    {
                        continue;
                    }

                    if ((SandCell.GetColorByte(cell) >> 4) != matchColorIndex)
                    {
                        continue;
                    }

                    sampleBuffer?.Consider(index, cell);
                    Backend.SetCell(index, 0);
                    TrackCollectionVacancy(index, cell);
                    collected++;
                    if (collected >= maxCount)
                    {
                        return CompleteCollectionVacancyRepair(collected);
                    }
                }
            }

            return CompleteCollectionVacancyRepair(collected);
        }

        public int CollectSandInRegions(
            IReadOnlyList<SandCollectionRegion> regions,
            int matchColorIndex,
            int maxCount,
            ref int scanCursor
        )
        {
            return CollectSandInRegions(
                regions,
                matchColorIndex,
                maxCount,
                ref scanCursor,
                null
            );
        }

        public int CollectSandInRegions(
            IReadOnlyList<SandCollectionRegion> regions,
            int matchColorIndex,
            int maxCount,
            ref int scanCursor,
            SandRemovalSampleBuffer sampleBuffer
        )
        {
            if (regions == null || regions.Count == 0 || maxCount <= 0)
            {
                return 0;
            }

            if (!TryGetRegionBounds(
                    regions,
                    out int minX,
                    out int minY,
                    out int maxX,
                    out int maxY
                ))
            {
                return 0;
            }

            BeginCollectionVacancyRepair();
            int scanWidth = maxX - minX + 1;
            int scanHeight = maxY - minY + 1;
            int candidateCount = scanWidth * scanHeight;
            int startOffset = PositiveModulo(scanCursor, candidateCount);
            int collected = 0;

            for (int scanned = 0; scanned < candidateCount; scanned++)
            {
                int offset = GetWrappedOffset(
                    startOffset,
                    scanned,
                    candidateCount
                );
                int x = minX + offset % scanWidth;
                int y = minY + offset / scanWidth;
                if (!IsInsideAnyRegion(regions, x, y))
                {
                    continue;
                }

                int index = y * Backend.Width + x;
                int cell = Backend.GetCell(index);
                if (SandCell.GetElementType(cell) != ElementType.Sand
                    || SandCell.GetColorIndex(cell) != matchColorIndex)
                {
                    continue;
                }

                sampleBuffer?.Consider(index, cell);
                Backend.SetCell(index, 0);
                TrackCollectionVacancy(index, cell);
                collected++;
                if (collected >= maxCount)
                {
                    scanCursor = (offset + 1) % candidateCount;
                    return CompleteCollectionVacancyRepair(collected);
                }
            }

            scanCursor = (startOffset + 1) % candidateCount;
            return CompleteCollectionVacancyRepair(collected);
        }

        /// <summary>
        /// Removes matching grains bottom-first along gravity, then nearest to an
        /// occupied cell footprint at equal depth. This is used only by rate-limited
        /// collectors so unlimited collection can retain its allocation-free scan
        /// path.
        /// </summary>
        public int CollectSandInRegionsNearestFirst(
            IReadOnlyList<SandCollectionRegion> regions,
            int matchColorIndex,
            int maxCount
        )
        {
            return CollectSandInRegionsNearestFirst(
                regions,
                matchColorIndex,
                maxCount,
                null
            );
        }

        public int CollectSandInRegionsNearestFirst(
            IReadOnlyList<SandCollectionRegion> regions,
            int matchColorIndex,
            int maxCount,
            SandRemovalSampleBuffer sampleBuffer
        )
        {
            uint unusedState = 0;
            return CollectSandInRegionsByPriority(regions, matchColorIndex, maxCount,
                false, 0f, ref unusedState, sampleBuffer);
        }

        public int CollectSandInRegionsRandomized(
            IReadOnlyList<SandCollectionRegion> regions,
            int matchColorIndex,
            int maxCount,
            float nearBias,
            ref uint randomState,
            SandRemovalSampleBuffer sampleBuffer = null
        )
        {
            nearBias = float.IsNaN(nearBias) || float.IsInfinity(nearBias)
                ? 0f : Mathf.Clamp(nearBias, 0f, 32f);
            return CollectSandInRegionsByPriority(regions, matchColorIndex, maxCount,
                true, nearBias, ref randomState, sampleBuffer);
        }

        private int CollectSandInRegionsByPriority(
            IReadOnlyList<SandCollectionRegion> regions, int matchColorIndex,
            int maxCount, bool randomized, float nearBias, ref uint randomState,
            SandRemovalSampleBuffer sampleBuffer
        )
        {
            if (regions == null || regions.Count == 0 || maxCount <= 0)
            {
                return 0;
            }

            if (!TryGetRegionBounds(
                    regions,
                    out int minX,
                    out int minY,
                    out int maxX,
                    out int maxY
                ))
            {
                return 0;
            }

            bool collectEntireBounds = maxCount >= (long)(maxX - minX + 1) * (maxY - minY + 1);
            _nearestCollectionCandidates.Clear();
#if PLA_CREATIVE_BUILD
            int[] currentGrid = Backend.CurrentGrid;
#else
            SandIntBuffer currentGrid = Backend.CurrentGrid;
#endif
            int backendWidth = Backend.Width;
            int backendHeight = Backend.Height;
            GridCoord gravityDirection =
                SandAlgorithmCore.NormalizeGravityDirection(
                    Settings.GravityDirection
                );
            byte[] gravityField = GravityField;
            bool hasSingleRegion = regions.Count == 1;
            SandCollectionRegion singleRegion = hasSingleRegion
                ? regions[0]
                : default;
#if !PLA_CREATIVE_BUILD
            using (CollectionScanMarker.Auto())
#endif
            {
                for (int y = minY; y <= maxY; y++)
                {
                    int rowSpanCount = BuildCollectionRowSpans(
                        regions,
                        y,
                        minX,
                        maxX
                    );
                    for (int spanIndex = 0;
                         spanIndex < rowSpanCount;
                         spanIndex++)
                    {
                        CollectionRowSpan span =
                            _collectionRowSpans[spanIndex];
                        for (int x = span.MinX; x <= span.MaxX; x++)
                        {
                            int index = y * backendWidth + x;
                            int cell = currentGrid[index];
                            if (SandCell.GetElementType(cell) != ElementType.Sand
                                || SandCell.GetColorIndex(cell) != matchColorIndex)
                            {
                                continue;
                            }

                            float distanceSquared;
                            if (hasSingleRegion)
                            {
                                distanceSquared = singleRegion
                                    .DistanceSquaredToFootprint(x, y);
                            }
                            else if (!TryGetNearestRegionDistanceSquared(
                                         regions,
                                         x,
                                         y,
                                         out distanceSquared
                                     ))
                            {
                                continue;
                            }

                            // No sampling is needed if the allowance covers every
                            // cell. Keep unlimited collection linear rather than
                            // building a heap.
                            if (collectEntireBounds)
                            {
                                _nearestCollectionCandidates.Add(
                                    new CollectionCandidate(
                                        index,
                                        cell,
                                        0,
                                        0d,
                                        0d
                                    )
                                );
                                continue;
                            }

                            RetainCollectionCandidate(
                                new CollectionCandidate(
                                    index,
                                    cell,
                                    GetGravityDepth(
                                        index,
                                        backendWidth,
                                        backendHeight,
                                        ResolveGravityDirection(index, cell, gravityField, gravityDirection)
                                    ),
                                    distanceSquared,
                                    randomized
                                        ? NextCollectionPriority(
                                            distanceSquared,
                                            nearBias,
                                            ref randomState
                                        )
                                        : 0d
                                ),
                                maxCount
                            );
                        }
                    }
                }
            }

            if (_nearestCollectionCandidates.Count == 0)
            {
                return 0;
            }

            int collected = _nearestCollectionCandidates.Count;
            // Preserve selection order for transfer samples; only sort retained grains.
            if (!collectEntireBounds) _nearestCollectionCandidates.Sort();

            BeginCollectionVacancyRepair();
            for (int index = 0; index < collected; index++)
            {
                CollectionCandidate candidate =
                    _nearestCollectionCandidates[index];
                sampleBuffer?.Consider(candidate.Index, candidate.PackedCell);
                Backend.SetCell(candidate.Index, 0);
                TrackCollectionVacancy(candidate.Index, candidate.PackedCell);
            }

            return CompleteCollectionVacancyRepair(collected);
        }

        // Pool ordering uses the weighted value directly. Region selection compares
        // strict footprint distance first and uses this value only to spread exact
        // ties across a contacting edge instead of falling back to scan order.
        public static double NextCollectionPriority(double distanceSquared, float nearBias,
            ref uint randomState)
        {
            randomState += 0x9E3779B9u;
            uint bits = randomState;
            bits = (bits ^ (bits >> 16)) * 0x21F0AAADu;
            bits = (bits ^ (bits >> 15)) * 0x735A2D97u;
            bits ^= bits >> 15;
            double uniform = (bits + 1d) / (uint.MaxValue + 2d);
            return -System.Math.Log(uniform) * (nearBias > 0f
                ? 1d + nearBias * distanceSquared : 1d);
        }

        private static int GetGravityDepth(
            int index,
            int width,
            int height,
            GridCoord gravityDirection
        )
        {
            int x = index % width;
            int y = index / width;
            if (gravityDirection.x == 0)
            {
                return gravityDirection.y > 0 ? y : height - 1 - y;
            }

            return gravityDirection.x > 0 ? x : width - 1 - x;
        }

        // Max heap retains only the K best candidates: O(N log K), O(K) storage.
        // Scan each grid position once so overlapping regions cannot duplicate it.
        protected void RetainCollectionCandidate(CollectionCandidate candidate, int limit)
        {
            var heap = _nearestCollectionCandidates;
            if (heap.Count < limit)
            {
                int child = heap.Count;
                heap.Add(candidate);
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (heap[parent].CompareTo(candidate) >= 0) break;
                    heap[child] = heap[parent];
                    child = parent;
                }
                heap[child] = candidate;
                return;
            }

            if (candidate.CompareTo(heap[0]) >= 0) return;
            int root = 0;
            while (root < heap.Count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < heap.Count && heap[child + 1].CompareTo(heap[child]) > 0)
                    child++;
                if (candidate.CompareTo(heap[child]) >= 0) break;
                heap[root] = heap[child];
                root = child;
            }
            heap[root] = candidate;
        }

        private void BeginCollectionVacancyRepair()
        {
            CompleteStep();
            _repairGravityField = GravityField;
            _repairDirectionalGravity = HasDirectionalGravity;
            _collectionVacancyDirections.Clear();
            _collectionRepairVacancies.Clear();
            _repairCollectionVacancies = SandSettingsLifetime.IsAvailable(Settings);
            if (!_repairCollectionVacancies)
            {
                return;
            }

            _collectionGravityDirection =
                SandAlgorithmCore.NormalizeGravityDirection(
                    Settings.GravityDirection
                );
            _collectionVacancyComparer.Configure(
                Backend.Width,
                Backend.Height,
                _collectionGravityDirection
            );
        }

        private void TrackCollectionVacancy(int index, int removedCell)
        {
            int x = index % Backend.Width;
            int y = index / Backend.Width;
            RequestBatchCollapse(x, y, x, y);
            if (!_repairCollectionVacancies)
            {
                return;
            }

            _collectionRepairVacancies.Add(index);
            if (_repairDirectionalGravity)
                _collectionVacancyDirections.Add(SandGravityMap.Encode(ResolveGravityDirection(index, removedCell,
                    _repairGravityField, SandAlgorithmCore.NormalizeGravityDirection(Settings.GravityDirection))));
        }

        private int CompleteCollectionVacancyRepair(int collected)
        {
            if (_repairCollectionVacancies && collected > 0)
            {
#if !PLA_CREATIVE_BUILD
                using (CollectionRepairMarker.Auto())
#endif
                {
                    if (!_repairDirectionalGravity)
                    {
                        RepairCollectionLines(_collectionRepairVacancies);
                    }
                    else
                    {
                        for (byte code = 1; code <= 4; code++)
                        {
                            _regionalVacancies.Clear();
                            for (int i = 0; i < _collectionRepairVacancies.Count; i++)
                                if (_collectionVacancyDirections[i] == code) _regionalVacancies.Add(_collectionRepairVacancies[i]);
                            _collectionGravityDirection = SandGravityMap.Decode(code);
                            _collectionVacancyComparer.Configure(Width, Height, _collectionGravityDirection);
                            RepairCollectionLines(_regionalVacancies);
                        }
                    }
                }
                unchecked { _collectionMixSequence++; }
            }

            _collectionRepairVacancies.Clear();
            _repairCollectionVacancies = false;
            return collected;
        }

        private void RepairCollectionLines(List<int> vacancies)
        {
            vacancies.Sort(_collectionVacancyComparer);
            int firstVacancy = 0;
            while (firstVacancy < vacancies.Count)
            {
                int line = _collectionVacancyComparer.GetLine(
                    vacancies[firstVacancy]
                );
                int endVacancy = firstVacancy + 1;
                while (endVacancy < vacancies.Count
                    && _collectionVacancyComparer.GetLine(
                        vacancies[endVacancy]
                    ) == line)
                {
                    endVacancy++;
                }

                RepairCollectionLine(line, firstVacancy, endVacancy, vacancies);
                firstVacancy = endVacancy;
            }
        }

        private void RepairCollectionLine(
            int line,
            int firstVacancy,
            int endVacancy,
            List<int> vacancies
        )
        {
            int lineLength = _collectionGravityDirection.x == 0
                ? Backend.Height
                : Backend.Width;
            int segmentStart = 0;
            int downstreamCollectedDepth = -1;
            int vacancyCursor = firstVacancy;
            for (int depth = 0; depth <= lineLength; depth++)
            {
                bool reachedEnd = depth == lineLength;
                int index = reachedEnd ? -1 : GetCollectionLineIndex(line, depth);
                bool reachedWall = !reachedEnd
                    && (SandCell.GetElementType(Backend.GetCell(index)) == ElementType.Wall
                        || _repairDirectionalGravity && !IsCollectionGravityCompatible(index));
                if (!reachedEnd && !reachedWall)
                {
                    if (vacancyCursor < endVacancy
                        && _collectionVacancyComparer.GetDepth(
                            vacancies[vacancyCursor]
                        ) == depth)
                    {
                        downstreamCollectedDepth = depth;
                        vacancyCursor++;
                    }

                    continue;
                }

                if (downstreamCollectedDepth >= 0)
                {
                    CompactCollectionSegment(
                        line,
                        segmentStart,
                        downstreamCollectedDepth
                    );
                }

                segmentStart = depth + 1;
                downstreamCollectedDepth = -1;
            }
        }

        private void CompactCollectionSegment(
            int line,
            int startDepth,
            int endDepth
        )
        {
            int writeDepth = endDepth;
            for (int readDepth = endDepth; readDepth >= startDepth; readDepth--)
            {
                int readIndex = GetCollectionLineIndex(line, readDepth);
                int cell = StampCollectionCell(readIndex, Backend.GetCell(readIndex));
                if (SandCell.IsUnsettled(cell)) { writeDepth = readDepth - 1; continue; }
                if (SandCell.GetElementType(cell) != ElementType.Sand)
                {
                    continue;
                }

                int writeIndex = GetCollectionLineIndex(line, writeDepth);
                if (writeIndex != readIndex)
                {
                    int mixingPasses = Mathf.Min(
                        writeDepth - readDepth,
                        Settings.OverrideCollectionFallMixing
                            ? Mathf.Clamp(Settings.CollectionFallMixSamples, 1, 8)
                            : Mathf.Clamp(Settings.LateralMixPasses, 1, 3)
                    );
                    for (int pass = 0; pass < mixingPasses; pass++)
                    {
                        // Sample successive parts of the fall, including landing.
                        // Bound the work even when collection opens a deep shaft.
                        int landingDepth = readDepth + (int)(
                            (long)(writeDepth - readDepth) * (pass + 1)
                            / mixingPasses
                        );
                        cell = MixCollectionFall(line, readIndex, landingDepth,
                            writeDepth - readDepth, cell);
                    }
                    Backend.SetCell(writeIndex, StampCollectionMove(readIndex, writeIndex, cell));
                    Backend.SetCell(readIndex, 0);
                }

                writeDepth--;
            }
        }

        private int MixCollectionFall(int line, int readIndex, int writeDepth,
            int fallDistance, int cell)
        {
            bool useOverride = Settings.OverrideCollectionFallMixing;
            float mixChance = useOverride
                ? Settings.CollectionFallMixChance : Settings.LateralMixChance;
            if ((!useOverride && !Settings.EnableLateralMixing)
                || float.IsNaN(mixChance) || mixChance <= 0f)
            {
                return cell;
            }

            uint random;
            unchecked
            {
                random = (uint)readIndex * 374761393u
                    ^ (uint)writeDepth * 668265263u
                    ^ (uint)Settings.MixingSeed * 2654435761u
                    ^ _collectionMixSequence * 2246822519u;
                random ^= random >> 13;
                random *= 1274126177u;
                random ^= random >> 16;
            }

            int chance = Mathf.RoundToInt(Mathf.Clamp01(mixChance) * 65536f);
            if ((random & 0xFFFFu) >= (uint)chance) return cell;

            int lineCount = _collectionGravityDirection.x == 0 ? Width : Height;
            int firstSide = (random & 0x10000u) == 0 ? -1 : 1;
            int radius = useOverride
                ? Mathf.Min(fallDistance, Mathf.Clamp(Settings.CollectionFallMixRadius, 1, 8))
                : 1;
            int reach = 1 + (int)((random >> 17) % (uint)radius);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int side = attempt == 0 ? firstSide : -firstSide;
                int available = 0;
                for (int distance = 1; distance <= reach; distance++)
                {
                    int neighborLine = line + side * distance;
                    if (neighborLine < 0 || neighborLine >= lineCount) break;
                    int neighborIndex = GetCollectionLineIndex(neighborLine, writeDepth - 1);
                    if (_repairDirectionalGravity && !IsCollectionGravityCompatible(neighborIndex)) break;
                    if (SandCell.GetElementType(Backend.GetCell(neighborIndex))
                        != ElementType.Sand) break;
                    available = distance;
                }
                if (available == 0) continue;

                // Rotate the contiguous row toward the fall: the falling grain
                // replaces the outermost grain, each neighbor shifts inward, and
                // the innermost joins the fall. No occupancy changes, upstream
                // movement, or crossing of walls/empty pockets. Radius 1 retains
                // the exact legacy exchange and random sequence.
                for (int distance = available; distance >= 1; distance--)
                {
                    int neighborIndex = GetCollectionLineIndex(
                        line + side * distance, writeDepth - 1);
                    int neighborCell = Backend.GetCell(neighborIndex);
                    if (SandCell.IsUnsettled(neighborCell)) continue;
                    Backend.SetCell(neighborIndex, StampCollectionCell(neighborIndex, cell));
                    cell = StampCollectionCell(neighborIndex, neighborCell);
                }
                return cell;
            }

            return cell;
        }

        private int GetCollectionLineIndex(int line, int depth)
        {
            if (_collectionGravityDirection.x == 0)
            {
                int y = _collectionGravityDirection.y > 0
                    ? depth
                    : Backend.Height - 1 - depth;
                return y * Backend.Width + line;
            }

            int x = _collectionGravityDirection.x > 0
                ? depth
                : Backend.Width - 1 - depth;
            return line * Backend.Width + x;
        }

        /// <summary>
        /// Writes a circular stamp into the grid. Sand grains get the configured
        /// palette color and a random visual variant within the configured range.
        /// </summary>
        public void DrawStamp(int centerX, int centerY, int radius, ElementType element)
        {
            GetRandomVariantRange(Settings.ColorIndex, out int variantMin, out int variantMax);
            int radiusSquared = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = centerY + dy;
                if (y < 0 || y >= Backend.Height)
                {
                    continue;
                }

                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radiusSquared)
                    {
                        continue;
                    }

                    int x = centerX + dx;
                    if (x < 0 || x >= Backend.Width)
                    {
                        continue;
                    }

                    int colorByte = element == ElementType.Sand
                        ? SandCell.PackColorVariant(
                            Settings.ColorIndex,
                            Random.Range(variantMin, variantMax + 1)
                        )
                        : 0;
                    int index = y * Backend.Width + x;
                    bool removedSupport = element == ElementType.Empty && Backend.GetCell(index) != 0;
                    int stamped = SandCell.Pack(element, 0, colorByte);
                    Backend.SetCell(index, element == ElementType.Sand ? SandCell.WithUnsettled(stamped, true) : stamped);
                    if (removedSupport) RequestBatchCollapse(x, y, x, y);
                }
            }
        }

        public void Clear()
        {
            ResetBatchCollapse();
            Backend.ClearGrid();
        }

        /// <summary>Clears the grid and re-seeds the demo content (sand rain + wall platform).</summary>
        public void ReseedDemo()
        {
            ResetBatchCollapse();
            Backend.ClearGrid();
            int wallCell = SandCell.Pack(ElementType.Wall, 0, 0);
            GetRandomVariantRange(Settings.ColorIndex, out int variantMin, out int variantMax);

            for (int x = Backend.Width / 4; x < Backend.Width * 3 / 4; x++)
            {
                for (int dy = 0; dy < 60; dy++)
                {
                    int y = dy;
                    int colorByte = SandCell.PackColorVariant(
                        Settings.ColorIndex,
                        Random.Range(variantMin, variantMax + 1)
                    );
                    Backend.SetCell(y * Backend.Width + x, SandCell.Pack(ElementType.Sand, 0, colorByte));
                }
            }

            for (int x = Backend.Width / 4; x < Backend.Width * 3 / 4; x++)
            {
                for (int dy = 0; dy < 6; dy++)
                {
                    int y = (int)(Backend.Height * 0.7f) + dy;
                    Backend.SetCell(y * Backend.Width + x, wallCell);
                }
            }
        }

        public void Resize(int newWidth, int newHeight)
        {
            if (HasRegionalGravity) throw new System.InvalidOperationException("Remove gravity regions before resizing their pool.");
            ResetBatchCollapse();
            Backend.Resize(newWidth, newHeight);
        }

        private bool IsCollectionGravityCompatible(int index)
        {
            byte code = SandGravityMap.Encode(_collectionGravityDirection);
            if (_repairGravityField != null && _repairGravityField[index] != 0 && _repairGravityField[index] != code) return false;
            int cell = Backend.GetCell(index);
            return SandCell.GetElementType(cell) != ElementType.Sand
                || ResolveGravityDirection(index, cell, _repairGravityField,
                    SandAlgorithmCore.NormalizeGravityDirection(Settings.GravityDirection)) == _collectionGravityDirection;
        }

        private int StampCollectionCell(int index, int cell) => _repairGravityField == null ? cell
            : SandCell.ApplyGravityModifier(cell, _repairGravityField[index], SandGravityMap.Encode(SandAlgorithmCore.NormalizeGravityDirection(Settings.GravityDirection)));

        private int StampCollectionMove(int source, int destination, int cell)
        {
            cell = StampCollectionCell(source, cell);
            if (_repairGravityField != null && SandCell.GetGravityCode(cell) == 0)
            {
                int stride = _collectionGravityDirection.y * Width + _collectionGravityDirection.x;
                for (int index = source; index != destination; index += stride)
                {
                    if (_repairGravityField[index] == 0) continue;
                    cell = StampCollectionCell(index, cell);
                    break;
                }
            }
            return StampCollectionCell(destination, cell);
        }

        private void ResetBatchCollapse()
        {
            _batchCollapse.Reset();
            if (_regionalCollapses != null)
                foreach (SandBatchCollapse collapse in _regionalCollapses) collapse.Reset();
            _regionalCollapseMoved = 0;
            _regionalCollapseReads = 0;
        }

        /// <summary>Refreshes the cached particle count (throttled by the caller).</summary>
        public int CountParticles()
        {
            return Backend.CountParticles();
        }

        /// <summary>Counts only live sand cells in the current grid.</summary>
        public int CountSand()
        {
            return Backend.CountSand();
        }

        /// <summary>Emits only into an empty cell; backend tracks view dirtiness.</summary>
        public bool TryEmitSand(int index, int colorByte, int fallSpeedMultiplier = 1)
        {
            if (index < 0 || index >= Backend.CellCount
                || SandCell.GetElementType(Backend.GetCell(index)) != ElementType.Empty) return false;
            int cell = SandCell.WithUnsettled(SandCell.Pack(ElementType.Sand, 0, colorByte), true);
            Backend.SetCell(index, SandCell.WithFallSpeedMultiplier(cell, fallSpeedMultiplier));
            return true;
        }

        private int BuildCollectionRowSpans(
            IReadOnlyList<SandCollectionRegion> regions,
            int y,
            int clipMinX,
            int clipMaxX
        )
        {
            _collectionRowSpans.Clear();
            for (int index = 0; index < regions.Count; index++)
            {
                if (!regions[index].TryGetHorizontalSpan(
                        y,
                        out int spanMinX,
                        out int spanMaxX
                    ))
                {
                    continue;
                }

                spanMinX = Mathf.Max(spanMinX, clipMinX);
                spanMaxX = Mathf.Min(spanMaxX, clipMaxX);
                if (spanMinX <= spanMaxX)
                {
                    _collectionRowSpans.Add(
                        new CollectionRowSpan(spanMinX, spanMaxX)
                    );
                }
            }

            int spanCount = _collectionRowSpans.Count;
            if (spanCount <= 1)
            {
                return spanCount;
            }

            _collectionRowSpans.Sort();
            int mergedCount = 1;
            CollectionRowSpan merged = _collectionRowSpans[0];
            for (int readIndex = 1; readIndex < spanCount; readIndex++)
            {
                CollectionRowSpan next = _collectionRowSpans[readIndex];
                if (next.MinX <= merged.MaxX + 1)
                {
                    merged = new CollectionRowSpan(
                        merged.MinX,
                        Mathf.Max(merged.MaxX, next.MaxX)
                    );
                    _collectionRowSpans[mergedCount - 1] = merged;
                    continue;
                }

                merged = next;
                _collectionRowSpans[mergedCount++] = merged;
            }

            return mergedCount;
        }

        private bool TryGetRegionBounds(
            IReadOnlyList<SandCollectionRegion> regions,
            out int minX,
            out int minY,
            out int maxX,
            out int maxY
        )
        {
            minX = Backend.Width;
            minY = Backend.Height;
            maxX = -1;
            maxY = -1;

            for (int index = 0; index < regions.Count; index++)
            {
                SandCollectionRegion region = regions[index];
                if (region.MaxX < 0
                    || region.MaxY < 0
                    || region.MinX >= Backend.Width
                    || region.MinY >= Backend.Height)
                {
                    continue;
                }

                minX = Mathf.Min(minX, Mathf.Max(0, region.MinX));
                minY = Mathf.Min(minY, Mathf.Max(0, region.MinY));
                maxX = Mathf.Max(
                    maxX,
                    Mathf.Min(Backend.Width - 1, region.MaxX)
                );
                maxY = Mathf.Max(
                    maxY,
                    Mathf.Min(Backend.Height - 1, region.MaxY)
                );
            }

            return minX <= maxX && minY <= maxY;
        }

        private static bool IsInsideAnyRegion(
            IReadOnlyList<SandCollectionRegion> regions,
            int x,
            int y
        )
        {
            for (int index = 0; index < regions.Count; index++)
            {
                if (regions[index].Contains(x, y))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetNearestRegionDistanceSquared(
            IReadOnlyList<SandCollectionRegion> regions,
            int x,
            int y,
            out float nearestDistanceSquared
        )
        {
            if (regions.Count == 1)
            {
                SandCollectionRegion onlyRegion = regions[0];
                if (!onlyRegion.Contains(x, y))
                {
                    nearestDistanceSquared = 0f;
                    return false;
                }

                nearestDistanceSquared =
                    onlyRegion.DistanceSquaredToFootprint(x, y);
                return true;
            }

            nearestDistanceSquared = 0f;
            bool foundRegion = false;
            for (int index = 0; index < regions.Count; index++)
            {
                SandCollectionRegion region = regions[index];
                if (!region.Contains(x, y))
                {
                    continue;
                }

                float distanceSquared = region.DistanceSquaredToFootprint(x, y);
                if (foundRegion && distanceSquared >= nearestDistanceSquared)
                {
                    continue;
                }

                nearestDistanceSquared = distanceSquared;
                foundRegion = true;
            }

            return foundRegion;
        }

        private static uint CreateVariantVariationState(int cellCount)
        {
            uint randomState = InitialVariantVariationState ^ (uint)cellCount;
            return randomState != 0
                ? randomState
                : InitialVariantVariationState;
        }

        private void GetRandomVariantRange(
            int logicalColor,
            out int minimum,
            out int maximum
        )
        {
            Settings.GetRandomVariantRange(logicalColor, out minimum, out maximum);
            minimum = Mathf.Clamp(minimum, 0, SandCell.VariantMask);
            maximum = Mathf.Clamp(maximum, minimum, SandCell.VariantMask);
        }

        private static int NextVariant(
            ref uint randomState,
            int variantMin,
            int variantCount
        )
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return variantMin + (int)(randomState % (uint)variantCount);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }

        private static int GetWrappedOffset(
            int startOffset,
            int scanned,
            int candidateCount
        )
        {
            int remainingUntilWrap = candidateCount - startOffset;
            return scanned < remainingUntilWrap
                ? startOffset + scanned
                : scanned - remainingUntilWrap;
        }

        private readonly struct CollectionRowSpan :
            System.IComparable<CollectionRowSpan>
        {
            public int MinX { get; }
            public int MaxX { get; }

            public CollectionRowSpan(int minX, int maxX)
            {
                MinX = minX;
                MaxX = maxX;
            }

            public int CompareTo(CollectionRowSpan other)
            {
                int minimumComparison = MinX.CompareTo(other.MinX);
                return minimumComparison != 0
                    ? minimumComparison
                    : MaxX.CompareTo(other.MaxX);
            }
        }

        protected readonly struct CollectionCandidate :
            System.IComparable<CollectionCandidate>
        {
            public int Index { get; }
            public int PackedCell { get; }
            public double Distance => _distanceSquared;

            private readonly int _gravityDepth;
            private readonly double _distanceSquared;
            private readonly double _tiePriority;

            public CollectionCandidate(
                int index,
                int packedCell,
                int gravityDepth,
                double distanceSquared,
                double tiePriority
            )
            {
                Index = index;
                PackedCell = packedCell;
                _gravityDepth = gravityDepth;
                _distanceSquared = distanceSquared;
                _tiePriority = tiePriority;
            }

            public int CompareTo(CollectionCandidate other)
            {
                int depthComparison = other._gravityDepth.CompareTo(
                    _gravityDepth
                );
                if (depthComparison != 0)
                {
                    return depthComparison;
                }

                int distanceComparison = _distanceSquared.CompareTo(
                    other._distanceSquared
                );
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                int tieComparison = _tiePriority.CompareTo(
                    other._tiePriority
                );
                return tieComparison != 0
                    ? tieComparison
                    : Index.CompareTo(other.Index);
            }
        }

        private sealed class CollectionVacancyComparer : IComparer<int>
        {
            private int _width;
            private int _height;
            private GridCoord _gravityDirection;

            public void Configure(
                int width,
                int height,
                GridCoord gravityDirection
            )
            {
                _width = width;
                _height = height;
                _gravityDirection = gravityDirection;
            }

            public int Compare(int first, int second)
            {
                int lineComparison = GetLine(first).CompareTo(GetLine(second));
                return lineComparison != 0
                    ? lineComparison
                    : GetDepth(first).CompareTo(GetDepth(second));
            }

            public int GetLine(int index)
            {
                return _gravityDirection.x == 0 ? index % _width : index / _width;
            }

            public int GetDepth(int index)
            {
                return GetGravityDepth(
                    index,
                    _width,
                    _height,
                    _gravityDirection
                );
            }
        }
    }
}
