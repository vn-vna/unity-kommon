using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    internal class CatalogData
    {
        private Dictionary<string, CatalogEntry> _entries;
        private HashSet<string> _ids;
        private string[] _catalogedIds = Array.Empty<string>();

        public bool IsLoaded { get; private set; }

        public Exception LastException { get; private set; }

        public void ThrowIfFailed(string context)
        {
            if (LastException == null)
            {
                return;
            }

            throw new InvalidOperationException(
                context ?? "Catalog operation failed.",
                LastException);
        }

        public IReadOnlyCollection<string> CatalogedIds
        {
            get
            {
                if (!IsLoaded)
                {
                    return Array.Empty<string>();
                }

                return _catalogedIds;
            }
        }

        public void LoadFromStreamingAssets(string catalogFileName)
        {
            LastException = null;
            if (string.IsNullOrEmpty(catalogFileName))
            {
                SetError(new ArgumentException(
                    "Catalog file name is null or empty.",
                    nameof(catalogFileName)));
                return;
            }

            string fullPath = Path.Combine(
                Application.streamingAssetsPath, catalogFileName
            );

            if (RequiresUnityWebRequest(fullPath))
            {
                SetError(new InvalidOperationException(
                    "This StreamingAssets catalog path requires asynchronous "
                    + "loading. Use LoadFromStreamingAssetsCoroutine."));
                return;
            }

            LoadFromFile(fullPath);
        }

        public IEnumerator LoadFromStreamingAssetsCoroutine(
            string catalogFileName)
        {
            LastException = null;
            if (string.IsNullOrEmpty(catalogFileName))
            {
                SetError(new ArgumentException(
                    "Catalog file name is null or empty.",
                    nameof(catalogFileName)));
                yield break;
            }

            string fullPath = Path.Combine(
                Application.streamingAssetsPath,
                catalogFileName);

            if (!RequiresUnityWebRequest(fullPath))
            {
                LoadFromFile(fullPath);
                yield break;
            }

            using UnityWebRequest request = UnityWebRequest.Get(fullPath);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetError(new InvalidOperationException(
                    $"Failed to load catalog '{fullPath}': "
                    + (request.error ?? "Unknown error")));
                yield break;
            }

            LoadFromBytes(request.downloadHandler.data);
            if (IsLoaded)
            {
                QuickLog.Info<CatalogData>(
                    "Catalog loaded from '{0}' with {1} entries.",
                    fullPath,
                    _ids.Count
                );
            }
        }

        private void LoadFromFile(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                SetError(new FileNotFoundException(
                    "Catalog file was not found.",
                    fullPath));
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
                SetError(new IOException(
                    $"Failed to read catalog file '{fullPath}'.",
                    ex));
            }
        }

        public void LoadFromBytes(byte[] bytes)
        {
            LastException = null;
            if (bytes == null || bytes.Length == 0)
            {
                _entries = new Dictionary<string, CatalogEntry>();
                _ids = new HashSet<string>();
                _catalogedIds = Array.Empty<string>();
                IsLoaded = false;

                SetError(new InvalidDataException(
                    "Catalog bytes are null or empty."));
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
            _catalogedIds = Array.Empty<string>();
            IsLoaded = false;
            LastException = null;

            if (string.IsNullOrEmpty(json))
            {
                SetError(new InvalidDataException(
                    "Catalog JSON is null or empty."));
                return;
            }

            try
            {
                CatalogFileJson catalog = JsonConvert.DeserializeObject<CatalogFileJson>(
                    json
                );

                if (catalog?.Entries == null)
                {
                    SetError(new InvalidDataException(
                        "Catalog JSON is empty or malformed."));
                    return;
                }

                List<string> catalogedIds = new List<string>(
                    catalog.Entries.Length);
                for (int i = 0; i < catalog.Entries.Length; i++)
                {
                    CatalogEntry entry = catalog.Entries[i];
                    if (string.IsNullOrWhiteSpace(entry.Id))
                    {
                        SetError(new InvalidDataException(
                            $"Catalog entry {i} has an empty resource ID."));
                        return;
                    }

                    if (!_ids.Add(entry.Id))
                    {
                        SetError(new InvalidDataException(
                            $"Catalog contains duplicate resource ID "
                            + $"'{entry.Id}' at entry {i}."));
                        return;
                    }

                    if (!Enum.IsDefined(typeof(DataType), entry.Type))
                    {
                        SetError(new InvalidDataException(
                            $"Catalog entry '{entry.Id}' has invalid data type "
                            + $"value {(int)entry.Type}."));
                        return;
                    }

                    _entries.Add(entry.Id, entry);
                    catalogedIds.Add(entry.Id);
                }

                _catalogedIds = catalogedIds.ToArray();
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                SetError(new InvalidDataException(
                    "Failed to parse catalog JSON.",
                    ex));
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

        public string GetContentHash(string resourceId)
        {
            if (!IsLoaded
                || _entries == null
                || string.IsNullOrEmpty(resourceId))
            {
                return null;
            }

            return _entries.TryGetValue(resourceId, out CatalogEntry entry)
                ? entry.ContentHash
                : null;
        }

        public void Reset()
        {
            _entries?.Clear();
            _ids?.Clear();
            _catalogedIds = Array.Empty<string>();
            IsLoaded = false;
            LastException = null;
        }

        private static bool RequiresUnityWebRequest(string path)
        {
            return path.IndexOf("://", StringComparison.Ordinal) >= 0;
        }

        private void SetError(Exception exception)
        {
            LastException = exception;
            IsLoaded = false;

            QuickLog.Warning<CatalogData>(
                "Catalog operation failed: {0}",
                exception.Message
            );
        }

        [Serializable]
        private class CatalogFileJson
        {
            public int Version;
            public CatalogEntry[] Entries;
        }
    }
}
