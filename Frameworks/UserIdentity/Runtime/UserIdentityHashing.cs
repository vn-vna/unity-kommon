using System.Text;

namespace Com.Hapiga.Scheherazade.Common.UserIdentity
{
    /// <summary>
    /// Stable, deterministic hashing helpers for identity values.
    /// </summary>
    public static class UserIdentityHashing
    {
        private const ulong FnvOffsetBasis64 = 14695981039346656037UL;
        private const ulong FnvPrime64 = 1099511628211UL;

        /// <summary>
        /// FNV-1a 64-bit hash of a string. Used to pack the canonical user id
        /// into platform-specific 64-bit score contexts (e.g. GameKit).
        /// </summary>
        public static long StableHash64(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0L;

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            ulong hash = FnvOffsetBasis64;

            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= FnvPrime64;
            }

            return unchecked((long)hash);
        }
    }
}
