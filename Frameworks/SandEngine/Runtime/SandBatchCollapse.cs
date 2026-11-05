#if !PLA_CREATIVE_BUILD
using PortableArray = System.Array;
using System.Collections.Generic;
using Com.Scheherazade.Common.Frameworks.GridSystem;
using Unity.Collections;
using SandIntBuffer = Unity.Collections.NativeArray<int>;
using Unity.Profiling;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Bounded surface relaxation shared by every backend. Profiles describe only
    /// the first exposed surface along gravity, never hidden voids inside a pile.
    /// Mutations use the backend API to maintain counts and render/GPU versions.
    /// </summary>
    internal sealed class SandBatchCollapse
    {
        internal const int MaxCellReadsPerPass = 65536;
        private const int MaxPairVisitsPerStep = 8192;
        private static readonly ProfilerMarker Marker =
            new ProfilerMarker("SandSimulation.BatchCollapse");

        private readonly List<Surface> _surfaces = new List<Surface>();
        private int _width, _height;
        private GridCoord _gravity;
        private int _minLine, _maxLine, _cursor;
        private int _remainingSteps;
        private int[] _activeEdges = PortableArray.Empty<int>();
        private int _pairParity;
        private int _idleDelay;
        private int _cellReadLimit = MaxCellReadsPerPass;
        private bool _sweepMoved;
        private byte[] _gravityField;
        private byte _baseGravityCode;

        internal bool IsPending => _remainingSteps > 0;
        internal int LastMovedGrains { get; private set; }
        internal int LastCellReads { get; private set; }

        internal void Reset()
        {
            _remainingSteps = 0;
            _pairParity = 0;
            _idleDelay = 0;
            _sweepMoved = false;
            System.Array.Clear(_activeEdges, 0, _activeEdges.Length);
            _surfaces.Clear();
            LastMovedGrains = 0;
            LastCellReads = 0;
        }

        internal void Request(ISandBackend backend, ISandEngineSettings config,
            int minX, int minY, int maxX, int maxY, GridCoord? gravityOverride = null)
        {
            if (!SandSettingsLifetime.IsAvailable(config) || !config.EnableBatchCollapse
                || config.BatchCollapseMaxGrainsPerStep <= 0
                || config.BatchCollapseMaxGrainsPerPair <= 0
                || config.BatchCollapseMaxSteps <= 0) return;

            minX = Mathf.Max(0, minX);
            minY = Mathf.Max(0, minY);
            maxX = Mathf.Min(backend.Width - 1, maxX);
            maxY = Mathf.Min(backend.Height - 1, maxY);
            if (minX > maxX || minY > maxY) return;

            ConfigureLayout(backend, config, gravityOverride);
            int lineCount = _gravity.x == 0 ? _width : _height;
            int radius = Mathf.Clamp(config.BatchCollapseRadius, 0, 128);
            int first = Mathf.Max(0, (_gravity.x == 0 ? minX : minY) - radius);
            int last = (int)System.Math.Min(lineCount - 1L,
                (long)(_gravity.x == 0 ? maxX : maxY) + radius);
            if (first >= last) return;

            if (!IsPending)
            {
                _minLine = first;
                _maxLine = last;
                _cursor = first;
            }
            else
            {
                _minLine = Mathf.Min(_minLine, first);
                _maxLine = Mathf.Max(_maxLine, last);
            }
            // Requests wake the detector immediately, but never replenish this
            // tick's transfer allowance or restart its fair traversal cursor.
            _idleDelay = 0;
            _remainingSteps = Mathf.Clamp(config.BatchCollapseMaxSteps, 0, 120);
        }

        internal void Step(ISandBackend backend, ISandEngineSettings config, byte[] gravityField = null,
            GridCoord? gravityOverride = null, int remainingTransferBudget = int.MaxValue)
        {
            _gravityField = gravityField;
            _baseGravityCode = !SandSettingsLifetime.IsAvailable(config) ? (byte)1 : SandGravityMap.Encode(SandAlgorithmCore.NormalizeGravityDirection(config.GravityDirection));
            LastMovedGrains = 0;
            LastCellReads = 0;
            if (!SandSettingsLifetime.IsAvailable(config) || !config.EnableBatchCollapse
                || config.BatchCollapseMaxGrainsPerStep <= 0
                || config.BatchCollapseMaxSteps <= 0
                || config.BatchCollapseMaxGrainsPerPair <= 0)
            {
                Reset();
                return;
            }

            ConfigureLayout(backend, config, gravityOverride);
            int lineCount = _gravity.x == 0 ? _width : _height;
            if (lineCount < 2) return;
            if (!IsPending)
            {
                if (!config.BatchCollapseAutoDetect) return;
                if (_idleDelay > 0)
                {
                    _idleDelay--;
                    return;
                }
            }
            if (config.BatchCollapseAutoDetect)
            {
                // Automatic monitoring continues around the entire pool, even
                // while a collector is repeatedly disturbing another location.
                _minLine = 0;
                _maxLine = lineCount - 1;
            }

            int transferLimit = Mathf.Clamp(
                Mathf.Min(config.BatchCollapseMaxGrainsPerStep, remainingTransferBudget),
                0,
                8192
            );
            if (transferLimit == 0) return;
            int passLimit = Mathf.Clamp(
                config.BatchCollapsePassesPerStep,
                1,
                8
            );
            _cellReadLimit = MaxCellReadsPerPass * passLimit;

            using (Marker.Auto())
            {
                // Collection has usually already synchronized this view on GPU.
                // Never retain it across Backend.Step(), which can swap buffers.
                SandIntBuffer grid = backend.CurrentGrid;
                int remainingPairVisits = MaxPairVisitsPerStep;
                for (int pass = 0; pass < passLimit; pass++)
                {
                    int previousCursor = _cursor;
                    bool completedTraversal = RelaxSurface(
                        backend,
                        grid,
                        config,
                        transferLimit,
                        ref remainingPairVisits
                    );
                    if (LastMovedGrains >= transferLimit
                        || LastCellReads >= _cellReadLimit
                        || remainingPairVisits <= 0
                        || (!completedTraversal && _cursor == previousCursor)
                        || (completedTraversal && !IsPending
                            && _pairParity == 0))
                    {
                        break;
                    }
                }
            }
        }

        private bool RelaxSurface(
            ISandBackend backend,
            SandIntBuffer grid,
            ISandEngineSettings config,
            int transferLimit,
            ref int remainingPairVisits
        )
        {
            int length = _gravity.x == 0 ? _height : _width;
            int start = Mathf.Clamp(_cursor, _minLine, _maxLine - 1);
            _surfaces.Clear();
            for (int line = start; line <= _maxLine; line++)
            {
                if (!TryReadSurface(grid, line, length, out Surface surface)) break;
                _surfaces.Add(surface);
            }

            int pairs = _surfaces.Count - 1;
            int retainedStep = Mathf.Clamp(
                config.BatchCollapseRetainedStep,
                1,
                128
            );
            int triggerDepth = Mathf.Max(
                retainedStep + 1,
                Mathf.Clamp(config.BatchCollapseMinDepth, 2, 4096)
            );
            int pairLimit = Mathf.Clamp(
                config.BatchCollapseMaxGrainsPerPair,
                0,
                8192
            );
            int quietScans = Mathf.Clamp(
                config.BatchCollapseMaxSteps,
                1,
                120
            );
            int radius = Mathf.Clamp(config.BatchCollapseRadius, 0, 128);
            int nextPair = start;
            for (int pair = 0;
                 pair < pairs && remainingPairVisits > 0;
                 pair++)
            {
                int edge = start + pair;
                nextPair = edge + 1;
                remainingPairVisits--;
                // Disjoint pairs alternate each relaxation pass. A column
                // participates only once per pass, so the pass count explicitly
                // controls how far a collapse can propagate in one tick.
                if ((edge & 1) != _pairParity) continue;
                int allowance = Mathf.Min(
                    pairLimit,
                    transferLimit - LastMovedGrains
                );
                int surfaceGap = _activeEdges[edge] > 0
                    || Mathf.Abs(
                        _surfaces[pair].Depth
                        - _surfaces[pair + 1].Depth
                    ) >= triggerDepth
                        ? 1
                        : triggerDepth;
                // Ordinary sliding can leave falling grains at the mouth
                // of a deep shaft. Land that exposed strand through its clear
                // vertical run first, otherwise it masks the hole forever.
                int moved = SettleExposedGrains(
                    backend,
                    grid,
                    start,
                    pair,
                    length,
                    triggerDepth,
                    surfaceGap,
                    allowance
                );
                moved += SettleExposedGrains(
                    backend,
                    grid,
                    start,
                    pair + 1,
                    length,
                    triggerDepth,
                    surfaceGap,
                    allowance - moved
                );
                int threshold = ResolveTriggerDepth(
                    start,
                    pair,
                    radius,
                    retainedStep,
                    triggerDepth
                );
                moved += Transfer(
                    backend,
                    grid,
                    start,
                    pair,
                    length,
                    threshold,
                    retainedStep,
                    allowance - moved
                );
                if (moved > 0)
                {
                    for (int neighbor = Mathf.Max(0, edge - 1);
                         neighbor <= Mathf.Min(
                             _activeEdges.Length - 1,
                             edge + 1
                         );
                         neighbor++)
                    {
                        _activeEdges[neighbor] = quietScans;
                    }
                    _remainingSteps = quietScans;
                    _sweepMoved = true;
                }
                else if (_activeEdges[edge] > 0)
                {
                    _activeEdges[edge]--;
                }
                LastMovedGrains += moved;
                if (LastMovedGrains >= transferLimit
                    || LastCellReads >= _cellReadLimit)
                {
                    break;
                }
            }

            // Resume after the last visited pair, including when the grain
            // budget interrupted a window. Progress carries to the next pass;
            // only complete quiet traversals count toward sleeping.
            _cursor = nextPair;
            if (_cursor < _maxLine)
            {
                return false;
            }

            _cursor = _minLine;
            _pairParity ^= 1;
            // Both pair parities must be quiet before sleeping. With
            // a one-scan grace period, one idle parity cannot cancel
            // work that is still waiting in the other parity.
            if (_pairParity == 0 && !_sweepMoved && _remainingSteps > 0)
            {
                _remainingSteps--;
                if (_remainingSteps == 0)
                {
                    System.Array.Clear(
                        _activeEdges,
                        0,
                        _activeEdges.Length
                    );
                }
            }
            if (_pairParity == 0)
            {
                _sweepMoved = false;
                if (!IsPending)
                {
                    _idleDelay = Mathf.Clamp(
                        config.BatchCollapseScanInterval,
                        1,
                        120
                    ) - 1;
                }
            }
            return true;
        }

        private void ConfigureLayout(ISandBackend backend, ISandEngineSettings config, GridCoord? gravityOverride = null)
        {
            GridCoord gravity = gravityOverride ?? SandAlgorithmCore.NormalizeGravityDirection(config.GravityDirection);
            if (_width == backend.Width && _height == backend.Height && _gravity == gravity) return;
            Reset();
            _width = backend.Width;
            _height = backend.Height;
            _gravity = gravity;
            int edges = (gravity.x == 0 ? _width : _height) - 1;
            if (_activeEdges.Length != edges) _activeEdges = new int[edges];
            _minLine = _cursor = 0;
            _maxLine = edges;
        }

        private int ResolveTriggerDepth(int start, int pair, int radius, int retainedStep, int triggerDepth)
        {
            if (_activeEdges[start + pair] > 0) return retainedStep + 1;
            int lowerSurface = Mathf.Max(_surfaces[pair].Depth, _surfaces[pair + 1].Depth);
            // A wide deep depression may consist entirely of small adjacent
            // drops. Consider the nearby shoulder's total drop as well, while
            // still transferring only across a clear immediate diagonal.
            for (int offset = Mathf.Max(0, pair - radius);
                 offset <= Mathf.Min(_surfaces.Count - 1, pair + 1 + radius); offset++)
            {
                Surface shoulder = _surfaces[offset];
                if (shoulder.IsSand && lowerSurface - shoulder.Depth >= triggerDepth)
                    return retainedStep + 1;
            }
            return triggerDepth;
        }

        private bool TryReadSurface(SandIntBuffer grid, int line, int length,
            out Surface surface)
        {
            surface = default;
            int depth = 0;
            // Ignore the outside wall rim. Once inside the pool, the next wall
            // terminates the profile, including platforms and enclosed pockets.
            while (depth < length)
            {
                if (LastCellReads >= _cellReadLimit / 2) return false;
                if (!TryRead(grid, Index(line, depth), out int cell)) return false;
                if (SandCell.GetElementType(cell) != ElementType.Wall) break;
                depth++;
            }
            surface.EmptyStart = depth;
            while (depth < length)
            {
                if (LastCellReads >= _cellReadLimit / 2) return false;
                if (!TryRead(grid, Index(line, depth), out int cell)) return false;
                if (cell != 0)
                {
                    surface.IsSand = SandCell.GetElementType(cell) == ElementType.Sand && !SandCell.IsUnsettled(cell);
                    break;
                }
                depth++;
            }
            surface.Depth = depth;
            return true;
        }

        private int Transfer(ISandBackend backend, SandIntBuffer grid,
            int start, int pair, int length, int triggerDepth, int retainedStep, int allowance)
        {
            Surface left = _surfaces[pair];
            Surface right = _surfaces[pair + 1];
            if (Mathf.Abs(left.Depth - right.Depth) < triggerDepth || allowance <= 0) return 0;
            int sourceOffset = left.Depth < right.Depth ? pair : pair + 1;
            int targetOffset = sourceOffset == pair ? pair + 1 : pair;
            Surface source = _surfaces[sourceOffset];
            Surface target = _surfaces[targetOffset];
            int sourceLine = start + sourceOffset;
            int targetLine = start + targetOffset;
            int amount = Mathf.Min(allowance,
                (target.Depth - source.Depth - retainedStep + 1) / 2);
            int moved = 0;
            for (; moved < amount; moved++)
            {
                if (source.Depth >= length || target.Depth <= source.Depth + 1
                    || target.EmptyStart > source.Depth) break;
                int from = Index(sourceLine, source.Depth);
                if (!TryRead(grid, from, out int cell)
                    || SandCell.GetElementType(cell) != ElementType.Sand || SandCell.IsUnsettled(cell)) break;
                // Ordinary gravity handles unsupported columns. A roof-touching
                // shoulder may peel off too: the clear side entry connects its
                // newly exposed vacancy directly to the notch's headspace.
                if (source.Depth + 1 < length)
                {
                    if (!TryRead(grid, Index(sourceLine, source.Depth + 1), out int support)) break;
                    if (support == 0) break;
                }
                // The target profile certifies a continuous empty run from the
                // side entry to the landing. Sequential commits reserve it and
                // update both profiles before another shoulder can use it.
                int to = Index(targetLine, target.Depth - 1);
                if (!TryRead(grid, to, out int destination) || destination != 0) break;
                if (!TryStampFall(grid, from, targetLine, source.Depth + 1, target.Depth - 1, ref cell)) break;
                backend.SetCell(from, 0);
                backend.SetCell(to, cell);
                source.Depth++;
                target.Depth--;
            }
            _surfaces[sourceOffset] = source;
            _surfaces[targetOffset] = target;
            return moved;
        }

        private int SettleExposedGrains(ISandBackend backend, SandIntBuffer grid,
            int start, int offset, int length, int triggerDepth, int surfaceGap, int allowance)
        {
            Surface surface = _surfaces[offset];
            if (allowance <= 0 || !surface.IsSand || surface.Depth + 1 >= length) return 0;
            int line = start + offset;
            int bottom = surface.Depth;
            // Find the end of the exposed sand strand. Never look through a
            // wall or another element to reach an enclosed lower compartment.
            for (int depth = surface.Depth + 1; depth < length; depth++)
            {
                if (!TryRead(grid, Index(line, depth), out int cell)) return 0;
                if (SandCell.GetElementType(cell) != ElementType.Sand || SandCell.IsUnsettled(cell)) break;
                bottom = depth;
            }
            int landing = bottom;
            for (int depth = bottom + 1; depth < length; depth++)
            {
                if (!TryRead(grid, Index(line, depth), out int cell)) return 0;
                if (cell != 0) break;
                landing = depth;
            }
            // A single loose shoulder grain must also close small gaps during
            // an avalanche; otherwise it blocks peeling despite the deep drop
            // next door. Do not extend this to shallow buried pockets.
            if (landing - bottom < (bottom == surface.Depth ? surfaceGap : triggerDepth)) return 0;
            int amount = Mathf.Min(allowance, bottom - surface.Depth + 1);
            int moved = 0;
            for (; moved < amount; moved++)
            {
                int from = Index(line, bottom - moved);
                if (!TryRead(grid, from, out int grain)) break;
                if (!TryStampFall(grid, from, line, bottom - moved + 1, landing - moved, ref grain)) break;
                backend.SetCell(from, 0);
                backend.SetCell(Index(line, landing - moved), grain);
            }
            if (moved == bottom - surface.Depth + 1)
                surface.Depth = landing - moved + 1;
            _surfaces[offset] = surface;
            return moved;
        }

        private bool TryRead(SandIntBuffer grid, int index, out int cell)
        {
            cell = 0;
            if (LastCellReads >= _cellReadLimit) return false;
            LastCellReads++;
            cell = grid[index];
            if (_gravityField != null)
            {
                byte direction = SandGravityMap.Encode(_gravity);
                byte modifier = _gravityField[index];
                if (modifier != 0 && modifier != direction
                    || SandCell.GetElementType(cell) == ElementType.Sand
                        && (modifier != 0 ? modifier : SandCell.ResolveGravityCode(cell, _baseGravityCode)) != direction)
                    cell = (int)ElementType.Wall;
            }
            return true;
        }

        private bool TryStampFall(SandIntBuffer grid, int source, int line, int firstDepth, int lastDepth, ref int cell)
        {
            if (_gravityField == null) return true;
            cell = SandCell.ApplyGravityModifier(cell, _gravityField[source], _baseGravityCode);
            if (SandCell.GetGravityCode(cell) != 0) return true;
            for (int depth = firstDepth; depth <= lastDepth; depth++)
            {
                int index = Index(line, depth);
                if (!TryRead(grid, index, out _)) return false;
                if (_gravityField[index] == 0) continue;
                cell = SandCell.ApplyGravityModifier(cell, _gravityField[index], _baseGravityCode);
                break;
            }
            return true;
        }

        private int Index(int line, int depth)
        {
            int x = _gravity.x == 0 ? line : _gravity.x > 0 ? depth : _width - 1 - depth;
            int y = _gravity.y == 0 ? line : _gravity.y > 0 ? depth : _height - 1 - depth;
            return y * _width + x;
        }

        private struct Surface
        {
            public int EmptyStart;
            public int Depth;
            public bool IsSand;
        }
    }
}
#endif
