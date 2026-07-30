using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public enum CatalogInvalidationMode
    {
        /// <summary>
        /// Invalidate only catalog metadata.
        /// All cached resources are preserved.
        /// </summary>
        PreserveResources,

        /// <summary>
        /// Invalidate catalog metadata and clear memory cache.
        /// Useful when the catalog URL changes to a different CDN
        /// and cached resource content may be stale.
        /// </summary>
        Aggressive
    }

    public interface IInvalidatableCatalog
    {
        void InvalidateCatalog(CatalogInvalidationMode mode);
        IEnumerator InvalidateCatalogCoroutine(CatalogInvalidationMode mode);
    }
}
