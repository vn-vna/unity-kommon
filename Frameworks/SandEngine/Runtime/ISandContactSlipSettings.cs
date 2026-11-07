using System;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>Optional support-specific slip probabilities. Existing settings keep legacy friction behavior.</summary>
    public interface ISandContactSlipSettings
    {
        bool UseContactSlip { get; }
        float SandSlipChance { get; }
        float FixedSandSlipChance { get; }
        float WallSlipChance { get; }
        float FrictionlessWallSlipChance { get; }
    }

    /// <summary>Optional bounded surface flow. Zero search distance keeps legacy ledge behavior.</summary>
    public interface ISandSupportFlowSettings
    {
        int MinimumSlideSpeed { get; }
        int SupportSearchDistance { get; }
    }

    /// <summary>Blittable per-step policy; probability thresholds use the solver's existing 0–65536 range.</summary>
    public readonly struct SandContactSlip
    {
        public readonly bool enabled;
        public readonly int sand;
        public readonly int fixedSand;
        public readonly int wall;
        public readonly int frictionlessWall;
        public readonly int minimumSlideSpeed;
        public readonly int supportSearchDistance;

        public SandContactSlip(bool enabled, int sand, int fixedSand, int wall, int frictionlessWall,
            int minimumSlideSpeed = 1, int supportSearchDistance = 0)
        {
            this.enabled = enabled;
            this.sand = Math.Max(0, Math.Min(65536, sand));
            this.fixedSand = Math.Max(0, Math.Min(65536, fixedSand));
            this.wall = Math.Max(0, Math.Min(65536, wall));
            this.frictionlessWall = Math.Max(0, Math.Min(65536, frictionlessWall));
            this.minimumSlideSpeed = Math.Max(1, Math.Min(255, minimumSlideSpeed));
            this.supportSearchDistance = Math.Max(0, Math.Min(32, supportSearchDistance));
        }

        public static SandContactSlip FromSettings(ISandContactSlipSettings settings)
        {
            if (settings == null || !settings.UseContactSlip) return default;
            var flow = settings as ISandSupportFlowSettings;
            return new SandContactSlip(true, Threshold(settings.SandSlipChance), Threshold(settings.FixedSandSlipChance),
                Threshold(settings.WallSlipChance), Threshold(settings.FrictionlessWallSlipChance),
                flow?.MinimumSlideSpeed ?? 1, flow?.SupportSearchDistance ?? 0);
        }

        private static int Threshold(float probability)
        {
            if (float.IsNaN(probability) || float.IsInfinity(probability) || probability < 0 || probability > 1)
                throw new ArgumentOutOfRangeException(nameof(probability), "Slip probability must be finite and in 0–1.");
            return (int)(probability * 65536f);
        }
    }
}
