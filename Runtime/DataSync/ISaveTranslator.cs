using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public interface ISaveTranslator
    {
        string FormatId { get; }

        byte[] Signature { get; }

        bool ValidateSignature(byte[] header);

        Task<DecodeResult> DecodeAsync(Stream input, CancellationToken ct = default);

        Task EncodeAsync(object data, VersionTag version, Stream output, CancellationToken ct = default);

        object ConvertTo(object data, Type targetType);
    }
}
