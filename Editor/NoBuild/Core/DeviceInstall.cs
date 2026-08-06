// ═══════════════════════════════════════════════════════════
// ── DeviceInstall ──────────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Categorizes an install failure so the recovery dialog can
    /// describe it in human-friendly terms.
    /// </summary>
    internal enum InstallFailureKind
    {
        None,
        VersionDowngrade,
        UserCancelled,
        UpdateIncompatible,
        AlreadyExists,
        Other
    }

    /// <summary>
    /// Result of a single install attempt.
    /// </summary>
    internal struct InstallResult
    {
        public bool Success;
        public InstallFailureKind FailureKind;
        public string Output;
    }

    /// <summary>
    /// Installs apps to ADB devices with an interactive recovery
    /// loop. On any install failure the user is asked whether to
    /// force install (uninstall then install), retry, or cancel.
    /// </summary>
    internal static class DeviceInstaller
    {
        // ══════════════════════════════════════════════════
        // ── Public Methods
        // ══════════════════════════════════════════════════

        /// <summary>
        /// Attempts an install and, on failure, prompts the user
        /// with Force Install / Retry / Cancel. Returns true only
        /// if the app ended up installed.
        /// </summary>
        public static bool InstallWithRecovery(
            string deviceSerial,
            string deviceDisplayName,
            string packageName,
            Func<InstallResult> tryInstall)
        {
            if (tryInstall == null)
                throw new ArgumentNullException(nameof(tryInstall));

            while (true)
            {
                InstallResult result = tryInstall();
                if (result.Success)
                {
                    return true;
                }

                int choice = EditorUtility.DisplayDialogComplex(
                    "NoBuild — Install Failed",
                    BuildFailureMessage(
                        deviceDisplayName, result),
                    "Force Install",
                    "Cancel",
                    "Retry");

                if (choice == 0)
                {
                    // Force Install: remove the current version
                    // first, then re-attempt.
                    if (!AdbUtility.UninstallApp(
                            deviceSerial, packageName))
                    {
                        Debug.LogWarning(
                            $"[NoBuild] Uninstall of "
                            + $"'{packageName}' on "
                            + $"{deviceSerial} failed or the "
                            + "app is not installed; "
                            + "continuing with install.");
                    }

                    continue;
                }

                if (choice == 2)
                {
                    // Retry: re-attempt the install as-is.
                    continue;
                }

                // Cancel
                return false;
            }
        }

        /// <summary>
        /// Classifies raw adb/bundletool output into a
        /// <see cref="InstallFailureKind"/>.
        /// </summary>
        public static InstallFailureKind ClassifyInstallFailure(
            string output)
        {
            if (string.IsNullOrEmpty(output))
                return InstallFailureKind.Other;

            if (output.Contains(
                    "INSTALL_FAILED_VERSION_DOWNGRADE"))
                return InstallFailureKind.VersionDowngrade;

            if (output.Contains(
                    "INSTALL_FAILED_USER_CANCELLED")
                || output.Contains(
                    "user cancelled")
                || output.Contains(
                    "user canceled"))
                return InstallFailureKind.UserCancelled;

            if (output.Contains(
                    "INSTALL_FAILED_UPDATE_INCOMPATIBLE"))
                return InstallFailureKind.UpdateIncompatible;

            if (output.Contains(
                    "INSTALL_FAILED_ALREADY_EXISTS"))
                return InstallFailureKind.AlreadyExists;

            return InstallFailureKind.Other;
        }

        // ══════════════════════════════════════════════════
        // ── Private Methods
        // ══════════════════════════════════════════════════

        private static string BuildFailureMessage(
            string deviceDisplayName, InstallResult result)
        {
            string device = string.IsNullOrEmpty(
                deviceDisplayName)
                ? "Device"
                : deviceDisplayName;

            string reason = DescribeFailure(
                result.FailureKind);
            string snippet = Truncate(result.Output, 300);

            string message =
                $"Install to {device} failed.\n\n"
                + $"{reason}";

            if (!string.IsNullOrEmpty(snippet))
            {
                message += $"\n\n{snippet}";
            }

            message +=
                "\n\nWhat would you like to do?"
                + "\n• Force Install — uninstalls the "
                + "current version, then installs."
                + "\n• Retry — tries the install again."
                + "\n• Cancel — aborts the action.";

            return message;
        }

        private static string DescribeFailure(
            InstallFailureKind kind)
        {
            switch (kind)
            {
                case InstallFailureKind.VersionDowngrade:
                    return "The app version on the device "
                        + "is newer than the build being "
                        + "installed (downgrade detected).";
                case InstallFailureKind.UserCancelled:
                    return "The installation was cancelled "
                        + "on the device.";
                case InstallFailureKind.UpdateIncompatible:
                    return "The installed app is "
                        + "incompatible with this build "
                        + "(e.g. different signature).";
                case InstallFailureKind.AlreadyExists:
                    return "The app is already installed.";
                default:
                    return "The install failed for an "
                        + "unknown reason.";
            }
        }

        private static string Truncate(
            string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            if (value.Length <= maxLength)
                return value;

            string cleaned = value.Trim();
            return cleaned.Length <= maxLength
                ? cleaned
                : cleaned.Substring(0, maxLength) + "...";
        }
    }
}
