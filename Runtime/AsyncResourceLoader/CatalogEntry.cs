using System;
using Newtonsoft.Json;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [Serializable]
    public struct CatalogEntry
    {
        public string Id;
        public DataType Type;

        [JsonProperty("relativePath")]
        public string RelativePath;

        [JsonProperty("contentHash")]
        public string ContentHash;

        [JsonProperty("path")]
        private string LegacyRelativePath
        {
            set
            {
                if (string.IsNullOrWhiteSpace(RelativePath))
                {
                    RelativePath = value;
                }
            }
        }
    }
}
