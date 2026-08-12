using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>Shared math helpers for the noise modules.</summary>
    public static class NoiseBakerUtility
    {
        /// <summary>FloorToInt that never throws for NaN inputs.</summary>
        public static int FloorToInt(float value)
        {
            int result = Mathf.FloorToInt(value);
            return result;
        }

        /// <summary>Wraps a lattice index into [0, period) with negative support.</summary>
        public static int WrapIndex(int index, int period)
        {
            index %= period;
            return index < 0 ? index + period : index;
        }

        /// <summary>
        /// Lattice wrap period for a tileable noise of the given frequency.
        /// Frequencies are expected to be integers (enforced by Sanitize).
        /// </summary>
        public static int PeriodFor(float frequency)
        {
            return Mathf.Max(1, Mathf.RoundToInt(frequency));
        }
    }
}
