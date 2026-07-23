using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelDownloadableProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Downloadable"
    )]
    public sealed class PuzzleLevelDownloadableProvider :
        DownloadableResourceProvider<TextAsset>
    {
        protected override TextAsset ConvertResource(byte[] data)
        {
            return new TextAsset(Encoding.UTF8.GetString(data));
        }
    }
}
