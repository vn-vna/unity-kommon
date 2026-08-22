using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    internal static class CatalogContentHash
    {
        private const string Sha256Prefix = "sha256:";
        private const int Sha256HexLength = 64;

        public static bool TryValidate(
            string resourceId,
            byte[] data,
            string expectedHash,
            out Exception exception)
        {
            exception = null;
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                return true;
            }

            string normalizedHash = expectedHash.StartsWith(
                    Sha256Prefix,
                    StringComparison.OrdinalIgnoreCase)
                ? expectedHash.Substring(Sha256Prefix.Length)
                : expectedHash;
            normalizedHash = normalizedHash.Trim();
            if (normalizedHash.Length != Sha256HexLength)
            {
                exception = new InvalidDataException(
                    $"Catalog hash for '{resourceId}' must be a SHA-256 "
                    + "hex string.");
                return false;
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] actualHash = sha256.ComputeHash(data ?? Array.Empty<byte>());
            StringBuilder actualBuilder = new StringBuilder(
                actualHash.Length * 2);
            for (int i = 0; i < actualHash.Length; i++)
            {
                actualBuilder.Append(actualHash[i].ToString("x2"));
            }

            if (string.Equals(
                    normalizedHash,
                    actualBuilder.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            exception = new InvalidDataException(
                $"Content hash mismatch for resource '{resourceId}'.");
            return false;
        }
    }
}
