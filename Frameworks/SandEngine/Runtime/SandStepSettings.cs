namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>Normalized physics values shared by native and PLA kernels.</summary>
    internal readonly struct SandStepSettings
    {
        public readonly int width;
        public readonly int height;
        public readonly bool disableGravity;
        public readonly int constantFallSpeed;
        public readonly int gravityPerStep;
        public readonly int maxFallSpeed;
        public readonly int slideChance;
        public readonly bool preventTemporaryEdgeVacancies;
        public readonly bool enableLateralMixing;
        public readonly int lateralMixChance;
        public readonly int lateralMixPasses;
        public readonly int mixingSeed;
        public readonly int simulationSeed;
        public readonly int gravityX;
        public readonly int gravityY;
        public readonly bool enableVerticalSettle;

        public SandStepSettings(int width, int height, bool disableGravity,
            int constantFallSpeed, int gravityPerStep, int maxFallSpeed, int slideChance,
            bool preventTemporaryEdgeVacancies, bool enableLateralMixing,
            int lateralMixChance, int lateralMixPasses, int mixingSeed,
            int gravityX, int gravityY, int simulationSeed = 0, bool enableVerticalSettle = false)
        {
            this.width = width;
            this.height = height;
            this.disableGravity = disableGravity;
            this.constantFallSpeed = constantFallSpeed;
            this.gravityPerStep = gravityPerStep;
            this.maxFallSpeed = maxFallSpeed;
            this.slideChance = slideChance;
            this.preventTemporaryEdgeVacancies = preventTemporaryEdgeVacancies;
            this.enableLateralMixing = enableLateralMixing;
            this.lateralMixChance = lateralMixChance;
            this.lateralMixPasses = lateralMixPasses;
            this.mixingSeed = mixingSeed;
            this.simulationSeed = simulationSeed;
            this.gravityX = gravityX;
            this.gravityY = gravityY;
            this.enableVerticalSettle = enableVerticalSettle;
        }
    }
}
