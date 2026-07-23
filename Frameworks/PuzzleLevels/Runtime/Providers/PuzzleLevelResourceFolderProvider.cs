using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelResourceFolderProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Resources Folder"
    )]
    public sealed class PuzzleLevelResourceFolderProvider :
        ResourceFolderAsyncResourceProvider<TextAsset>
    {
#if UNITY_EDITOR
        [Tooltip(
            "Format string for resolving PuzzleLevelId to a resource path.\n"
            + "{0} = ResourceId (e.g. \"levels/level_{0}\")")]
#endif
        [SerializeField]
        private string _pathFormat = "{0}";

        internal string PathFormat => _pathFormat;
    }
}
