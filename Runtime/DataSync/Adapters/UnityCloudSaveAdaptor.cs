using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public class UnityCloudSaveAdaptor :
        ScriptableObject,
        ISaveAdapter
    {
        public string AdapterId => "unity-cloud-save";

        public bool IsAvailable => throw new NotImplementedException();

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> InitializeAsync()
        {
            throw new NotImplementedException();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public Task WriteAsync(string key, Stream data, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}