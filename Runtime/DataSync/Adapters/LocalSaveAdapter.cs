using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "LocalSaveAdapter",
        menuName = "Scheherazade/Data Sync/Local Save Adapter"
    )]
    public class LocalSaveAdapter : ScriptableObject, ISaveAdapter
    {
        public string AdapterId => "local";

        public bool IsAvailable => true;

        public Task<bool> InitializeAsync()
        {
            Directory.CreateDirectory(RootPath);
            return Task.FromResult(true);
        }

        public void Reset() { }

        private static string RootPath
            => Path.Combine(Application.persistentDataPath, "SaveData");

        private string GetFilePath(string key)
            => Path.Combine(RootPath, key + ".dat");

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            string path = GetFilePath(key);
            if (!File.Exists(path)) return null;

            byte[] bytes = await File.ReadAllBytesAsync(path);
            return new MemoryStream(bytes);
        }

        public async Task WriteAsync(string key, Stream data, CancellationToken ct = default)
        {
            string path = GetFilePath(key);
            Directory.CreateDirectory(RootPath);

            using (var fs = new FileStream(
                path, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, useAsync: true))
            {
                await data.CopyToAsync(fs, 81920, ct);
            }
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            string path = GetFilePath(key);
            if (File.Exists(path))
            {
                File.Delete(path);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(File.Exists(GetFilePath(key)));
        }

        public Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default)
        {
            string path = GetFilePath(key);
            if (File.Exists(path))
            {
                return Task.FromResult<DateTime?>(File.GetLastWriteTimeUtc(path));
            }

            return Task.FromResult<DateTime?>(null);
        }
    }
}
