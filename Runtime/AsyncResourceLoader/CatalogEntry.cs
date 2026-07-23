using System;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [Serializable]
    public struct CatalogEntry
    {
        public string Id;
        public DataType Type;
        public string RelativePath;
        public string ContentHash;
    }
}
