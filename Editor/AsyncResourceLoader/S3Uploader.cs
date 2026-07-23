using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    public enum S3UploadStatus
    {
        Success,
        NetworkError,
        AuthError,
        NotFound,
        InvalidConfig,
        Cancelled,
        Unknown
    }

    public struct S3Result
    {
        public S3UploadStatus Status;
        public long HttpStatusCode;
        public string Message;
        public string ETag;

        public bool IsSuccess
        {
            get { return Status == S3UploadStatus.Success; }
        }
    }

    public static class S3Uploader
    {
        private const string Algorithm = "AWS4-HMAC-SHA256";
        private const string ServiceName = "s3";
        private const string Terminator = "aws4_request";
        private const string Iso8601Format = "yyyyMMddTHHmmssZ";
        private const string DateStampFormat = "yyyyMMdd";

        private static readonly HashSet<string> TextContentTypes
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".json", ".txt", ".csv", ".xml", ".html", ".htm",
                ".css", ".js", ".yaml", ".yml", ".md"
            };

        #region Public API

        public static void PutObject(
            S3UploadSettings settings,
            string key,
            byte[] data,
            string contentType,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            if (data == null)
            {
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.InvalidConfig,
                    Message = "Data is null."
                });
                return;
            }

            string url = BuildUrl(settings, key);
            string host = BuildHost(settings);
            string payloadHash = Sha256Hex(data);
            string amzDate = DateTime.UtcNow.ToString(Iso8601Format);

            Dictionary<string, string> headers
                = new Dictionary<string, string>
                {
                    { "host", host },
                    { "x-amz-content-sha256", payloadHash },
                    { "x-amz-date", amzDate }
                };

            AppendContentHeaders(headers, contentType, settings.PublicRead);

            string authorization = BuildAuthorizationHeader(
                "PUT", payloadHash, amzDate, headers, settings);

            UnityWebRequest request = CreateSignedRequest(
                url, UnityWebRequest.kHttpVerbPUT,
                authorization, payloadHash, amzDate, host, headers);

            request.uploadHandler = new UploadHandlerRaw(data);
            SendRequest(request, onComplete);
        }

        public static void UploadDirectory(
            S3UploadSettings settings,
            string localDirectory,
            Func<bool> shouldCancel,
            Action<string, float> onProgress,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            if (!Directory.Exists(localDirectory))
            {
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.InvalidConfig,
                    Message = $"Directory not found: {localDirectory}"
                });
                return;
            }

            string[] files = DiscoverFiles(localDirectory);

            if (files.Length == 0)
            {
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.Success,
                    Message = "No files to upload (empty directory)."
                });
                return;
            }

            UploadState state = new UploadState
            {
                Files = files,
                LocalDirectory = localDirectory,
                TotalCount = files.Length
            };

            UploadNext(settings, state, 0,
                shouldCancel, onProgress, onComplete);
        }

        public static void TestConnection(
            S3UploadSettings settings,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            string prefix = string.IsNullOrWhiteSpace(settings.BasePrefix)
                ? "test-connection-probe"
                : settings.BasePrefix.TrimEnd('/')
                    + "/test-connection-probe";

            ListObjects(settings, prefix, 1, null, result =>
            {
                if (result.IsSuccess
                    || result.Status == S3UploadStatus.NotFound)
                {
                    onComplete?.Invoke(new S3Result
                    {
                        Status = S3UploadStatus.Success,
                        HttpStatusCode = result.HttpStatusCode,
                        Message = "Connection successful."
                    });
                }
                else
                {
                    onComplete?.Invoke(result);
                }
            });
        }

        public static void DeleteObject(
            S3UploadSettings settings,
            string key,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            string url = BuildUrl(settings, key);
            string host = BuildHost(settings);
            string payloadHash = Sha256Hex("");
            string amzDate = DateTime.UtcNow.ToString(Iso8601Format);

            Dictionary<string, string> headers
                = new Dictionary<string, string>
                {
                    { "host", host },
                    { "x-amz-content-sha256", payloadHash },
                    { "x-amz-date", amzDate }
                };

            string authorization = BuildAuthorizationHeader(
                "DELETE", payloadHash, amzDate, headers, settings);

            UnityWebRequest request = CreateSignedRequest(
                url, UnityWebRequest.kHttpVerbDELETE,
                authorization, payloadHash, amzDate, host, headers);

            SendRequest(request, onComplete);
        }

        public static void ListObjects(
            S3UploadSettings settings,
            string prefix,
            int maxKeys,
            string continuationToken,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            string url = BuildUrl(settings, "");
            string host = BuildHost(settings);
            string payloadHash = Sha256Hex("");
            string amzDate = DateTime.UtcNow.ToString(Iso8601Format);

            string queryString = BuildListQuery(
                prefix, maxKeys, continuationToken);

            Dictionary<string, string> headers
                = new Dictionary<string, string>
                {
                    { "host", host },
                    { "x-amz-content-sha256", payloadHash },
                    { "x-amz-date", amzDate }
                };

            string canonicalQueryString
                = BuildCanonicalQueryString(queryString);
            string authorization = BuildAuthorizationWithQuery(
                "GET", payloadHash, amzDate, headers,
                canonicalQueryString, settings);

            UnityWebRequest request = CreateSignedRequest(
                url + "?" + queryString, UnityWebRequest.kHttpVerbGET,
                authorization, payloadHash, amzDate, host, headers);

            SendRequest(request, onComplete);
        }

        public static void HeadObject(
            S3UploadSettings settings,
            string key,
            Action<S3Result> onComplete)
        {
            if (!ValidateSettings(settings, onComplete))
            {
                return;
            }

            string url = BuildUrl(settings, key);
            string host = BuildHost(settings);
            string payloadHash = Sha256Hex("");
            string amzDate = DateTime.UtcNow.ToString(Iso8601Format);

            Dictionary<string, string> headers
                = new Dictionary<string, string>
                {
                    { "host", host },
                    { "x-amz-content-sha256", payloadHash },
                    { "x-amz-date", amzDate }
                };

            string authorization = BuildAuthorizationHeader(
                "HEAD", payloadHash, amzDate, headers, settings);

            UnityWebRequest request = CreateSignedRequest(
                url, UnityWebRequest.kHttpVerbHEAD,
                authorization, payloadHash, amzDate, host, headers);

            SendRequest(request, onComplete);
        }

        #endregion

        #region Private — Types

        private sealed class UploadState
        {
            public string[] Files;
            public string LocalDirectory;
            public int TotalCount;
            public int Completed;
            public int Failed;
            public StringBuilder Errors = new StringBuilder();
        }

        #endregion

        #region Private — Validation & Helpers

        private static bool ValidateSettings(
            S3UploadSettings settings,
            Action<S3Result> onComplete)
        {
            if (settings.IsValid)
            {
                return true;
            }

            onComplete?.Invoke(new S3Result
            {
                Status = S3UploadStatus.InvalidConfig,
                Message = "S3 settings are incomplete."
            });
            return false;
        }

        private static string[] DiscoverFiles(string localDirectory)
        {
            string[] allFiles = Directory.GetFiles(
                localDirectory, "*", SearchOption.AllDirectories);

            List<string> filtered
                = new List<string>(allFiles.Length);
            foreach (string f in allFiles)
            {
                if (!f.EndsWith(".meta",
                        StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(f);
                }
            }

            return filtered.ToArray();
        }

        private static void AppendContentHeaders(
            Dictionary<string, string> headers,
            string contentType,
            bool publicRead)
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                headers["content-type"] = contentType;
            }

            if (publicRead)
            {
                headers["x-amz-acl"] = "public-read";
            }
        }

        #endregion

        #region Private — Upload Directory (Recursive)

        private static void UploadNext(
            S3UploadSettings settings,
            UploadState state,
            int index,
            Func<bool> shouldCancel,
            Action<string, float> onProgress,
            Action<S3Result> onComplete)
        {
            if (shouldCancel != null && shouldCancel())
            {
                onProgress?.Invoke(
                    $"Cancelled ({state.Completed}/{state.TotalCount})",
                    1f);
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.Cancelled,
                    Message = $"Upload cancelled. "
                        + $"Completed: {state.Completed}/{state.TotalCount}."
                });
                return;
            }

            if (index >= state.Files.Length)
            {
                FinalizeUpload(state, onProgress, onComplete);
                return;
            }

            string filePath = state.Files[index];
            string relativePath = ComputeRelativePath(
                filePath, state.LocalDirectory);
            string key = CombineKey(
                settings.BasePrefix, relativePath);

            onProgress?.Invoke(
                $"Uploading: {relativePath}",
                (float)index / state.TotalCount);

            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                state.Failed++;
                state.Errors.AppendLine(
                    $"{relativePath}: Read error - {ex.Message}");
                UploadNext(settings, state, index + 1,
                    shouldCancel, onProgress, onComplete);
                return;
            }

            string contentType = GetContentType(filePath);

            PutObject(settings, key, data, contentType, result =>
            {
                if (result.IsSuccess)
                {
                    state.Completed++;
                }
                else
                {
                    state.Failed++;
                    state.Errors.AppendLine(
                        $"{relativePath}: [{result.HttpStatusCode}] "
                        + $"{result.Message}");
                }

                UploadNext(settings, state, index + 1,
                    shouldCancel, onProgress, onComplete);
            });
        }

        private static void FinalizeUpload(
            UploadState state,
            Action<string, float> onProgress,
            Action<S3Result> onComplete)
        {
            onProgress?.Invoke(
                $"Done: {state.Completed}/{state.TotalCount}", 1f);

            if (state.Failed > 0)
            {
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.NetworkError,
                    Message = $"Uploaded {state.Completed}"
                        + $"/{state.TotalCount} files "
                        + $"with {state.Failed} errors.\n{state.Errors}"
                });
            }
            else
            {
                onComplete?.Invoke(new S3Result
                {
                    Status = S3UploadStatus.Success,
                    Message = $"Uploaded {state.Completed}"
                        + $"/{state.TotalCount} files successfully."
                });
            }
        }

        #endregion

        #region Private — Request Dispatch

        private static UnityWebRequest CreateSignedRequest(
            string url,
            string method,
            string authorization,
            string payloadHash,
            string amzDate,
            string host,
            Dictionary<string, string> headers)
        {
            UnityWebRequest request
                = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Authorization", authorization);
            request.SetRequestHeader(
                "x-amz-content-sha256", payloadHash);
            request.SetRequestHeader(
                "x-amz-date", amzDate);
            request.SetRequestHeader(
                "Host", host);

            foreach (KeyValuePair<string, string> h in headers)
            {
                if (h.Key == "host"
                    || h.Key == "x-amz-content-sha256"
                    || h.Key == "x-amz-date")
                {
                    continue;
                }

                request.SetRequestHeader(h.Key, h.Value);
            }

            return request;
        }

        private static void SendRequest(
            UnityWebRequest request,
            Action<S3Result> onComplete)
        {
            UnityWebRequestAsyncOperation op
                = request.SendWebRequest();

            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!op.isDone)
                {
                    return;
                }

                EditorApplication.update -= poll;

                S3Result result = BuildResult(request);
                request.Dispose();
                onComplete?.Invoke(result);
            };

            EditorApplication.update += poll;
        }

        private static S3Result BuildResult(
            UnityWebRequest request)
        {
            long statusCode = request.responseCode;
            S3UploadStatus status = MapStatus(
                request.result, statusCode);

            string message = request.error;
            if (string.IsNullOrEmpty(message)
                && request.downloadHandler != null
                && !string.IsNullOrEmpty(
                    request.downloadHandler.text))
            {
                message = ExtractS3ErrorMessage(
                    request.downloadHandler.text);
            }

            string etag = request.GetResponseHeader("ETag")
                ?.Trim('"');

            return new S3Result
            {
                Status = status,
                HttpStatusCode = statusCode,
                Message = message ?? "Unknown error",
                ETag = etag
            };
        }

        private static S3UploadStatus MapStatus(
            UnityWebRequest.Result result,
            long statusCode)
        {
            if (result == UnityWebRequest.Result.Success)
            {
                return S3UploadStatus.Success;
            }

            if (result == UnityWebRequest.Result.ConnectionError)
            {
                return S3UploadStatus.NetworkError;
            }

            if (statusCode == 403 || statusCode == 401)
            {
                return S3UploadStatus.AuthError;
            }

            if (statusCode == 404)
            {
                return S3UploadStatus.NotFound;
            }

            return S3UploadStatus.Unknown;
        }

        private static string ExtractS3ErrorMessage(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            const string codeTag = "<Code>";
            const string codeEndTag = "</Code>";
            const string msgTag = "<Message>";
            const string msgEndTag = "</Message>";

            int codeStart = xml.IndexOf(
                codeTag, StringComparison.Ordinal);
            int codeEnd = xml.IndexOf(
                codeEndTag, StringComparison.Ordinal);
            int msgStart = xml.IndexOf(
                msgTag, StringComparison.Ordinal);
            int msgEnd = xml.IndexOf(
                msgEndTag, StringComparison.Ordinal);

            if (codeStart >= 0 && codeEnd > codeStart
                && msgStart >= 0 && msgEnd > msgStart)
            {
                string code = xml.Substring(
                    codeStart + codeTag.Length,
                    codeEnd - codeStart - codeTag.Length);
                string message = xml.Substring(
                    msgStart + msgTag.Length,
                    msgEnd - msgStart - msgTag.Length);
                return $"{code}: {message}";
            }

            int maxLen = Math.Min(xml.Length, 200);
            return xml.Substring(0, maxLen);
        }

        #endregion

        #region Private — AWS Signature V4

        private static string BuildAuthorizationHeader(
            string method,
            string payloadHash,
            string amzDate,
            Dictionary<string, string> headers,
            S3UploadSettings settings)
        {
            string canonicalRequest = BuildCanonicalRequest(
                method, "/", "", headers, payloadHash);
            string authorization = BuildAuthorization(
                canonicalRequest, amzDate, headers, settings);
            return authorization;
        }

        private static string BuildAuthorizationWithQuery(
            string method,
            string payloadHash,
            string amzDate,
            Dictionary<string, string> headers,
            string canonicalQueryString,
            S3UploadSettings settings)
        {
            string canonicalRequest = BuildCanonicalRequest(
                method, "/", canonicalQueryString,
                headers, payloadHash);
            string authorization = BuildAuthorization(
                canonicalRequest, amzDate, headers, settings);
            return authorization;
        }

        private static string BuildAuthorization(
            string canonicalRequest,
            string amzDate,
            Dictionary<string, string> headers,
            S3UploadSettings settings)
        {
            string stringToSign = BuildStringToSign(
                amzDate, settings.Region, canonicalRequest);
            string signature = CalculateSignature(
                settings.SecretKey, amzDate,
                settings.Region, stringToSign);
            string signedHeaders = BuildSignedHeaders(headers);
            string dateStamp
                = DateTime.UtcNow.ToString(DateStampFormat);

            return $"{Algorithm} "
                + $"Credential={settings.AccessKey}/{dateStamp}"
                + $"/{settings.Region}/{ServiceName}/{Terminator}, "
                + $"SignedHeaders={signedHeaders}, "
                + $"Signature={signature}";
        }

        private static string BuildCanonicalRequest(
            string method,
            string canonicalUri,
            string canonicalQueryString,
            Dictionary<string, string> headers,
            string payloadHash)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(method).Append('\n');
            sb.Append(canonicalUri).Append('\n');
            sb.Append(canonicalQueryString).Append('\n');

            List<KeyValuePair<string, string>> sortedHeaders
                = new List<KeyValuePair<string, string>>(headers);
            sortedHeaders.Sort((a, b)
                => string.Compare(
                    a.Key.ToLowerInvariant(),
                    b.Key.ToLowerInvariant(),
                    StringComparison.Ordinal));

            foreach (KeyValuePair<string, string> kvp
                in sortedHeaders)
            {
                sb.Append(kvp.Key.ToLowerInvariant())
                    .Append(':')
                    .Append(kvp.Value.Trim())
                    .Append('\n');
            }

            sb.Append('\n');
            sb.Append(BuildSignedHeaders(headers));
            sb.Append('\n');
            sb.Append(payloadHash);

            return sb.ToString();
        }

        private static string BuildSignedHeaders(
            Dictionary<string, string> headers)
        {
            List<string> sorted = new List<string>();
            foreach (string key in headers.Keys)
            {
                sorted.Add(key.ToLowerInvariant());
            }

            sorted.Sort(StringComparer.Ordinal);
            return string.Join(";", sorted);
        }

        private static string BuildStringToSign(
            string amzDate,
            string region,
            string canonicalRequest)
        {
            string dateStamp
                = DateTime.UtcNow.ToString(DateStampFormat);
            string scope
                = $"{dateStamp}/{region}"
                + $"/{ServiceName}/{Terminator}";

            return $"{Algorithm}\n"
                + $"{amzDate}\n"
                + $"{scope}\n"
                + $"{Sha256Hex(canonicalRequest)}";
        }

        private static string CalculateSignature(
            string secretKey,
            string amzDate,
            string region,
            string stringToSign)
        {
            string dateStamp
                = DateTime.UtcNow.ToString(DateStampFormat);

            byte[] kSecret = Encoding.UTF8.GetBytes(
                "AWS4" + secretKey);
            byte[] kDate = HmacSha256(kSecret, dateStamp);
            byte[] kRegion = HmacSha256(kDate, region);
            byte[] kService = HmacSha256(kRegion, ServiceName);
            byte[] kSigning = HmacSha256(kService, Terminator);

            return HexEncode(
                HmacSha256(kSigning, stringToSign));
        }

        private static string BuildCanonicalQueryString(
            string queryString)
        {
            if (string.IsNullOrEmpty(queryString))
            {
                return "";
            }

            string[] pairs = queryString.Split('&');
            Array.Sort(pairs, StringComparer.Ordinal);
            return string.Join("&", pairs);
        }

        private static string BuildListQuery(
            string prefix,
            int maxKeys,
            string continuationToken)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("list-type=2");

            if (!string.IsNullOrEmpty(prefix))
            {
                sb.Append("&prefix=")
                    .Append(Uri.EscapeDataString(prefix));
            }

            sb.Append("&max-keys=").Append(maxKeys);

            if (!string.IsNullOrEmpty(continuationToken))
            {
                sb.Append("&continuation-token=")
                    .Append(Uri.EscapeDataString(
                        continuationToken));
            }

            return sb.ToString();
        }

        #endregion

        #region Private — Crypto Helpers

        private static byte[] HmacSha256(
            byte[] key, string data)
        {
            return HmacSha256(
                key, Encoding.UTF8.GetBytes(data));
        }

        private static byte[] HmacSha256(
            byte[] key, byte[] data)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        private static string HexEncode(byte[] data)
        {
            StringBuilder sb
                = new StringBuilder(data.Length * 2);
            foreach (byte b in data)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        private static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data);
                return HexEncode(hash);
            }
        }

        private static string Sha256Hex(string data)
        {
            return Sha256Hex(
                Encoding.UTF8.GetBytes(data));
        }

        #endregion

        #region Private — URL & Path Helpers

        private static string BuildUrl(
            S3UploadSettings settings, string key)
        {
            string endpoint = settings.Endpoint.TrimEnd('/');

            string encodedKey = string.IsNullOrEmpty(key)
                ? ""
                : "/" + Uri.EscapeDataString(key)
                    .Replace("%2F", "/");

            return $"{endpoint}/{settings.Bucket}{encodedKey}";
        }

        private static string BuildHost(S3UploadSettings settings)
        {
            Uri uri = new Uri(settings.Endpoint);
            string host = uri.Host;
            if (!uri.IsDefaultPort)
            {
                host += ":" + uri.Port;
            }

            return host;
        }

        private static string CombineKey(
            string basePrefix, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(basePrefix))
            {
                return relativePath;
            }

            return basePrefix.TrimEnd('/')
                + "/" + relativePath.TrimStart('/');
        }

        private static string ComputeRelativePath(
            string filePath, string localDirectory)
        {
            return filePath
                .Substring(localDirectory.Length)
                .TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static string GetContentType(string filePath)
        {
            string ext = Path.GetExtension(filePath);

            if (string.IsNullOrEmpty(ext))
            {
                return "application/octet-stream";
            }

            string lower = ext.ToLowerInvariant();

            if (TextContentTypes.Contains(lower))
            {
                return MapTextContentType(lower);
            }

            return MapBinaryContentType(lower);
        }

        private static string MapTextContentType(string lowerExt)
        {
            switch (lowerExt)
            {
                case ".json":
                    return "application/json";
                case ".xml":
                    return "application/xml";
                case ".html":
                case ".htm":
                    return "text/html";
                case ".css":
                    return "text/css";
                case ".js":
                    return "application/javascript";
                case ".csv":
                    return "text/csv";
                case ".md":
                    return "text/markdown";
                default:
                    return "text/plain";
            }
        }

        private static string MapBinaryContentType(string lowerExt)
        {
            switch (lowerExt)
            {
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".svg":
                    return "image/svg+xml";
                case ".webp":
                    return "image/webp";
                case ".mp3":
                    return "audio/mpeg";
                case ".wav":
                    return "audio/wav";
                case ".ogg":
                    return "audio/ogg";
                case ".mp4":
                    return "video/mp4";
                case ".webm":
                    return "video/webm";
                case ".pdf":
                    return "application/pdf";
                case ".zip":
                    return "application/zip";
                case ".bin":
                case ".bytes":
                    return "application/octet-stream";
                default:
                    return "application/octet-stream";
            }
        }

        #endregion
    }
}
