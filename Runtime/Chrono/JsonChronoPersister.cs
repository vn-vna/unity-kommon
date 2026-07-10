using System;
using System.IO;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(
        fileName = "JsonChronoPersister",
        menuName = "Scheherazade/Chrono/JSON Persister"
    )]
    public class JsonChronoPersister : ScriptableObject, IChronoPersister
    {
        private const string DefaultFileName = "chrono_last_online.json";

        [SerializeField] private string _fileName = DefaultFileName;

        [NonSerialized] private string _filePath;

        [NonSerialized] private PersistedData _cachedData;

        [Serializable]
        private class PersistedData
        {
            public string lastOnlineKey;
            public string lastOnlineValue;
        }

        private void OnEnable()
        {
            _cachedData = null;
            _filePath = null;
        }

        private string FilePath
        {
            get
            {
                if (_filePath == null)
                {
                    _filePath = Path.Combine(
                        Application.persistentDataPath,
                        _fileName
                    );
                }

                return _filePath;
            }
        }

        public void Save(string key, DateTime value)
        {
            LoadFromDisk();

            _cachedData = _cachedData ?? new PersistedData();
            _cachedData.lastOnlineKey = key;
            _cachedData.lastOnlineValue = value.ToString("O");

            WriteToDisk();
        }

        public DateTime? Load(string key)
        {
            LoadFromDisk();

            if (_cachedData == null
                || _cachedData.lastOnlineKey != key
                || string.IsNullOrEmpty(_cachedData.lastOnlineValue))
            {
                return null;
            }

            if (DateTime.TryParse(
                    _cachedData.lastOnlineValue,
                    out DateTime result))
            {
                return result;
            }

            return null;
        }

        public void Delete(string key)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            _cachedData = null;
        }

        private void LoadFromDisk()
        {
            if (_cachedData != null)
            {
                return;
            }

            if (!File.Exists(FilePath))
            {
                _cachedData = new PersistedData();
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                _cachedData = JsonUtility.FromJson<PersistedData>(json)
                    ?? new PersistedData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[JsonChronoPersister] Failed to read '{FilePath}': {ex.Message}"
                );

                _cachedData = new PersistedData();
            }
        }

        private void WriteToDisk()
        {
            if (_cachedData == null)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)
                    && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(_cachedData, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[JsonChronoPersister] Failed to write '{FilePath}': {ex.Message}"
                );
            }
        }
    }
}
