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
        #region Interfaces & Properties

        public string AdapterId => "local";

        public TimeSpan ReadTimeout => TimeSpan.FromSeconds(3);

        public bool IsAvailable => true;

        public SaveAdapterFeature SupportedFeatures
            => SaveAdapterFeature.Read
             | SaveAdapterFeature.Write
             | SaveAdapterFeature.Delete
             | SaveAdapterFeature.Exists
             | SaveAdapterFeature.KvStore;

        private string RootPath
            => Path.Combine(Application.persistentDataPath, "SaveData");

        #endregion

        #region Public Methods

        public Task<bool> InitializeAsync()
        {
            Directory.CreateDirectory(RootPath);
            RecoverInterruptedWrites();
            return Task.FromResult(true);
        }

        public void Reset() { }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = GetFilePath(key);
            if (!File.Exists(path)) return null;

            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                useAsync: true
            );
        }

        public async Task WriteAsync(string key, Stream data, CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            string path = GetFilePath(key);
            string tmpPath = path + ".tmp";
            string backupPath = path + ".bak";
            Directory.CreateDirectory(RootPath);

            try
            {
                using (FileStream fs = new FileStream(
                    tmpPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    useAsync: true))
                {
                    await data.CopyToAsync(fs, 81920, ct);
                    await fs.FlushAsync(ct);
                    fs.Flush(flushToDisk: true);
                }

                ct.ThrowIfCancellationRequested();
                ReplaceFile(tmpPath, path, backupPath);
                TryDelete(backupPath);
            }
            catch
            {
                TryDelete(tmpPath);
                throw;
            }
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = GetFilePath(key);
            bool deleted = false;

            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }

            string tmpPath = path + ".tmp";
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
            string backupPath = path + ".bak";
            if (File.Exists(backupPath)) File.Delete(backupPath);

            return Task.FromResult(deleted);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(File.Exists(GetFilePath(key)));
        }

        public Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = GetFilePath(key);
            if (File.Exists(path))
            {
                return Task.FromResult<DateTime?>(File.GetLastWriteTimeUtc(path));
            }

            return Task.FromResult<DateTime?>(null);
        }

        #endregion

        #region Private Methods

        private string GetFilePath(string key)
        {
            ValidateKey(key);
            return Path.Combine(RootPath, key + ".dat");
        }

        private void RecoverInterruptedWrites()
        {
            foreach (string backupPath in Directory.GetFiles(RootPath, "*.bak"))
            {
                string path = backupPath.Substring(
                    0,
                    backupPath.Length - ".bak".Length
                );
                if (!File.Exists(path))
                {
                    File.Move(backupPath, path);
                    continue;
                }

                TryDelete(backupPath);
            }

            foreach (string tmpPath in Directory.GetFiles(RootPath, "*.tmp"))
            {
                // A temporary file has not crossed the atomic commit boundary.
                // It may be only partially written after process termination.
                TryDelete(tmpPath);
            }
        }

        private static void ReplaceFile(
            string tmpPath,
            string path,
            string backupPath)
        {
            if (!File.Exists(path))
            {
                File.Move(tmpPath, path);
                return;
            }

            TryDelete(backupPath);
            try
            {
                File.Replace(tmpPath, path, backupPath);
            }
            catch (Exception exception)
                when (exception is PlatformNotSupportedException
                    || exception is IOException)
            {
                ReplaceFileWithRecoverableMoves(tmpPath, path, backupPath);
            }
        }

        private static void ReplaceFileWithRecoverableMoves(
            string tmpPath,
            string path,
            string backupPath)
        {
            File.Move(path, backupPath);
            try
            {
                File.Move(tmpPath, path);
            }
            catch
            {
                if (!File.Exists(path) && File.Exists(backupPath))
                {
                    File.Move(backupPath, path);
                }

                throw;
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key == "."
                || key == ".."
                || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || key.IndexOf(Path.DirectorySeparatorChar) >= 0
                || key.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new ArgumentException(
                    "Save key must be a valid file name.",
                    nameof(key)
                );
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        #endregion
    }
}
