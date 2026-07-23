using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    public class PuzzleLevelData : IPuzzleLevelData
    {
        public string LevelId { get; }
        public DataType Type { get; }
        public bool IsLoaded => _textAsset != null || _rawBytes != null;

        private readonly TextAsset _textAsset;
        private byte[] _rawBytes;
        private readonly Dictionary<Type, object> _parsedCache = new Dictionary<Type, object>();

        public PuzzleLevelData(string levelId, TextAsset textAsset, DataType type)
        {
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            _textAsset = textAsset ?? throw new ArgumentNullException(nameof(textAsset));
            Type = type;
        }

        public PuzzleLevelData(string levelId, byte[] rawBytes)
        {
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            _rawBytes = rawBytes ?? throw new ArgumentNullException(nameof(rawBytes));
            Type = DataType.Binary;
        }

        public string GetText()
        {
            if (_textAsset != null)
            {
                return _textAsset.text;
            }

            if (_rawBytes != null)
            {
                return System.Text.Encoding.UTF8.GetString(_rawBytes);
            }

            return null;
        }

        public byte[] GetBytes()
        {
            if (_textAsset != null)
            {
                return Type == DataType.Binary
                    ? _textAsset.bytes
                    : System.Text.Encoding.UTF8.GetBytes(_textAsset.text);
            }

            return _rawBytes;
        }

        public T GetParsed<T>()
        {
            if (_parsedCache.TryGetValue(typeof(T), out object cached))
            {
                return (T)cached;
            }

            try
            {
                string json = GetText();
                if (string.IsNullOrEmpty(json))
                {
                    return default;
                }

                T parsed = JsonUtility.FromJson<T>(json);
                _parsedCache[typeof(T)] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<PuzzleLevelData>(
                    "Failed to parse level '{0}' as {1}: {2}",
                    LevelId, typeof(T).Name, ex.Message
                );
                return default;
            }
        }
    }
}
