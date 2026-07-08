// ═══════════════════════════════════════════════════════════
// ── AabUtility ──────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Converts an Android App Bundle (.aab) to an APK Set (.apks)
    /// and installs it to devices using Google's bundletool.
    /// </summary>
    internal static class AabUtility
    {
        // ══════════════════════════════════════════════════
        // ── Constants
        // ══════════════════════════════════════════════════

        private const string BundletoolPrefKey =
            "NoBuild_BundletoolPath";
        private const string JavaPrefKey =
            "NoBuild_JavaPath";
        private const string GithubApiUrl =
            "https://api.github.com/repos/google/bundletool"
            + "/releases/latest";
        private const string FallbackVersion = "1.18.3";
        private const string FallbackDownloadUrl =
            "https://github.com/google/bundletool/releases"
            + "/download/" + FallbackVersion
            + "/bundletool-all-" + FallbackVersion + ".jar";
        private const int ProcessTimeout = 300000;  // 5 min
        private const int WebTimeout = 30000;       // 30 sec

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Override the auto-detected java path.
        /// Set via EditorPrefs or assign programmatically.
        /// </summary>
        public static string CustomJavaPath
        {
            get => EditorPrefs.GetString(JavaPrefKey, "");
            set => EditorPrefs.SetString(JavaPrefKey, value ?? "");
        }

        /// <summary>
        /// Override the auto-detected bundletool.jar path.
        /// Set via EditorPrefs or assign programmatically.
        /// </summary>
        public static string CustomBundletoolPath
        {
            get => EditorPrefs.GetString(
                BundletoolPrefKey, "");
            set => EditorPrefs.SetString(
                BundletoolPrefKey, value ?? "");
        }

        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Converts an AAB file to an APKS file using
        /// bundletool build-apks.
        /// </summary>
        /// <param name="aabPath">
        /// Absolute path to the .aab file.
        /// </param>
        /// <param name="apksOutputPath">
        /// Destination path for the .apks file.
        /// If null, uses a temp file.
        /// </param>
        /// <returns>Path to the generated .apks file.</returns>
        public static string BuildApks(
            string aabPath, string apksOutputPath = null)
        {
            if (string.IsNullOrEmpty(aabPath))
                throw new ArgumentNullException(
                    nameof(aabPath));
            if (!File.Exists(aabPath))
                throw new FileNotFoundException(
                    $"AAB file not found: {aabPath}");

            apksOutputPath ??=
                GetTempApksPath(aabPath);

            string javaPath = ResolveJava();
            string bundletoolPath =
                ResolveBundletool();

            string args = BuildApksCommandArgs(
                aabPath, apksOutputPath);

            EditorUtility.DisplayProgressBar(
                "NoBuild — AAB Conversion",
                "Running bundletool build-apks...",
                0.5f);

            try
            {
                Debug.Log(
                    $"[NoBuild] build-apks: {javaPath} "
                    + $"-jar \"{bundletoolPath}\" {args}");

                string output = RunProcess(
                    javaPath,
                    $"-jar \"{bundletoolPath}\" {args}",
                    ProcessTimeout);

                if (!File.Exists(apksOutputPath))
                {
                    throw new InvalidOperationException(
                        "bundletool failed to generate "
                        + "APKS.\n\nOutput:\n"
                        + $"{Truncate(output, 2000)}");
                }

                Debug.Log(
                    $"[NoBuild] APKS generated: "
                    + $"{apksOutputPath} "
                    + $"({FormatFileSize(apksOutputPath)})");

                return apksOutputPath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "AAB to APKS conversion failed.\n\n"
                    + $"Error: {ex.Message}", ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Installs an APKS file to a specific device
        /// using bundletool install-apks.
        /// </summary>
        public static bool InstallApks(
            string apksPath, string deviceSerial)
        {
            if (string.IsNullOrEmpty(apksPath))
                throw new ArgumentNullException(
                    nameof(apksPath));
            if (string.IsNullOrEmpty(deviceSerial))
                throw new ArgumentNullException(
                    nameof(deviceSerial));
            if (!File.Exists(apksPath))
            {
                Debug.LogError(
                    $"[NoBuild] APKS not found: "
                    + $"{apksPath}");
                return false;
            }

            try
            {
                string javaPath = ResolveJava();
                string bundletoolPath =
                    ResolveBundletool();

                string args =
                    $"install-apks "
                    + $"--apks=\"{apksPath}\" "
                    + $"--device-id=\"{deviceSerial}\"";

                Debug.Log(
                    $"[NoBuild] Installing APKS to "
                    + $"{deviceSerial}...");

                var result = RunProcessVerbose(
                    javaPath,
                    $"-jar \"{bundletoolPath}\" {args}",
                    ProcessTimeout);

                string combined = result.stdout
                    + "\n" + result.stderr;

                bool success = result.exitCode == 0
                    || combined.Contains(
                        "The APKs have been installed")
                    || combined.Contains(
                        "Successfully");

                if (success)
                {
                    Debug.Log(
                        $"[NoBuild] APKS install "
                        + "succeeded on "
                        + $"{deviceSerial}");
                    return true;
                }

                Debug.LogError(
                    $"[NoBuild] APKS install failed on "
                    + $"{deviceSerial}"
                    + $" (exit={result.exitCode})"
                    + $":\n{Truncate(combined, 2000)}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[NoBuild] APKS install "
                    + $"exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes a temporary APKS file. Safe to call
        /// even if the file does not exist.
        /// </summary>
        public static void Cleanup(string apksPath)
        {
            if (string.IsNullOrEmpty(apksPath))
                return;
            try
            {
                if (File.Exists(apksPath))
                {
                    File.Delete(apksPath);
                    Debug.Log(
                        $"[NoBuild] Cleaned up: "
                        + $"{apksPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[NoBuild] Failed to cleanup "
                    + $"{apksPath}: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════
        // ── Private: Resolution
        // ══════════════════════════════════════════════════

        private static string ResolveJava()
        {
            // 1. Custom override
            string custom = CustomJavaPath;
            if (!string.IsNullOrEmpty(custom)
                && File.Exists(custom))
                return custom;
            if (!string.IsNullOrEmpty(custom))
                Debug.LogWarning(
                    "[NoBuild] Custom java path "
                    + $"invalid: {custom}");

            // 2. Unity bundled OpenJDK
            string bundled =
                FindUnityBundledJava();
            if (!string.IsNullOrEmpty(bundled))
                return bundled;

            // 3. JAVA_HOME
            string javaHome =
                Environment.GetEnvironmentVariable(
                    "JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string javaBin = Path.Combine(
                    javaHome, "bin",
                    GetPlatformExeName("java"));
                if (File.Exists(javaBin))
                    return javaBin;
            }

            // 4. System PATH
            return GetPlatformExeName("java");
        }

        private static string ResolveBundletool()
        {
            // 1. Custom override
            string custom = CustomBundletoolPath;
            if (!string.IsNullOrEmpty(custom)
                && File.Exists(custom))
                return custom;
            if (!string.IsNullOrEmpty(custom))
                Debug.LogWarning(
                    "[NoBuild] Custom bundletool "
                    + $"path invalid: {custom}");

            // 2. Cached in Library/NoBuild
            string cached = FindCachedBundletool();
            if (!string.IsNullOrEmpty(cached))
                return cached;

            // 3. Unity SDK locations
            string sdk = FindBundletoolInSdk();
            if (!string.IsNullOrEmpty(sdk))
                return sdk;

            // 4. Auto-download (GitHub → fallback)
            return DownloadBundletool();
        }

        // ══════════════════════════════════════════════════
        // ── Private: Java Discovery
        // ══════════════════════════════════════════════════

        private static string FindUnityBundledJava()
        {
            string contentsPath =
                EditorApplication
                    .applicationContentsPath;
            string javaDir = Path.Combine(
                contentsPath,
                "PlaybackEngines",
                "AndroidPlayer",
                "OpenJDK",
                "bin");
            string java = Path.Combine(
                javaDir,
                GetPlatformExeName("java"));
            return File.Exists(java) ? java : null;
        }

        // ══════════════════════════════════════════════════
        // ── Private: Bundletool Discovery
        // ══════════════════════════════════════════════════

        private static string FindCachedBundletool()
        {
            string cacheDir = GetNoBuildCacheDir();
            if (!Directory.Exists(cacheDir))
                return null;

            string[] jars = Directory.GetFiles(
                cacheDir, "bundletool-*.jar",
                SearchOption.TopDirectoryOnly);

            foreach (string jar in jars)
            {
                if (File.Exists(jar))
                    return jar;
            }
            return null;
        }

        private static string FindBundletoolInSdk()
        {
            string contentsPath =
                EditorApplication
                    .applicationContentsPath;
            string[] candidates =
            {
                Path.Combine(contentsPath,
                    "PlaybackEngines",
                    "AndroidPlayer",
                    "SDK", "tools", "lib",
                    "bundletool.jar"),
                Path.Combine(contentsPath,
                    "PlaybackEngines",
                    "AndroidPlayer",
                    "SDK", "cmdline-tools",
                    "latest", "lib",
                    "bundletool.jar"),
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        private static string DownloadBundletool()
        {
            EditorUtility.DisplayProgressBar(
                "NoBuild",
                "Fetching latest bundletool release...",
                0.1f);

            string downloadUrl = null;
            string version = null;

            try
            {
                // Try GitHub API first
                GitHubRelease release =
                    QueryGitHubRelease();
                if (release != null
                    && release.jarUrl != null)
                {
                    downloadUrl = release.jarUrl;
                    version = release.tagName;
                    Debug.Log(
                        $"[NoBuild] Latest bundletool: "
                        + $"{version} from GitHub");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[NoBuild] GitHub API query failed: "
                    + $"{ex.Message}");
            }

            // Fallback to hardcoded URL
            if (downloadUrl == null)
            {
                downloadUrl = FallbackDownloadUrl;
                version = FallbackVersion;
                Debug.Log(
                    $"[NoBuild] Using fallback bundletool: "
                    + $"{version}");
            }

            string cacheDir = GetNoBuildCacheDir();
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);

            string destPath = Path.Combine(
                cacheDir,
                $"bundletool-all-{version}.jar");

            // If already downloaded, skip
            if (File.Exists(destPath))
            {
                Debug.Log(
                    $"[NoBuild] bundletool already "
                    + $"cached: {destPath}");
                EditorUtility.ClearProgressBar();
                return destPath;
            }

            EditorUtility.DisplayProgressBar(
                "NoBuild",
                "Downloading bundletool.jar "
                + "(~30 MB)...",
                0.3f);

            try
            {
                Debug.Log(
                    $"[NoBuild] Downloading: "
                    + $"{downloadUrl}");

                using var client = new WebClient();
                client.Headers.Add(
                    HttpRequestHeader.UserAgent,
                    "NoBuild-Unity");
                client.DownloadFile(
                    downloadUrl, destPath);

                if (!File.Exists(destPath))
                {
                    throw new InvalidOperationException(
                        "Download completed but file "
                        + "not found at: "
                        + $"{destPath}");
                }

                Debug.Log(
                    $"[NoBuild] bundletool downloaded: "
                    + $"{destPath} "
                    + $"({FormatFileSize(destPath)})");

                return destPath;
            }
            catch (Exception ex)
            {
                string msg =
                    "Failed to download "
                    + "bundletool.jar.\n\n"
                    + "Check your internet "
                    + "connection and firewall "
                    + "settings.\n\n"
                    + $"Error: {ex.Message}\n\n"
                    + "Download manually from:\n"
                    + "https://github.com/google/"
                    + "bundletool/releases\n\n"
                    + "Then set via EditorPrefs:\n"
                    + "\"NoBuild_BundletoolPath\"";

                EditorUtility.DisplayDialog(
                    "NoBuild — Download Failed",
                    msg, "OK");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static GitHubRelease QueryGitHubRelease()
        {
            try
            {
                var request = WebRequest.Create(
                    GithubApiUrl) as HttpWebRequest;
                if (request == null) return null;

                request.Method = "GET";
                request.UserAgent = "NoBuild-Unity";
                request.Accept =
                    "application/vnd.github+json";
                request.Timeout = WebTimeout;

                using var response = request.GetResponse()
                    as HttpWebResponse;
                if (response == null) return null;

                using var reader = new StreamReader(
                    response.GetResponseStream(),
                    Encoding.UTF8);

                string json = reader.ReadToEnd();
                return ParseGitHubRelease(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[NoBuild] GitHub release query "
                    + $"failed: {ex.Message}");
                return null;
            }
        }

        private static GitHubRelease ParseGitHubRelease(
            string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            // Simple manual JSON parsing to avoid
            // JsonUtility limitations with nested arrays
            string tag = ExtractJsonString(
                json, "\"tag_name\"");
            if (tag == null) return null;

            string jarUrl = null;

            // Find the JAR asset URL
            int assetsIdx = json.IndexOf(
                "\"assets\"",
                StringComparison.Ordinal);
            if (assetsIdx < 0) return null;

            // Search within assets array
            string remaining = json.Substring(
                assetsIdx);
            int nameIdx = 0;
            while ((nameIdx = remaining.IndexOf(
                "\"name\"",
                nameIdx,
                StringComparison.Ordinal)) >= 0)
            {
                string name = ExtractJsonString(
                    remaining, "\"name\"", nameIdx);
                if (name != null
                    && name.Contains("-all")
                    && name.EndsWith(".jar"))
                {
                    jarUrl = ExtractJsonString(
                        remaining,
                        "\"browser_download_url\"",
                        nameIdx);
                    break;
                }
                nameIdx++;
            }

            if (jarUrl == null) return null;

            return new GitHubRelease
            {
                tagName = tag,
                jarUrl = jarUrl
            };
        }

        /// <summary>
        /// Extracts a JSON string value for a given key
        /// from a position or the start of the string.
        /// </summary>
        private static string ExtractJsonString(
            string json, string key,
            int startIndex = 0)
        {
            int keyIdx = json.IndexOf(
                key, startIndex,
                StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            // Skip key + colon + optional whitespace
            int valStart = json.IndexOf(
                '"', keyIdx + key.Length);
            if (valStart < 0) return null;

            int valEnd = json.IndexOf(
                '"', valStart + 1);
            if (valEnd < 0) return null;

            return json.Substring(
                valStart + 1,
                valEnd - valStart - 1);
        }

        // ══════════════════════════════════════════════════
        // ── Private: Command Building
        // ══════════════════════════════════════════════════

        private static string BuildApksCommandArgs(
            string aabPath, string apksOutputPath)
        {
            var sb = new StringBuilder();
            sb.Append("build-apks ");
            sb.Append(
                $"--bundle=\"{aabPath}\" ");
            sb.Append(
                $"--output=\"{apksOutputPath}\"");

            AppendSigningArgs(sb);
            return sb.ToString();
        }

        private static void AppendSigningArgs(
            StringBuilder sb)
        {
            string keystoreName =
                PlayerSettings.Android.keystoreName;
            string keystorePass =
                PlayerSettings.Android.keystorePass;
            string keyaliasName =
                PlayerSettings.Android.keyaliasName;
            string keyaliasPass =
                PlayerSettings.Android.keyaliasPass;

            string resolved =
                ResolveKeystorePath(keystoreName);
            if (string.IsNullOrEmpty(resolved))
                return;

            sb.Append($" --ks=\"{resolved}\"");

            if (!string.IsNullOrEmpty(keystorePass))
                sb.Append(
                    " --ks-pass=pass:"
                    + keystorePass);

            if (!string.IsNullOrEmpty(keyaliasName))
                sb.Append(
                    " --ks-key-alias="
                    + keyaliasName);

            if (!string.IsNullOrEmpty(keyaliasPass))
                sb.Append(
                    " --key-pass=pass:"
                    + keyaliasPass);
        }

        private static string ResolveKeystorePath(
            string keystoreName)
        {
            if (!string.IsNullOrEmpty(keystoreName))
                return keystoreName;

            // Fallback to default debug keystore
            string home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            string debugKeystore = Path.Combine(
                home, ".android", "debug.keystore");
            return File.Exists(debugKeystore)
                ? debugKeystore : null;
        }

        // ══════════════════════════════════════════════════
        // ── Private: Process Execution
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Runs a process and returns its stdout.
        /// Throws if exit code is non-zero.
        /// </summary>
        private static string RunProcess(
            string fileName, string arguments,
            int timeoutMs)
        {
            var result = RunProcessVerbose(
                fileName, arguments, timeoutMs);

            if (result.exitCode != 0)
            {
                throw new InvalidOperationException(
                    "Process exited with code "
                    + $"{result.exitCode}.\n"
                    + "stderr:\n"
                    + $"{Truncate(result.stderr, 1000)}");
            }

            return result.stdout;
        }

        /// <summary>
        /// Runs a process and returns both stdout, stderr,
        /// and exit code. Never throws on non-zero exit.
        /// </summary>
        private static ProcessResult RunProcessVerbose(
            string fileName, string arguments,
            int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding =
                    Encoding.UTF8,
                StandardErrorEncoding =
                    Encoding.UTF8
            };

            using var process =
                Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException(
                    "Failed to start process: "
                    + $"{fileName}");
            }

            string stdout =
                process.StandardOutput.ReadToEnd();
            string stderr =
                process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); }
                catch { /* best effort */ }
                throw new TimeoutException(
                    $"Process timed out after "
                    + $"{timeoutMs}ms: "
                    + $"{fileName} {arguments}");
            }

            return new ProcessResult
            {
                stdout = stdout ?? "",
                stderr = stderr ?? "",
                exitCode = process.ExitCode
            };
        }

        private struct ProcessResult
        {
            public string stdout;
            public string stderr;
            public int exitCode;
        }

        // ══════════════════════════════════════════════════
        // ── Private: Helpers
        // ══════════════════════════════════════════════════

        private static string GetTempApksPath(
            string aabPath)
        {
            string baseName =
                Path.GetFileNameWithoutExtension(
                    aabPath);
            return Path.Combine(
                Path.GetTempPath(),
                "NoBuild",
                $"{baseName}.apks");
        }

        /// <summary>
        /// Persistent cache directory under the Unity
        /// project's Library folder.
        /// </summary>
        private static string GetNoBuildCacheDir()
        {
            string projectRoot =
                Directory.GetParent(
                    Application.dataPath)
                .FullName;
            return Path.Combine(
                projectRoot,
                "Library",
                "NoBuild");
        }

        private static string GetPlatformExeName(
            string baseName)
        {
#if UNITY_EDITOR_WIN
            return baseName + ".exe";
#else
            return baseName;
#endif
        }

        private static string Truncate(
            string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength)
                    + "...";
        }

        private static string FormatFileSize(
            string path)
        {
            try
            {
                long bytes =
                    new FileInfo(path).Length;
                if (bytes >= 1024 * 1024)
                    return
                        $"{(bytes / (1024f * 1024f)):F1}"
                        + " MB";
                if (bytes >= 1024)
                    return
                        $"{(bytes / 1024f):F1}"
                        + " KB";
                return $"{bytes} B";
            }
            catch { return "? B"; }
        }

        // ══════════════════════════════════════════════════
        // ── Nested Types
        // ══════════════════════════════════════════════════

        private sealed class GitHubRelease
        {
            public string tagName;
            public string jarUrl;
        }
    }
}
