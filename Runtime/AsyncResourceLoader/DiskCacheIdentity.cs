using System;
using System.IO;
using System.Text;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    internal static class DiskCacheIdentity
    {
        private const string IdentitySuffix = ".id";
        private const string TemporarySuffix = ".tmp";

        private static readonly Encoding Utf8WithoutBom
            = new UTF8Encoding(false);

        public static void Write(
            string cacheFilePath,
            string resourceId,
            byte[] fileData)
        {
            if (string.IsNullOrWhiteSpace(cacheFilePath))
            {
                throw new ArgumentException(
                    "Cache file path cannot be empty.",
                    nameof(cacheFilePath));
            }

            if (fileData == null)
            {
                throw new ArgumentNullException(nameof(fileData));
            }

            string identityPath = cacheFilePath + IdentitySuffix;
            string temporaryCachePath = cacheFilePath + TemporarySuffix;
            string temporaryIdentityPath = identityPath + TemporarySuffix;
            DeleteFile(temporaryCachePath);
            DeleteFile(temporaryIdentityPath);

            try
            {
                File.WriteAllBytes(temporaryCachePath, fileData);
                File.WriteAllText(
                    temporaryIdentityPath,
                    resourceId ?? string.Empty,
                    Utf8WithoutBom);

                Delete(cacheFilePath);
                File.Move(temporaryIdentityPath, identityPath);
                try
                {
                    File.Move(temporaryCachePath, cacheFilePath);
                }
                catch
                {
                    DeleteFile(identityPath);
                    throw;
                }
            }
            finally
            {
                DeleteFile(temporaryCachePath);
                DeleteFile(temporaryIdentityPath);
            }
        }

        public static void Delete(string cacheFilePath)
        {
            if (string.IsNullOrWhiteSpace(cacheFilePath))
            {
                return;
            }

            DeleteFile(cacheFilePath);
            DeleteFile(cacheFilePath + IdentitySuffix);
        }

        public static int DeleteByResourceId(
            string cacheRoot,
            string resourceId)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot)
                || !Directory.Exists(cacheRoot))
            {
                return 0;
            }

            int deletedCount = 0;
            string[] identityFiles = Directory.GetFiles(
                cacheRoot,
                "*" + IdentitySuffix,
                SearchOption.TopDirectoryOnly);
            for (int i = 0; i < identityFiles.Length; i++)
            {
                string identityFile = identityFiles[i];
                string storedResourceId;
                try
                {
                    storedResourceId = File.ReadAllText(
                        identityFile,
                        Encoding.UTF8).TrimStart('\uFEFF');
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(
                        storedResourceId,
                        resourceId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string cacheFilePath = identityFile.Substring(
                    0,
                    identityFile.Length - IdentitySuffix.Length);
                Delete(cacheFilePath);
                deletedCount++;
            }

            return deletedCount;
        }

        private static void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
