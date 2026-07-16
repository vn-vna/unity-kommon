#if NEWTONSOFT_JSON
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "NewtonsoftJsonTranslator",
        menuName = "Scheherazade/Data Sync/Newtonsoft JSON Translator")]
    public class NewtonsoftJsonTranslator : ScriptableObject, ISaveTranslator
    {
        public string FormatId => "newtonsoft-json";

        public byte[] Signature => new byte[] { 0x7B };

        public async Task<DecodeResult> DecodeAsync(
            Stream input, CancellationToken ct = default)
        {
            using (var reader = new StreamReader(
                input, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true))
            {
                string json = await reader.ReadToEndAsync();
                JObject obj = JObject.Parse(json);
                JToken versionToken = obj["_version"];
                VersionTag version = versionToken != null
                    ? VersionTag.Parse(versionToken.ToString())
                    : VersionTag.Zero;
                obj.Remove("_version");
                return new DecodeResult(obj, typeof(JObject), version);
            }
        }

        public async Task EncodeAsync(
            object data, VersionTag version,
            Stream output, CancellationToken ct = default)
        {
            JObject wrapper = JObject.FromObject(data);
            wrapper["_version"] = version.ToString();
            string json = wrapper.ToString(Formatting.None);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await output.WriteAsync(bytes, 0, bytes.Length, ct);
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
