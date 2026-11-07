#if !PLA_CREATIVE_BUILD
using PortableMath = System.Math;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using SandIntBuffer = Unity.Collections.NativeArray<int>;
using SandByteBuffer = Unity.Collections.NativeArray<byte>;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    internal sealed class RegionalSandState : IDisposable
    {
        private byte[] _source;
        private NativeArray<byte> _field;

        internal NativeArray<byte> Synchronize(byte[] source, int cellCount)
        {
            if (source != null && source.Length != cellCount)
                throw new ArgumentException("Regional gravity must cover the complete simulation grid.");
            if (SandBufferLifetime.IsCreated(_field) && _field.Length == cellCount && ReferenceEquals(source, _source)) return _field;
            if (!SandBufferLifetime.IsCreated(_field) || _field.Length != cellCount)
            {
                if (SandBufferLifetime.IsCreated(_field)) _field.Dispose();
                _field = new NativeArray<byte>(cellCount, Allocator.Persistent);
            }
            if (source != null) _field.CopyFrom(source);
            else for (int i = 0; i < cellCount; i++) _field[i] = 0;
            _source = source;
            return _field;
        }

        public void Dispose()
        {
            if (SandBufferLifetime.IsCreated(_field)) _field.Dispose();
            _source = null;
        }
    }

    /// <summary>
    /// Conservative mixed-gravity solver. Dependencies stay within same-direction
    /// segments (so they cannot form cycles); direct falls can cross into empty
    /// cells of any region. Every destination has exactly one owner and each grain
    /// commits once. The original solver remains in use for undecorated pools.
    /// </summary>
    [BurstCompile]
    internal struct RegionalSandStepJob : IJob
    {
        public SandIntBuffer current;
        public SandIntBuffer next;
        public SandIntBuffer intents;
        public SandIntBuffer claims;
        [ReadOnly] public SandByteBuffer gravity;
        public SandStepSettings settings;

        public void Execute()
        {
            for (int i = 0; i < current.Length; i++) current[i] = Stamp(i, current[i]);
            if (settings.enableVerticalSettle)
            {
                for (byte code = 1; code <= 4; code++) Compact(code);
            }
            for (int i = 0; i < current.Length; i++)
            {
                intents[i] = 0;
                claims[i] = 0;
                next[i] = 0;
            }
            for (int source = 0; source < current.Length; source++)
            {
                SandStepSettings local = LocalSettings(DirectionAt(source));
                if (!SandAlgorithmCore.TryResolveTarget(current, source, local, out int target)) continue;
                target = StopAtDirectionChange(source, target, local);
                int incumbent = claims[target] - 1;
                if (incumbent >= 0 && !Outranks(source, incumbent, target, local)) continue;
                if (incumbent >= 0) intents[incumbent] = 0;
                claims[target] = source + 1;
                intents[source] = target + 1;
            }

            int passes = settings.enableLateralMixing ? settings.lateralMixPasses : 0;
            for (int pass = 0; pass < passes; pass++)
                for (byte code = 1; code <= 4; code++) ResolveDependencies(code, true, pass);
            if (settings.enableVerticalSettle)
                for (byte code = 1; code <= 4; code++) ResolveDependencies(code, false, 0);

            for (int source = 0; source < current.Length; source++)
            {
                int cell = current[source];
                if (cell == 0) continue;
                int target = intents[source] - 1;
                if (target < 0)
                {
                    next[source] = (cell & 255) == 1 ? cell & ~0xFF00 : cell;
                    continue;
                }
                SandStepSettings local = LocalSettings(DirectionAt(source));
                int distance = (target % settings.width - source % settings.width) * local.gravityX
                    + (target / settings.width - source / settings.width) * local.gravityY;
                bool lateralSlide = distance == 0;
                if (lateralSlide) distance = Math.Abs(target % settings.width - source % settings.width)
                    + Math.Abs(target / settings.width - source / settings.width);
                int moved = SandCell.WithFrictionlessSliding((cell & ~0xFF00) | (Math.Min(255, distance) << 8), lateralSlide);
                next[target] = Stamp(target, moved);
            }
            // Only genuine physical support latches Pipe/Wall airborne grains.
            // A region edge itself is never support.
            int persistentCount = 0;
            for (int index = 0; index < next.Length; index++)
            {
                int cell = next[index];
                if (SandCell.HasPersistentGravity(cell)) persistentCount++;
                if ((cell & 255) != 1 || !SandCell.IsUnsettled(cell)) continue;
                byte direction = DirectionForCell(index, cell);
                SandStepSettings local = LocalSettings(direction);
                int support = Neighbor(index, local.gravityX, local.gravityY);
                if (support < 0 || (current[support] & 255) == 2
                    || (current[support] & 255) == 1 && intents[support] == 0
                        && !SandCell.IsUnsettled(current[support]) && DirectionAt(support) == direction)
                    next[index] = SandCell.WithUnsettled(cell, false);
            }
            claims[0] = persistentCount;
        }

        private void Compact(byte code)
        {
            SandStepSettings local = LocalSettings(code);
            int lines = local.gravityX == 0 ? settings.width : settings.height;
            int length = local.gravityX == 0 ? settings.height : settings.width;
            for (int line = 0; line < lines; line++)
            {
                int write = 0;
                byte previousModifier = 255;
                for (int depth = 0; depth < length; depth++)
                {
                    int index = LineIndex(line, depth, local);
                    int cell = current[index];
                    if (gravity[index] != previousModifier) write = depth;
                    previousModifier = gravity[index];
                    if (!Compatible(index, cell, code) || (cell & 255) == 2 || SandCell.IsUnsettled(cell))
                    {
                        write = depth + 1;
                        continue;
                    }
                    if ((cell & 255) != 1) continue;
                    int target = LineIndex(line, write++, local);
                    if (target == index) continue;
                    current[target] = Stamp(target, cell & ~0xFF00);
                    current[index] = 0;
                }
            }
        }

        private void ResolveDependencies(byte code, bool mixing, int pass)
        {
            SandStepSettings local = LocalSettings(code);
            int lines = local.gravityX == 0 ? settings.width : settings.height;
            int length = local.gravityX == 0 ? settings.height : settings.width;
            for (int depth = 0; depth < length; depth++)
            for (int line = 0; line < lines; line++)
            {
                int source = LineIndex(line, depth, local);
                if (DirectionAt(source) != code || (current[source] & 255) != 1
                    || SandCell.IsUnsettled(current[source]) || intents[source] != 0) continue;
                int target = Neighbor(source, local.gravityX, local.gravityY);
                if (mixing)
                {
                    uint roll = Hash(source, pass);
                    if ((roll & 65535u) < (uint)settings.lateralMixChance)
                    {
                        int side = (roll & 65536u) == 0 ? -1 : 1;
                        int diagonal = Neighbor(source, local.gravityX + side * local.gravityY, local.gravityY + side * local.gravityX);
                        if (CanFollow(diagonal, code) && SandAlgorithmCore.IsSafeLateralDependencySource(current, source, side, local)) target = diagonal;
                        else
                        {
                            diagonal = Neighbor(source, local.gravityX - side * local.gravityY, local.gravityY - side * local.gravityX);
                            if (CanFollow(diagonal, code) && SandAlgorithmCore.IsSafeLateralDependencySource(current, source, -side, local)) target = diagonal;
                        }
                    }
                }
                if (!CanFollow(target, code)) continue;
                intents[source] = target + 1;
                claims[target] = source + 1;
            }
        }

        private bool CanFollow(int target, byte code) => target >= 0 && DirectionAt(target) == code
            && (current[target] & 255) == 1 && intents[target] != 0 && claims[target] == 0;

        private bool Outranks(int source, int incumbent, int target, SandStepSettings local)
        {
            if (DirectionAt(source) == DirectionAt(incumbent)) return SandAlgorithmCore.Outranks(source, incumbent, target, local);
            uint candidate = Hash(source, target);
            uint previous = Hash(incumbent, target);
            return candidate < previous || candidate == previous && source < incumbent;
        }

        private int StopAtDirectionChange(int source, int target, SandStepSettings local)
        {
            int sideways = (target % settings.width - source % settings.width) * local.gravityY
                + (target / settings.width - source / settings.width) * local.gravityX;
            int side = PortableMath.Sign(sideways);
            int lateralSteps = Math.Abs(sideways);
            bool lateralOnly = (target % settings.width - source % settings.width) * local.gravityX
                + (target / settings.width - source / settings.width) * local.gravityY == 0;
            int index = source;
            byte direction = DirectionAt(source);
            bool hasMemory = SandCell.GetGravityCode(current[source]) != 0;
            while (index != target)
            {
                index = Neighbor(index, (lateralOnly ? 0 : local.gravityX) + (lateralSteps > 0 ? side * local.gravityY : 0),
                    (lateralOnly ? 0 : local.gravityY) + (lateralSteps > 0 ? side * local.gravityX : 0));
                if (gravity[index] != 0 && (gravity[index] != direction || !hasMemory)) return index;
                lateralSteps--;
            }
            return target;
        }

        private byte BaseDirection => (byte)(settings.gravityX > 0 ? 3 : settings.gravityX < 0 ? 4 : settings.gravityY < 0 ? 2 : 1);
        private byte DirectionAt(int index) => DirectionForCell(index, current[index]);
        private byte DirectionForCell(int index, int cell) => gravity[index] != 0
            ? gravity[index] : SandCell.ResolveGravityCode(cell, BaseDirection);
        private int Stamp(int index, int cell) => SandCell.ApplyGravityModifier(cell, gravity[index], BaseDirection);
        private bool Compatible(int index, int cell, byte direction) =>
            (gravity[index] == 0 || gravity[index] == direction)
            && ((cell & 255) != 1 || DirectionForCell(index, cell) == direction);

        private int Neighbor(int index, int dx, int dy)
        {
            int x = index % settings.width + dx;
            int y = index / settings.width + dy;
            return x < 0 || x >= settings.width || y < 0 || y >= settings.height ? -1 : y * settings.width + x;
        }

        private int LineIndex(int line, int depth, SandStepSettings local)
        {
            int x = local.gravityX == 0 ? line : local.gravityX > 0 ? settings.width - 1 - depth : depth;
            int y = local.gravityY == 0 ? line : local.gravityY > 0 ? settings.height - 1 - depth : depth;
            return y * settings.width + x;
        }

        private uint Hash(int source, int salt)
        {
            uint value = (uint)source * 374761393u ^ (uint)salt * 668265263u
                ^ (uint)(settings.mixingSeed ^ settings.simulationSeed) * 2654435761u;
            value = (value ^ (value >> 13)) * 1274126177u;
            return value ^ (value >> 16);
        }

        private SandStepSettings LocalSettings(byte code)
        {
            int gx = code == 3 ? 1 : code == 4 ? -1 : 0;
            int gy = code == 1 ? 1 : code == 2 ? -1 : 0;
            return new SandStepSettings(settings.width, settings.height, settings.disableGravity,
                settings.constantFallSpeed, settings.gravityPerStep, settings.maxFallSpeed, settings.slideChance,
                settings.preventTemporaryEdgeVacancies, settings.enableLateralMixing, settings.lateralMixChance,
                settings.lateralMixPasses, settings.mixingSeed, gx, gy, settings.simulationSeed, settings.enableVerticalSettle,
                settings.contactSlip);
        }
    }
}
#endif
