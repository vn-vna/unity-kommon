using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelStreamingAssetProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Streaming Assets"
    )]
    public sealed class PuzzleLevelStreamingAssetProvider :
        StreamingAssetProvider<TextAsset>
    {
#if UNITY_EDITOR
        [Tooltip(
            "Format string for resolving PuzzleLevelId to a file path.\n"
            + "{0} = ResourceId (e.g. \"level_{0}.json\")")]
#endif
        [SerializeField]
        private string _pathFormat = "{0}";

        internal string PathFormat => _pathFormat;

        protected override TextAsset ConvertResource(byte[] data)
        {
            return new TextAsset(Encoding.UTF8.GetString(data));
        }
    }
}
