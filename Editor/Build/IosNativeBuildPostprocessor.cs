#if UNITY_IOS
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Com.Scheherazade.Editor.Build
{
    internal static class IosNativeBuildPostprocessor
    {
        private const string CoreHapticsFramework = "CoreHaptics.framework";
        private const string LofeltFrameworkDirectory = "LofeltHaptics.framework";
        private const string LofeltBinaryName = "LofeltHaptics";

        [PostProcessBuild(1000)]
        private static void ProcessIosBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            AddCoreHapticsFramework(buildPath);
            StripLofeltBitcode(buildPath);
        }

        private static void AddCoreHapticsFramework(string buildPath)
        {
            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string frameworkTarget = project.GetUnityFrameworkTargetGuid();
            if (!project.ContainsFramework(frameworkTarget, CoreHapticsFramework))
            {
                project.AddFrameworkToProject(frameworkTarget, CoreHapticsFramework, false);
                project.WriteToFile(projectPath);
            }
        }

        private static void StripLofeltBitcode(string buildPath)
        {
            string[] candidates = Directory.GetFiles(
                buildPath,
                LofeltBinaryName,
                SearchOption.AllDirectories
            );

            foreach (string binaryPath in candidates)
            {
                DirectoryInfo parent = Directory.GetParent(binaryPath);
                if (parent == null || parent.Name != LofeltFrameworkDirectory) continue;

                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/xcrun",
                    Arguments = $"bitcode_strip -r {Quote(binaryPath)} -o {Quote(binaryPath)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(startInfo);
                if (process == null)
                    throw new BuildFailedException("Failed to start xcrun bitcode_strip for LofeltHaptics.");

                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new BuildFailedException(
                        $"Failed to strip legacy bitcode from '{binaryPath}': {standardError}"
                    );
                }
            }
        }

        private static string Quote(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }
    }
}
#endif
