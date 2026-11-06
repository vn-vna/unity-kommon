using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    [InitializeOnLoad]
    internal static class NoBuildDeviceOperations
    {
        private static CancellationTokenSource _cancellation;
        public static bool IsBusy => _cancellation != null;
        public static string Status { get; private set; } = "";
        public static string RememberedDeviceKey => "NoBuild.LastDevice." + UnityEngine.Application.dataPath;

        static NoBuildDeviceOperations()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
        }

        public static void Cancel() => _cancellation?.Cancel();

        public static void SetStatus(string status)
        {
            Status = status;
            NoBuildToolbarState.RequestRepaint();
        }

        public static void Start(string status, Func<CancellationToken, Task> operation)
        {
            if (IsBusy) return;
            _cancellation = new CancellationTokenSource();
            SetStatus(status);
            _ = RunAsync(operation, _cancellation);
        }

        private static async Task RunAsync(Func<CancellationToken, Task> operation, CancellationTokenSource cancellation)
        {
            try
            {
                await operation(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                NoBuildEditorState.SetResult("Device operation cancelled.");
            }
            catch (Exception exception)
            {
                NoBuildEditorState.SetResult("Device operation failed: " + exception.Message, failed: true);
            }
            finally
            {
                cancellation.Dispose();
                _cancellation = null;
                Status = "";
                AdbDeviceCache.Refresh();
                NoBuildToolbarState.RequestRepaint();
            }
        }

        public static void InspectArtifact(string path, bool aab, Action<string> inspected)
        {
            Start("Reading artifact package" + (aab ? " (preparing bundletool if needed)…" : "…"), async token =>
            {
                string package = aab ? await AabUtility.GetPackageNameAsync(path, token)
                    : await AdbUtility.GetApkPackageAsync(path, token);
                token.ThrowIfCancellationRequested();
                inspected(package);
                NoBuildEditorState.SetResult($"Selected {Path.GetFileName(path)} · {package}");
            });
        }

        public static void Install(string path, string package, string serial, string deviceName, bool aab)
        {
            Start("Preparing install…", async token =>
            {
                string apks = null;
                try
                {
                    string actualPackage = aab ? await AabUtility.GetPackageNameAsync(path, token)
                        : await AdbUtility.GetApkPackageAsync(path, token);
                    if (!string.Equals(actualPackage, package, StringComparison.Ordinal))
                        throw new InvalidOperationException("Artifact identity changed. Select and inspect the artifact again before installing.");
                    if (aab)
                    {
                        SetStatus("Converting AAB to APKS…");
                        apks = await AabUtility.BuildApksAsync(path, token);
                    }
                    SetStatus($"Installing {Path.GetFileName(path)} to {deviceName}…");
                    bool installed = await DeviceInstaller.InstallWithRecoveryAsync(serial, deviceName, package,
                        () => aab ? AabUtility.InstallApksAsync(apks, serial, token)
                            : AdbUtility.InstallApkAsync(path, serial, token), token);
                    if (installed)
                    {
                        EditorPrefs.SetString(RememberedDeviceKey, serial);
                        NoBuildEditorState.SetResult($"Installed {package} on {deviceName}");
                    }
                    else NoBuildEditorState.SetResult($"Installation cancelled for {deviceName}.");
                }
                finally
                {
                    if (apks != null) AabUtility.Cleanup(apks);
                }
            });
        }

        public static void Launch(string serial, string package)
        {
            Start("Launching " + package + "…", async token =>
            {
                ProcessExecutionResult result = await AdbUtility.RunAsync(
                    $"-s \"{serial}\" shell monkey -p \"{package}\" -c android.intent.category.LAUNCHER 1", cancellationToken: token);
                if (result.TimedOut || result.ExitCode != 0 || result.CombinedOutput.Contains("Error"))
                    throw new InvalidOperationException(result.CombinedOutput);
                EditorPrefs.SetString(RememberedDeviceKey, serial);
                NoBuildEditorState.SetResult($"Launched {package} on {serial}");
            });
        }
    }
}
