namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Deterministic hashing utilities for noise lattice lookups.
    /// Never uses System.Random: results are identical on every platform,
    /// which is required for reproducible texture bakes.
    /// </summary>
    public static class NoiseHash
    {
        /// <summary>Murmur-ish 32-bit finalizer mixing (x, y, seed) into a uniform uint.</summary>
        public static uint Hash2D(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 974634317);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        /// <summary>Murmur-ish 32-bit finalizer mixing (x, seed) into a uniform uint.</summary>
        public static uint Hash1D(int x, int seed)
        {
            uint h = (uint)(x * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        /// <summary>2D lattice hash normalized to 0..1.</summary>
        public static float Hash01(int x, int y, int seed)
        {
            return Hash2D(x, y, seed) / 4294967295f;
        }

        /// <summary>1D lattice hash normalized to 0..1.</summary>
        public static float Hash01(int x, int seed)
        {
            return Hash1D(x, seed) / 4294967295f;
        }

        /// <summary>4D lattice hash normalized to 0..1 (folded 2D hashes for mixing).</summary>
        public static float Hash01(int w, int x, int y, int z, int seed)
        {
            int mixA = (int)Hash2D(w, z, seed);
            int mixB = (int)Hash2D(x, y, seed ^ 0x5BD1E995);
            return Hash2D(mixA, mixB, seed) / 4294967295f;
        }

        /// <summary>
        /// Builds a 256-entry permutation table via seeded Fisher-Yates shuffle.
        /// The caller usually duplicates it to 512 entries for classic Perlin lookups.
        /// </summary>
        public static int[] BuildPermutation(int seed)
        {
            int[] permutation = new int[256];
            for (int i = 0; i < 256; i++)
            {
                permutation[i] = i;
            }

            uint state = (uint)seed * 0x9E3779B1u + 0x6D2B79F5u;
            if (state == 0u)
            {
                state = 0xA5A5A5A5u;
            }

            for (int i = 255; i > 0; i--)
            {
                uint roll = XorShift(ref state);
                int j = (int)(roll % (uint)(i + 1));
                int tmp = permutation[i];
                permutation[i] = permutation[j];
                permutation[j] = tmp;
            }

            return permutation;
        }

        /// <summary>One step of the classic xorshift32 generator.</summary>
        public static uint XorShift(ref uint state)
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }
    }
}
