using System.Runtime.InteropServices;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Element ids stored in the grid. Empty must stay zero so a zeroed buffer means empty.
    /// </summary>
    public enum ElementType : byte
    {
        Empty = 0,
        Sand = 1,
        Wall = 2,
    }

    /// <summary>
    /// Two-channel render data for one grid cell. The first byte selects the element
    /// rendering path and the second byte directly addresses the 16 x 16 sand table.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SandViewCell
    {
        public byte elementType;
        public byte packedColorVariant;

        public SandViewCell(ElementType elementType, int packedColorVariant)
        {
            this.elementType = (byte)elementType;
            this.packedColorVariant = (byte)Mathf.Clamp(
                packedColorVariant,
                0,
                byte.MaxValue
            );
        }

        public ElementType ElementType => (ElementType)elementType;
    }

    /// <summary>
    /// Bit layout of one 32-bit grid cell. The element type lives in the low byte so a
    /// zeroed int still means an empty cell; the velocity byte and the 16-bit payload
    /// travel with the particle when the cell moves.
    /// </summary>
    public static class SandCell
    {
        public const int TypeShift = 0;
        public const uint TypeMask = 0x000000FFu;

        public const int VelocityShift = 8;
        public const uint VelocityMask = 0x0000FF00u;

        // Walls have no velocity. Bit 8 is their material flag; Sand still uses
        // the complete velocity byte unchanged. Existing wall/color encodings stay valid.
        public const int FrictionlessWallMask = 0x00000100;
        public static bool IsFrictionlessWall(int cell) => GetElementType(cell) == ElementType.Wall
            && (cell & FrictionlessWallMask) != 0;
        public static int PackWall(bool frictionless = false, int payload = 0) =>
            Pack(ElementType.Wall, frictionless ? 1 : 0, payload);

        // A separate wall-only material bit distinguishes uncut artwork from ordinary solids.
        public const int FixedSandWallMask = 0x00000200;
        public static bool IsFixedSandWall(int cell) => GetElementType(cell) == ElementType.Wall
            && (cell & FixedSandWallMask) != 0;
        public static int PackFixedSandWall(int payload) => Pack(ElementType.Wall, 2, payload);

        // Bit 31 remembers a bounded supported sideways slide while it crosses sand
        // support. Clear it on the next gravity-axis move; color is unaffected.
        public const int FrictionlessSlideMask = unchecked((int)0x80000000);
        public static bool IsFrictionlessSliding(int cell) => GetElementType(cell) == ElementType.Sand
            && (cell & FrictionlessSlideMask) != 0;
        public static int WithFrictionlessSliding(int cell, bool sliding) => sliding
            ? cell | FrictionlessSlideMask : cell & ~FrictionlessSlideMask;

        public const int PayloadShift = 16;
        public const uint PayloadMask = 0xFFFF0000u;

        public const int ColorBits = 4;
        public const int VariantMask = (1 << ColorBits) - 1;

        /// <summary>Bits 16-23 hold the 8-bit palette address: 4-bit color | 4-bit variant.</summary>
        public const uint ColorByteMask = 0x00FF0000u;
        // Zero preserves the settled eligibility of existing authored buffers.
        public const int UnsettledMask = 0x01000000;
        public static bool IsUnsettled(int cell) => (cell & UnsettledMask) != 0;
        public static int WithUnsettled(int cell, bool unsettled) => unsettled
            ? cell | UnsettledMask : cell & ~UnsettledMask;

        // Bits 26-27 encode multiplier minus one; zero keeps existing grains at 1x.
        // Bit 25 belongs to temporary wall barriers.
        public const int FallSpeedMultiplierShift = 26;
        public const int FallSpeedMultiplierMask = 0x0C000000;
        public const int MaximumFallSpeedMultiplier = 4;

        // Bits 28-30 carry a grain's last gravity modifier. Zero means the grain
        // has never been modified and still follows the pool's base gravity.
        public const int GravityShift = 28;
        public const int GravityMask = 0x70000000;
        public static byte GetGravityCode(int cell)
        {
            int code = (cell & GravityMask) >> GravityShift;
            return (byte)(code <= 4 ? code : 0);
        }
        public static bool HasPersistentGravity(int cell) => GetElementType(cell) == ElementType.Sand && GetGravityCode(cell) != 0;
        public static int WithGravityCode(int cell, byte code) =>
            (cell & ~GravityMask) | (Mathf.Clamp(code, 0, 4) << GravityShift);
        public static byte ResolveGravityCode(int cell, byte baseCode)
        {
            byte stored = GetGravityCode(cell);
            return stored == 0 ? baseCode : stored;
        }
        public static int ApplyGravityModifier(int cell, byte modifier, byte baseCode)
        {
            if (modifier == 0 || GetElementType(cell) != ElementType.Sand) return cell;
            if (ResolveGravityCode(cell, baseCode) != modifier) cell &= ~(int)VelocityMask;
            return WithGravityCode(cell, modifier);
        }

        public static int GetFallSpeedMultiplier(int cell) =>
            1 + ((cell & FallSpeedMultiplierMask) >> FallSpeedMultiplierShift);
        public static int WithFallSpeedMultiplier(int cell, int multiplier) =>
            (cell & ~FallSpeedMultiplierMask)
            | ((Mathf.Clamp(multiplier, 1, MaximumFallSpeedMultiplier) - 1) << FallSpeedMultiplierShift);

        /// <summary>Packs a cell value. <paramref name="velocity"/> and <paramref name="payload"/> are clamped to their bit widths.</summary>
        public static int Pack(ElementType type, int velocity, int payload)
        {
            velocity = Mathf.Clamp(velocity, 0, 255);
            payload = Mathf.Clamp(payload, 0, 0xFFFF);
            return (int)type | (velocity << VelocityShift) | (payload << PayloadShift);
        }

        /// <summary>Reads the 8-bit color/variant byte from the payload.</summary>
        public static int GetColorByte(int cell) => (cell >> PayloadShift) & 0xFF;

        public static int GetColorIndex(int cell) => GetColorByte(cell) >> ColorBits;

        public static int GetVariant(int cell) => GetColorByte(cell) & VariantMask;

        public static int PackColorVariant(int colorIndex, int variant)
        {
            int clampedColor = Mathf.Clamp(colorIndex, 0, VariantMask);
            int clampedVariant = Mathf.Clamp(variant, 0, VariantMask);
            return (clampedColor << ColorBits) | clampedVariant;
        }

        /// <summary>Replaces the render byte while preserving the cell type, velocity and remaining payload.</summary>
        public static int WithColorByte(int cell, int colorByte)
        {
            int packedColorByte = Mathf.Clamp(colorByte, 0, byte.MaxValue)
                                  << PayloadShift;
            return (cell & ~(int)ColorByteMask) | packedColorByte;
        }

        public static ElementType GetElementType(int cell) => (ElementType)(cell & (int)TypeMask);

        public static int GetVelocity(int cell) => (cell & (int)VelocityMask) >> VelocityShift;

        public static int GetPayload(int cell) => (cell >> PayloadShift) & 0xFFFF;
    }

}
