using Com.Scheherazade.Common.Frameworks.GridSystem;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Borrowed, live settings for an engine. Implementations need not be Unity
    /// assets. The engine reads these at the same operation boundaries as its
    /// physics/collapse/collection work; it does not clone or own this object.
    /// Scheduled backends receive a StepParams value snapshot, not this interface.
    /// </summary>
    public interface ISandEngineSettings
    {
        bool EnableGravity { get; }
        int ConstantFallSpeed { get; }
        int GravityPerStep { get; }
        int MaxFallSpeed { get; }
        float SlideFriction { get; }
        GridCoord GravityDirection { get; }
        int SettleBatchSize { get; }
        bool PreventTemporaryEdgeVacancies { get; }
        bool EnableLateralMixing { get; }
        float LateralMixChance { get; }
        int LateralMixPasses { get; }
        bool OverrideCollectionFallMixing { get; }
        float CollectionFallMixChance { get; }
        int CollectionFallMixRadius { get; }
        int CollectionFallMixSamples { get; }
        int MixingSeed { get; }
        int UpdateBatchSize { get; }
        bool EnableBatchCollapse { get; }
        bool BatchCollapseAutoDetect { get; }
        int BatchCollapseScanInterval { get; }
        int BatchCollapseMinDepth { get; }
        int BatchCollapseRetainedStep { get; }
        int BatchCollapseMaxGrainsPerStep { get; }
        int BatchCollapseMaxGrainsPerPair { get; }
        int BatchCollapsePassesPerStep { get; }
        int BatchCollapseMaxSteps { get; }
        int BatchCollapseRadius { get; }
        int ColorIndex { get; }

        /// <summary>
        /// Resolves the current brush/seed range for a logical color without
        /// consuming randomness. The engine clamps the inclusive bounds to 0-15.
        /// No palette asset or rendering/authoring contract crosses this seam.
        /// </summary>
        void GetRandomVariantRange(int logicalColor, out int minimum, out int maximum);
    }

    /// <summary>Preserves Unity asset null semantics through neutral interfaces.</summary>
    internal static class SandSettingsLifetime
    {
        internal static bool IsAvailable(object settings) => settings != null
            && (!(settings is UnityEngine.Object asset) || asset != null);
    }
}
