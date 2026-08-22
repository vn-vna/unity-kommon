using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelReferenceTableProvider",
        menuName = "Scheherazade/Puzzle Levels/Providers/Reference Table Provider"
    )]
    public sealed class PuzzleLevelReferenceTableProvider :
        ReferenceTableAsyncResourceProvider<TextAsset>,
        ICatalogAwareAsyncResourceProvider,
        IAsyncResourceDataTypeResolver
    {
        [SerializeField]
        private PuzzleLevelReferenceTable _table;

#if UNITY_EDITOR
        [Tooltip(
            "Format string for resolving PuzzleLevelId to a table key.\n"
            + "{id} = ResourceId (e.g. \"level_{id}\")")]
#endif
        [SerializeField]
        private string _keyFormat = "{id}";

        public override IAsyncResourceReferenceTable<TextAsset> ReferenceTable
            => _table;

        internal string KeyFormat => _keyFormat;

        public System.Collections.Generic.IReadOnlyCollection<string> CatalogedIds =>
            _table?.CatalogedIds ?? System.Array.Empty<string>();

        public bool HasResource(IAsyncResourceId resourceId)
        {
            if (_table == null
                || resourceId is not IReferenceTableAsyncResourceId referenceId)
            {
                return false;
            }

            return _table.HasResource(referenceId.GetResourceId(this));
        }

        public DataType GetDataType(string resourceId)
        {
            return _table?.GetDataType(ApplyKeyFormat(resourceId))
                ?? DataType.Unknown;
        }

        public DataType GetDataType(IAsyncResourceId resourceId)
        {
            if (_table == null
                || resourceId is not IReferenceTableAsyncResourceId referenceId)
            {
                return DataType.Unknown;
            }

            string resolvedId = referenceId.GetResourceId(this);
            return _table.GetDataType(resolvedId);
        }

        private string ApplyKeyFormat(string resourceId)
        {
            return (_keyFormat ?? "{id}")
                .Replace("{id}", resourceId ?? string.Empty)
                .Replace("{0}", resourceId ?? string.Empty);
        }
    }
}
