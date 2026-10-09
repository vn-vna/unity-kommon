// ═══════════════════════════════════════════════════════════
// ── GitUtility ────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Retrieves git metadata by shelling out to the <c>git</c> CLI.
    /// Results are cached for 30 seconds. Gracefully returns <c>"unknown"</c>
    /// if git is unavailable or the project is not a git repository.
    /// </summary>
    [InitializeOnLoad]
    internal static class GitUtility
    {
        // ══════════════════════════════════════════════════
        // ── Constants
        // ══════════════════════════════════════════════════

        private const string FallbackValue = "unknown";
        private const int CacheTtlMs = 30_000;
        private const int ProcessTimeoutMs = 5_000;

        // ══════════════════════════════════════════════════
        // ── Private Fields
        // ══════════════════════════════════════════════════

        private static string _cachedShortHash;
        private static string _cachedFullHash;
        private static string _cachedBranch;
        private static DateTime _lastFetchTime = DateTime.MinValue;

        private static readonly Dictionary<string, SubmoduleGitInfo>
            _submoduleCache = new(StringComparer.OrdinalIgnoreCase);

        private static Task<string[]> _rootFetch;
        private static readonly Dictionary<string, Task<SubmoduleGitInfo>> _submoduleFetches = new();
        private static readonly CancellationTokenSource _lifetime = new();
        public static bool IsRefreshing => _rootFetch != null || _submoduleFetches.Count > 0;

        static GitUtility()
        {
            EditorApplication.update += HandleFetches;
            AssemblyReloadEvents.beforeAssemblyReload += () => _lifetime.Cancel();
            EditorApplication.quitting += () => _lifetime.Cancel();
        }

        private static void HandleFetches()
        {
            if (_rootFetch == null && _submoduleFetches.Count == 0) return;
            bool changed = false;
            if (_rootFetch?.IsCompleted == true)
            {
                try
                {
                    string[] values = _rootFetch.GetAwaiter().GetResult();
                    _cachedShortHash = values[0]; _cachedFullHash = values[1]; _cachedBranch = values[2];
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    SetAllToFallback();
                    Debug.LogWarning("[NoBuild] Git metadata unavailable: " + exception.Message);
                }
                _rootFetch = null;
                changed = true;
            }
            foreach (string key in new List<string>(_submoduleFetches.Keys))
            {
                Task<SubmoduleGitInfo> task = _submoduleFetches[key];
                if (!task.IsCompleted) continue;
                try
                {
                    _submoduleCache[key] = task.GetAwaiter().GetResult() ?? new SubmoduleGitInfo
                    {
                        shortHash = FallbackValue, fullHash = FallbackValue, fetchTime = DateTime.UtcNow
                    };
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    _submoduleCache[key] = new SubmoduleGitInfo
                    {
                        shortHash = FallbackValue, fullHash = FallbackValue, fetchTime = DateTime.UtcNow
                    };
                    Debug.LogWarning($"[NoBuild] Submodule '{key}' unavailable: {exception.Message}");
                }
                _submoduleFetches.Remove(key);
                changed = true;
            }
            if (changed) NoBuildToolbarState.RequestRepaint();
        }

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public static string ShortCommitHash
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedShortHash ?? FallbackValue;
            }
        }

        public static string FullCommitHash
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedFullHash ?? FallbackValue;
            }
        }

        public static string BranchName
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedBranch ?? FallbackValue;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Submodule Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Returns the short commit hash of a git submodule.
        /// </summary>
        public static string GetSubmoduleCommit(string submoduleName)
        {
            try
            {
                SubmoduleGitInfo info =
                    ResolveSubmoduleInfo(submoduleName);
                return info?.shortHash ?? FallbackValue;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NoBuild] Could not resolve git submodule "
                    + $"'{submoduleName}': {exception.Message}");
                return FallbackValue;
            }
        }

        /// <summary>
        /// Returns the full commit hash of a git submodule.
        /// </summary>
        public static string GetSubmoduleFullCommit(
            string submoduleName)
        {
            try
            {
                SubmoduleGitInfo info =
                    ResolveSubmoduleInfo(submoduleName);
                return info?.fullHash ?? FallbackValue;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NoBuild] Could not resolve git submodule "
                    + $"'{submoduleName}': {exception.Message}");
                return FallbackValue;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Private Methods
        // ══════════════════════════════════════════════════

        private static void RefreshCacheIfNeeded()
        {
            if (_rootFetch != null || (DateTime.UtcNow - _lastFetchTime).TotalMilliseconds < CacheTtlMs) return;
            _lastFetchTime = DateTime.UtcNow;
            string projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectPath)) { SetAllToFallback(); return; }
            _rootFetch = Task.Run(() => new[]
            {
                RunGitCommand(projectPath, "rev-parse --short HEAD"),
                RunGitCommand(projectPath, "rev-parse HEAD"),
                RunGitCommand(projectPath, "rev-parse --abbrev-ref HEAD")
            }, _lifetime.Token);
        }

        private static void SetAllToFallback()
        {
            _cachedShortHash = FallbackValue;
            _cachedFullHash  = FallbackValue;
            _cachedBranch    = FallbackValue;
        }

        private static string RunGitCommand(
            string workingDirectory,
            string arguments)
        {
            ProcessExecutionResult result =
                NoBuildProcessRunner.Run(
                    "git",
                    arguments,
                    ProcessTimeoutMs,
                    workingDirectory, _lifetime.Token);
            if (result.TimedOut)
            {
                throw new TimeoutException(
                    $"Git command timed out after "
                    + $"{ProcessTimeoutMs}ms.");
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git exited with code {result.ExitCode}: "
                    + result.StandardError);
            }

            string output = result.StandardOutput.Trim();

            // git rev-parse --abbrev-ref HEAD returns
            // "HEAD" in detached state.
            if (arguments.Contains("abbrev-ref")
                && output == "HEAD")
            {
                output = RunGitCommand(
                    workingDirectory,
                    "rev-parse --short HEAD");
            }

            return string.IsNullOrEmpty(output)
                ? FallbackValue
                : output;
        }

        private static SubmoduleGitInfo ResolveSubmoduleInfo(
            string submoduleName)
        {
            if (string.IsNullOrEmpty(submoduleName))
                return null;

            _submoduleCache.TryGetValue(submoduleName, out SubmoduleGitInfo cached);
            if (cached != null && (DateTime.UtcNow - cached.fetchTime).TotalMilliseconds < CacheTtlMs) return cached;
            if (_submoduleFetches.ContainsKey(submoduleName)) return cached;
            string projectRoot =
                System.IO.Path.GetDirectoryName(
                    Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            _submoduleFetches[submoduleName] = Task.Run(() => FetchSubmodule(submoduleName, projectRoot), _lifetime.Token);
            return cached;
        }

        private static SubmoduleGitInfo FetchSubmodule(string submoduleName, string projectRoot)
        {

            string subPath = RunGitCommand(
                projectRoot,
                "config --file .gitmodules --get "
                + $"submodule.{submoduleName}.path");
            if (subPath == FallbackValue)
                return null;

            string fullPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    projectRoot, NormalizePath(subPath)));

            if (!System.IO.Directory.Exists(fullPath))
                return null;

            string shortHash = RunGitCommand(
                fullPath, "rev-parse --short HEAD");
            string fullHash = RunGitCommand(
                fullPath, "rev-parse HEAD");

            SubmoduleGitInfo info = new()
            {
                shortHash = shortHash,
                fullHash  = fullHash,
                fetchTime = DateTime.UtcNow
            };

            return info;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? "").Replace(
                '/', System.IO.Path.DirectorySeparatorChar);
        }

        // ══════════════════════════════════════════════════
        // ── Nested Types
        // ══════════════════════════════════════════════════

        private sealed class SubmoduleGitInfo
        {
            public string shortHash;
            public string fullHash;
            public DateTime fetchTime;
        }
    }
}
