#if NEWTONSOFT_JSON
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "NewtonsoftBsonTranslator",
        menuName = "Scheherazade/Data Sync/Newtonsoft BSON Translator")]
    public class NewtonsoftBsonTranslator : ScriptableObject, ISaveTranslator
    {
        public string FormatId => "newtonsoft-bson";

        public byte[] Signature => new byte[] { 0x1F, 0x8B };

        public async Task<DecodeResult> DecodeAsync(
            Stream input, CancellationToken ct = default)
        {
            using (var gzip = new GZipStream(
                input, CompressionMode.Decompress, leaveOpen: true))
            using (var reader = new BsonReader(gzip))
            {
                var serializer = new JsonSerializer();
                JObject obj = serializer.Deserialize<JObject>(reader);
                JToken versionToken = obj["_version"];
                VersionTag version = versionToken != null
                    ? VersionTag.Parse(versionToken.ToString())
                    : VersionTag.Zero;
                obj.Remove("_version");
                await Task.CompletedTask;
                return new DecodeResult(obj, typeof(JObject), version);
            }
        }

        public async Task EncodeAsync(
            object data, VersionTag version,
            Stream output, CancellationToken ct = default)
        {
            JObject wrapper = JObject.FromObject(data);
            wrapper["_version"] = version.ToString();

            using (var gzip = new GZipStream(
                output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            using (var writer = new BsonWriter(gzip))
            {
                var serializer = new JsonSerializer();
                serializer.Serialize(writer, wrapper);
                await Task.CompletedTask;
            }
        }

        public object ConvertTo(object data, Type targetType)
        {
            if (data is JObject jobject)
            {
                return jobject.ToObject(targetType);
            }

            throw new TranslationException(
                $"Expected JObject, got {data?.GetType()?.Name ?? "null"}");
        }
    }
}
#endif
