// ═══════════════════════════════════════════════════════════
// ── GitUtility ────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Retrieves git metadata by shelling out to the <c>git</c> CLI.
    /// Results are cached for 30 seconds. Gracefully returns <c>"unknown"</c>
    /// if git is unavailable or the project is not a git repository.
    /// </summary>
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
        private static bool _gitAvailable = true;
        private static readonly object _lock = new();

        // ══════════════════════════════════════════════════
        // ── Properties
        // ══════════════════════════════════════════════════

        public static string ShortCommitHash
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedShortHash;
            }
        }

        public static string FullCommitHash
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedFullHash;
            }
        }

        public static string BranchName
        {
            get
            {
                RefreshCacheIfNeeded();
                return _cachedBranch;
            }
        }

        // ══════════════════════════════════════════════════
        // ── Private Methods
        // ══════════════════════════════════════════════════

        private static void RefreshCacheIfNeeded()
        {
            lock (_lock)
            {
                if ((DateTime.UtcNow - _lastFetchTime).TotalMilliseconds < CacheTtlMs)
                {
                    return;
                }

                _lastFetchTime = DateTime.UtcNow;

                if (!_gitAvailable)
                {
                    SetAllToFallback();
                    return;
                }

                // Project root is one level above Application.dataPath (Assets/)
                string projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(projectPath))
                {
                    _gitAvailable = false;
                    SetAllToFallback();
                    return;
                }

                try
                {
                    _cachedShortHash = RunGitCommand(projectPath, "rev-parse --short HEAD");
                    _cachedFullHash  = RunGitCommand(projectPath, "rev-parse HEAD");
                    _cachedBranch    = RunGitCommand(projectPath, "rev-parse --abbrev-ref HEAD");
                }
                catch (Exception ex)
                {
                    _gitAvailable = false;
                    SetAllToFallback();
                    Debug.LogWarning(
                        $"[NoBuild] GitUtility failed to retrieve git info: {ex.Message}"
                    );
                }
            }
        }

        private static void SetAllToFallback()
        {
            _cachedShortHash = FallbackValue;
            _cachedFullHash  = FallbackValue;
            _cachedBranch    = FallbackValue;
        }

        private static string RunGitCommand(string workingDirectory, string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName               = "git",
                Arguments              = arguments,
                WorkingDirectory       = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process.");
            }

            process.WaitForExit(ProcessTimeoutMs);

            if (!process.HasExited)
            {
                process.Kill();
                throw new TimeoutException($"Git command timed out after {ProcessTimeoutMs}ms.");
            }

            string output = process.StandardOutput.ReadToEnd().Trim();

            // git rev-parse --abbrev-ref HEAD returns "HEAD" in detached state
            if (arguments.Contains("abbrev-ref") && output == "HEAD")
            {
                // Try to get tag or commit for detached HEAD
                output = RunGitCommand(workingDirectory, "rev-parse --short HEAD");
            }

            return string.IsNullOrEmpty(output) ? FallbackValue : output;
        }
    }
}
