using System.Collections.Generic;

namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface ICatalogAwareAsyncResourceProvider
    {
        IReadOnlyCollection<string> CatalogedIds { get; }
        bool HasResource(IAsyncResourceId resourceId);
        DataType GetDataType(string resourceId);
    }
}
