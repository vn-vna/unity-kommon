#if UNITY_ADDRESSABLES
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelAddressableProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Addressable"
    )]
    public sealed class PuzzleLevelAddressableProvider :
        AddressableAsyncResourceProvider<TextAsset>
    {
#if UNITY_EDITOR
        [Tooltip(
            "Format string for resolving PuzzleLevelId to an addressable key.\n"
            + "{0} = ResourceId (e.g. \"puzzle_level_{0}\")")]
#endif
        [SerializeField]
        private string _keyFormat = "{0}";

        internal string KeyFormat => _keyFormat;
    }
}
#endif
