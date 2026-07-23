using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelCachedProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Cached (LRU)"
    )]
    public sealed class PuzzleLevelCachedProvider :
        CachedAsyncResourceProvider<TextAsset>
    {
    }
}
