using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    internal readonly struct CapturedTagData
    {
        internal Type TagDefinitionType { get; }

        internal Type DataType { get; }

        internal string Json { get; }

        internal CapturedTagData(
            Type tagDefinitionType,
            Type dataType,
            string json)
        {
            TagDefinitionType = tagDefinitionType;
            DataType = dataType;
            Json = json;
        }
    }

    internal static class ItemTagDataSerializer
    {
        internal static CapturedTagData Capture(ITagData data)
        {
            if (data == null)
            {
                throw new ItemDatabaseException("Tag data cannot be null.");
            }

            Type dataType = data.GetType();
            Type tagDefinitionType = TagDataRegistry.GetTagDefType(dataType);
            if (tagDefinitionType == null)
            {
                throw new ItemDatabaseException(
                    $"Type '{dataType.FullName}' has no valid [TagData] mapping."
                );
            }

            string json = Serialize(tagDefinitionType, data);
            return new CapturedTagData(tagDefinitionType, dataType, json);
        }

        internal static string Serialize(Type tagDefinitionType, ITagData data)
        {
            if (tagDefinitionType == null)
            {
                throw new ArgumentNullException(nameof(tagDefinitionType));
            }

            if (data == null)
            {
                throw new ItemDatabaseException("Tag data cannot be null.");
            }

            Type expectedType = TagDataRegistry.GetDataType(tagDefinitionType);
            Type actualType = data.GetType();
            if (expectedType == null || expectedType != actualType)
            {
                throw new ItemDatabaseException(
                    $"Tag '{tagDefinitionType.Name}' expects "
                    + $"'{expectedType?.Name ?? "marker data"}', got '{actualType.Name}'."
                );
            }

            PrepareForSerialization(data);
            string json = JsonUtility.ToJson(data);
            _ = Deserialize(json, expectedType);
            return json;
        }

        internal static ITagData Deserialize(string json, Type dataType)
        {
            if (dataType == null || !typeof(ITagData).IsAssignableFrom(dataType))
            {
                throw new ItemDatabaseException("A valid tag data type is required.");
            }

            if (string.IsNullOrEmpty(json))
            {
                throw new ItemDatabaseException(
                    $"Serialized tag data for '{dataType.Name}' is empty."
                );
            }

            ITagData data;
            try
            {
                data = JsonUtility.FromJson(json, dataType) as ITagData;
            }
            catch (Exception exception)
            {
                throw new ItemDatabaseException(
                    $"Failed to deserialize '{dataType.Name}': {exception.Message}"
                );
            }

            if (data == null)
            {
                throw new ItemDatabaseException(
                    $"Failed to deserialize '{dataType.Name}'."
                );
            }

            RestoreAfterDeserialization(data);
            return data;
        }

        internal static T Clone<T>(T data) where T : class, ITagData
        {
            if (data == null) return null;

            CapturedTagData captured = Capture(data);
            return Deserialize(captured.Json, captured.DataType) as T;
        }

        private static void PrepareForSerialization(ITagData data)
        {
            if (data is ExpirableData expirable)
            {
                expirable.PrepareForSerialization();
            }
        }

        private static void RestoreAfterDeserialization(ITagData data)
        {
            if (data is ExpirableData expirable
                && !expirable.TryRestoreAfterDeserialization())
            {
                throw new ItemDatabaseException(
                    "ExpirableData does not contain a persisted UTC expiry."
                );
            }
        }
    }
}
