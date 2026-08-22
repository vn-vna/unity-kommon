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
        public bool IsLoaded => _rawBytes != null;

        private readonly byte[] _rawBytes;
        private readonly Dictionary<Type, object> _parsedCache = new Dictionary<Type, object>();
        private readonly object _parseLock = new object();

        public PuzzleLevelData(string levelId, TextAsset textAsset, DataType type)
        {
            LevelId = ValidateLevelId(levelId);
            if (textAsset == null)
            {
                throw new ArgumentNullException(nameof(textAsset));
            }

            byte[] sourceBytes = textAsset.bytes;
            _rawBytes = sourceBytes == null
                ? Array.Empty<byte>()
                : (byte[])sourceBytes.Clone();
            Type = ValidateDataType(type, DataType.Text);
        }

        public PuzzleLevelData(string levelId, byte[] rawBytes)
            : this(levelId, rawBytes, DataType.Binary)
        {
        }

        public PuzzleLevelData(
            string levelId,
            byte[] rawBytes,
            DataType type)
        {
            LevelId = ValidateLevelId(levelId);
            if (rawBytes == null)
            {
                throw new ArgumentNullException(nameof(rawBytes));
            }

            _rawBytes = (byte[])rawBytes.Clone();
            Type = ValidateDataType(type, DataType.Text);
        }

        public string GetText()
        {
            if (_rawBytes != null)
            {
                return System.Text.Encoding.UTF8.GetString(_rawBytes);
            }

            return null;
        }

        public byte[] GetBytes()
        {
            return _rawBytes == null
                ? null
                : (byte[])_rawBytes.Clone();
        }

        public T GetParsed<T>()
        {
            if (TryGetParsed(out T value, out Exception error))
            {
                return value;
            }

            QuickLog.Warning<PuzzleLevelData>(
                "Failed to parse level '{0}' as {1}: {2}",
                LevelId,
                typeof(T).Name,
                error?.Message ?? "Unknown parsing error"
            );
            return default;
        }

        public bool TryGetParsed<T>(out T value, out Exception error)
        {
            lock (_parseLock)
            {
                if (_parsedCache.TryGetValue(typeof(T), out object cached))
                {
                    value = cached == null ? default : (T)cached;
                    error = null;
                    return true;
                }

                try
                {
                    string json = GetText();
                    if (string.IsNullOrEmpty(json))
                    {
                        value = default;
                        error = new InvalidOperationException(
                            $"Puzzle level '{LevelId}' contains no text data.");
                        return false;
                    }

                    value = JsonUtility.FromJson<T>(json);
                    _parsedCache[typeof(T)] = value;
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    value = default;
                    error = exception;
                    return false;
                }
            }
        }

        private static string ValidateLevelId(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId))
            {
                throw new ArgumentException(
                    "Level ID cannot be null, empty, or whitespace.",
                    nameof(levelId));
            }

            return levelId;
        }

        private static DataType ValidateDataType(
            DataType type,
            DataType defaultType)
        {
            if (type == DataType.Unknown)
            {
                return defaultType;
            }

            if (!Enum.IsDefined(typeof(DataType), type))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(type),
                    type,
                    "Unknown puzzle level data type.");
            }

            return type;
        }
    }
}
