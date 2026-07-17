using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public interface ISaveAdapter
    {
        string AdapterId { get; }

        bool IsAvailable { get; }

        Task<bool> InitializeAsync();

        void Reset();

        Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

        Task WriteAsync(string key, Stream data, CancellationToken ct = default);

        Task<bool> DeleteAsync(string key, CancellationToken ct = default);

        Task<bool> ExistsAsync(string key, CancellationToken ct = default);

        Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default);

        SaveAdapterFeature SupportedFeatures { get; }
    }
}
