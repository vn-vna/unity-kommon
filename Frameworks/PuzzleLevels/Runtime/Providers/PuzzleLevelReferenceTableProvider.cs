using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelReferenceTableProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Reference Table Provider"
    )]
    public sealed class PuzzleLevelReferenceTableProvider :
        ReferenceTableAsyncResourceProvider<TextAsset>
    {
        [SerializeField]
        private PuzzleLevelReferenceTable _table;

#if UNITY_EDITOR
        [Tooltip(
            "Format string for resolving PuzzleLevelId to a table key.\n"
            + "{0} = ResourceId (e.g. \"level_{0}\")")]
#endif
        [SerializeField]
        private string _keyFormat = "{0}";

        public override IAsyncResourceReferenceTable<TextAsset> ReferenceTable
            => _table;

        internal string KeyFormat => _keyFormat;
    }
}
