using System;
using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    internal class CatalogData
    {
        private Dictionary<string, CatalogEntry> _entries;
        private HashSet<string> _ids;

        public bool IsLoaded { get; private set; }

        public IReadOnlyCollection<string> CatalogedIds
        {
            get
            {
                if (!IsLoaded || _ids == null)
                {
                    return Array.Empty<string>();
                }

                return _ids;
            }
        }

        public void LoadFromStreamingAssets(string catalogFileName)
        {
            if (string.IsNullOrEmpty(catalogFileName))
            {
                QuickLog.Warning<CatalogData>(
                    "Catalog file name is null or empty."
                );
                return;
            }

            string fullPath = Path.Combine(
                Application.streamingAssetsPath, catalogFileName
            );

            if (!File.Exists(fullPath))
            {
                QuickLog.Warning<CatalogData>(
                    "Catalog file not found: {0}", fullPath
                );
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                LoadFromBytes(bytes);

                if (IsLoaded)
                {
                    QuickLog.Info<CatalogData>(
                        "Catalog loaded from '{0}' with {1} entries.",
                        fullPath, _ids.Count
                    );
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<CatalogData>(
                    "Failed to read catalog file '{0}': {1}",
                    fullPath, ex.Message
                );
            }
        }

        public void LoadFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                _entries = new Dictionary<string, CatalogEntry>();
                _ids = new HashSet<string>();
                IsLoaded = false;

                QuickLog.Warning<CatalogData>(
                    "Catalog bytes are null or empty."
                );
                return;
            }

            using MemoryStream stream = new MemoryStream(bytes);
            using StreamReader reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            LoadFromJson(json);
        }

        public void LoadFromJson(string json)
        {
            _entries = new Dictionary<string, CatalogEntry>();
            _ids = new HashSet<string>();
            IsLoaded = false;

            if (string.IsNullOrEmpty(json))
            {
                QuickLog.Warning<CatalogData>(
                    "Catalog JSON is null or empty."
                );
                return;
            }

            try
            {
                CatalogFileJson catalog = JsonConvert.DeserializeObject<CatalogFileJson>(
                    json
                );

                if (catalog?.Entries == null)
                {
                    QuickLog.Warning<CatalogData>(
                        "Catalog JSON is empty or malformed."
                    );
                    return;
                }

                foreach (CatalogEntry entry in catalog.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Id))
                    {
                        continue;
                    }

                    _entries[entry.Id] = entry;
                    _ids.Add(entry.Id);
                }

                IsLoaded = true;
            }
            catch (Exception ex)
            {
                QuickLog.Error<CatalogData>(
                    "Failed to parse catalog JSON: {0}", ex.Message
                );
            }
        }

        public bool HasResource(string resourceId)
        {
            return IsLoaded
                && _ids != null
                && !string.IsNullOrEmpty(resourceId)
                && _ids.Contains(resourceId);
        }

        public DataType GetDataType(string resourceId)
        {
            if (!IsLoaded
                || _entries == null
                || string.IsNullOrEmpty(resourceId))
            {
                return DataType.Unknown;
            }

            if (_entries.TryGetValue(resourceId, out CatalogEntry entry))
            {
                return entry.Type;
            }

            return DataType.Unknown;
        }

        public string GetRelativePath(string resourceId)
        {
            if (!IsLoaded
                || _entries == null
                || string.IsNullOrEmpty(resourceId))
            {
                return null;
            }

            if (_entries.TryGetValue(resourceId, out CatalogEntry entry))
            {
                return entry.RelativePath;
            }

            return null;
        }

        public void Reset()
        {
            _entries?.Clear();
            _ids?.Clear();
            IsLoaded = false;
        }

        [Serializable]
        private class CatalogFileJson
        {
            public int Version;
            public CatalogEntry[] Entries;
        }
    }
}
