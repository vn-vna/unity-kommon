using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "UnityJsonTranslator",
        menuName = "Scheherazade/Data Sync/Unity JSON Translator")]
    public class UnityJsonTranslator : ScriptableObject, ISaveTranslator
    {
        private const string VersionPrefix = "Version: ";

        public string FormatId => "unity-json";

        public byte[] Signature => new byte[] { 0x7B };

        public bool ValidateSignature(byte[] header)
            => header != null
            && header.Length >= Signature.Length
            && header[0] == 0x7B;

        public async Task<DecodeResult> DecodeAsync(
            Stream input, CancellationToken ct = default)
        {
            using (var reader = new StreamReader(
                input, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true))
            {
                string json = await reader.ReadToEndAsync();
                VersionTag version = ExtractVersion(ref json);
                return new DecodeResult(json, typeof(string), version);
            }
        }

        public async Task EncodeAsync(
            object data, VersionTag version,
            Stream output, CancellationToken ct = default)
        {
            string json = JsonUtility.ToJson(data);
            string withVersion = $"{VersionPrefix}{version}\n{json}";
            byte[] bytes = Encoding.UTF8.GetBytes(withVersion);
            await output.WriteAsync(bytes, 0, bytes.Length, ct);
        }

        public object ConvertTo(object data, Type targetType)
        {
            if (data is string json)
            {
                return JsonUtility.FromJson(json, targetType);
            }

            throw new TranslationException(
                $"Expected string, got {data?.GetType()?.Name ?? "null"}");
        }

        private static VersionTag ExtractVersion(ref string json)
        {
            if (json.StartsWith(VersionPrefix))
            {
                int newlineIndex = json.IndexOf('\n');
                if (newlineIndex > 0)
                {
                    string versionStr = json.Substring(
                        VersionPrefix.Length,
                        newlineIndex - VersionPrefix.Length
                    ).Trim();
                    json = json.Substring(newlineIndex + 1);
                    return VersionTag.Parse(versionStr);
                }
            }

            return VersionTag.Zero;
        }
    }
}
