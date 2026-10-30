#if PLA_CREATIVE_BUILD && UNITY_LUNA
namespace Com.Scheherazade.Common.Threading
{
    /// <summary>
    /// Identity-only adapter for Luna's single JavaScript thread. Bridge's actual
    /// CurrentThread getter returns null. This does not implement or launch threads;
    /// native Unity, including creative Editor tests, retains real managed thread IDs.
    /// </summary>
    public static class CreativeBrowserThread
    {
        public static Identity CurrentThread => default;
        public readonly struct Identity
        {
            public int ManagedThreadId => 1;
        }
    }
}
#endif
