using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Com.Hapiga.Scheherazade.Common.ROAP.Editor
{
    public sealed class RoapDeviceInfo
    {
        public string Serial { get; set; }
        public string State { get; set; }
        public string Description { get; set; }

        public bool IsReady => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);

        public string DisplayName
        {
            get
            {
                string suffix = string.IsNullOrWhiteSpace(Description)
                    ? State
                    : $"{State} - {Description}";
                return $"{Serial} ({suffix})";
            }
        }
    }

    public sealed class RoapLauncherInfo
    {
        public string ComponentName { get; set; }
        public string ActivityName { get; set; }
        public bool IsResolvedDefault { get; set; }

        public string DisplayName => IsResolvedDefault
            ? $"{ActivityName} (default)"
            : ActivityName;
    }

    internal sealed class RoapProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
    }

    public static class RoapAdbClient
    {
        private const string AndroidSdkRootEditorPrefsKey = "AndroidSdkRoot";
        private const int DefaultTimeoutMilliseconds = 15000;
        private const string IntentActionMain = "android.intent.action.MAIN";
        private const string IntentCategoryLauncher = "android.intent.category.LAUNCHER";
        private static readonly Regex ComponentRegex = new Regex(
            @"(?<package>[A-Za-z0-9._$-]+)\/(?<activity>[A-Za-z0-9._$-]+)",
            RegexOptions.Compiled
        );

        public static string GetDefaultPackageId()
        {
            return PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        }

        public static string GetAdbPathForDisplay()
        {
            return TryResolveAdbPath(out string adbPath)
                ? adbPath
                : "adb not found";
        }

        public static List<RoapDeviceInfo> GetDevices()
        {
            RoapProcessResult result = RunAdbCommand(
                new[]
                {
                    "devices",
                    "-l",
                }
            );

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(GetFailureMessage("list adb devices", result));
            }

            List<RoapDeviceInfo> devices = new List<RoapDeviceInfo>();
            string[] lines = SplitLines(result.StandardOutput);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] segments = SplitByWhitespace(line, 3);
                if (segments.Length < 2)
                {
                    continue;
                }

                devices.Add(
                    new RoapDeviceInfo
                    {
                        Serial = segments[0],
                        State = segments[1],
                        Description = segments.Length >= 3 ? segments[2] : string.Empty,
                    }
                );
            }

            return devices;
        }

        public static List<RoapLauncherInfo> GetLaunchers(string deviceSerial, string packageId)
        {
            if (string.IsNullOrWhiteSpace(deviceSerial))
            {
                throw new InvalidOperationException("Select an Android device before refreshing launchers.");
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new InvalidOperationException("Enter an Android package id before refreshing launchers.");
            }

            string normalizedPackageId = packageId.Trim();
            string resolvedDefault = TryResolveDefaultLauncher(deviceSerial, normalizedPackageId);
            HashSet<string> components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string component in QueryLauncherComponents(deviceSerial, normalizedPackageId))
            {
                components.Add(component);
            }

            if (!string.IsNullOrWhiteSpace(resolvedDefault))
            {
                components.Add(resolvedDefault);
            }

            if (components.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No launcher activities were found for package '{normalizedPackageId}'."
                );
            }

            List<RoapLauncherInfo> launchers = new List<RoapLauncherInfo>();
            foreach (string component in components)
            {
                string activityName = component.Substring(component.IndexOf('/') + 1);
                launchers.Add(
                    new RoapLauncherInfo
                    {
                        ComponentName = component,
                        ActivityName = activityName,
                        IsResolvedDefault = string.Equals(component, resolvedDefault, StringComparison.OrdinalIgnoreCase),
                    }
                );
            }

            launchers.Sort(
                (left, right) =>
                {
                    int defaultComparison = right.IsResolvedDefault.CompareTo(left.IsResolvedDefault);
                    if (defaultComparison != 0)
                    {
                        return defaultComparison;
                    }

                    return string.Compare(left.ActivityName, right.ActivityName, StringComparison.OrdinalIgnoreCase);
                }
            );

            return launchers;
        }

        public static string LaunchApp(
            string deviceSerial,
            string packageId,
            string launcherComponent,
            IReadOnlyList<RoapLaunchParameter> parameters
        )
        {
            if (string.IsNullOrWhiteSpace(deviceSerial))
            {
                throw new InvalidOperationException("Select an Android device before launching.");
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new InvalidOperationException("Enter an Android package id before launching.");
            }

            string component = NormalizeComponent(launcherComponent, packageId.Trim());
            if (string.IsNullOrWhiteSpace(component))
            {
                throw new InvalidOperationException("Select a launcher activity before launching.");
            }

            List<string> shellArguments = new List<string>
            {
                "am",
                "start",
                "-W",
                "-n",
                component,
            };

            if (parameters != null)
            {
                foreach (RoapLaunchParameter parameter in parameters)
                {
                    if (
                        parameter == null ||
                        !parameter.enabled ||
                        string.IsNullOrWhiteSpace(parameter.key)
                    )
                    {
                        continue;
                    }

                    shellArguments.Add(GetExtraSwitch(parameter.type));
                    shellArguments.Add(parameter.key);
                    shellArguments.Add(parameter.value ?? string.Empty);
                }
            }

            List<string> commandArguments = new List<string>
            {
                "-s",
                deviceSerial,
                "shell",
                BuildShellCommand(shellArguments),
            };

            RoapProcessResult result = RunAdbCommand(commandArguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(GetFailureMessage("launch Android app", result));
            }

            if (ContainsFailure(result.StandardOutput) || ContainsFailure(result.StandardError))
            {
                throw new InvalidOperationException(GetFailureMessage("launch Android app", result));
            }

            return string.IsNullOrWhiteSpace(result.StandardOutput)
                ? "Application launched successfully."
                : result.StandardOutput.Trim();
        }

        private static IEnumerable<string> QueryLauncherComponents(string deviceSerial, string packageId)
        {
            List<string[]> commandVariants = new List<string[]>
            {
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "query-activities",
                    "--brief",
                    "--user",
                    "0",
                    packageId,
                    IntentActionMain,
                    IntentCategoryLauncher,
                },
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "query-activities",
                    "--brief",
                    IntentActionMain,
                    IntentCategoryLauncher,
                    packageId,
                },
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "query-activities",
                    "--brief",
                    "-a",
                    IntentActionMain,
                    "-c",
                    IntentCategoryLauncher,
                    packageId,
                },
            };

            HashSet<string> components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] command in commandVariants)
            {
                RoapProcessResult result = RunAdbCommand(command);
                foreach (string component in ExtractComponents(result.StandardOutput, packageId))
                {
                    components.Add(component);
                }

                if (components.Count > 0)
                {
                    break;
                }
            }

            return components;
        }

        private static string TryResolveDefaultLauncher(string deviceSerial, string packageId)
        {
            List<string[]> commandVariants = new List<string[]>
            {
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "resolve-activity",
                    "--brief",
                    "--user",
                    "0",
                    packageId,
                    IntentActionMain,
                    IntentCategoryLauncher,
                },
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "resolve-activity",
                    "--brief",
                    IntentActionMain,
                    IntentCategoryLauncher,
                    packageId,
                },
                new[]
                {
                    "-s",
                    deviceSerial,
                    "shell",
                    "cmd",
                    "package",
                    "resolve-activity",
                    "--brief",
                    "-a",
                    IntentActionMain,
                    "-c",
                    IntentCategoryLauncher,
                    packageId,
                },
            };

            foreach (string[] command in commandVariants)
            {
                RoapProcessResult result = RunAdbCommand(command);
                foreach (string component in ExtractComponents(result.StandardOutput, packageId))
                {
                    return component;
                }
            }

            return string.Empty;
        }

        private static string GetExtraSwitch(RoapParameterType type)
        {
            return type switch
            {
                RoapParameterType.Integer => "--ei",
                RoapParameterType.Boolean => "--ez",
                RoapParameterType.Long => "--el",
                RoapParameterType.Float => "--ef",
                RoapParameterType.Uri => "--eu",
                _ => "--es",
            };
        }

        private static string BuildShellCommand(IReadOnlyList<string> arguments)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(EscapeShellArgument(arguments[i] ?? string.Empty));
            }

            return builder.ToString();
        }

        private static string EscapeShellArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "''";
            }

            StringBuilder builder = new StringBuilder(argument.Length + 2);
            builder.Append('\'');
            foreach (char character in argument)
            {
                if (character == '\'')
                {
                    builder.Append("'\"'\"'");
                    continue;
                }

                builder.Append(character);
            }

            builder.Append('\'');
            return builder.ToString();
        }

        private static IEnumerable<string> ExtractComponents(string output, string packageId)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                yield break;
            }

            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in SplitLines(output))
            {
                MatchCollection matches = ComponentRegex.Matches(line);
                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    string package = match.Groups["package"].Value;
                    if (!string.Equals(package, packageId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string component = NormalizeComponent(match.Value, packageId);
                    if (string.IsNullOrWhiteSpace(component) || !yielded.Add(component))
                    {
                        continue;
                    }

                    yield return component;
                }
            }
        }

        private static string NormalizeComponent(string component, string packageId)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                return string.Empty;
            }

            string[] segments = component.Trim().Split('/');
            if (segments.Length != 2)
            {
                return string.Empty;
            }

            string package = string.IsNullOrWhiteSpace(segments[0]) ? packageId : segments[0];
            string activity = segments[1];
            if (string.IsNullOrWhiteSpace(activity))
            {
                return string.Empty;
            }

            string fullActivity = activity;
            if (activity.StartsWith("."))
            {
                fullActivity = package + activity;
            }
            else if (!activity.Contains("."))
            {
                fullActivity = $"{package}.{activity}";
            }

            return $"{package}/{fullActivity}";
        }

        private static bool ContainsFailure(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (
                       text.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       text.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       text.IndexOf("unable to resolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       text.IndexOf("unable to find", StringComparison.OrdinalIgnoreCase) >= 0
                   );
        }

        private static string GetFailureMessage(string action, RoapProcessResult result)
        {
            string output = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            output = string.IsNullOrWhiteSpace(output)
                ? "No additional adb output was produced."
                : output.Trim();

            return $"Failed to {action}. adb exited with code {result.ExitCode}. {output}";
        }

        private static RoapProcessResult RunAdbCommand(IReadOnlyList<string> arguments)
        {
            if (!TryResolveAdbPath(out string adbPath))
            {
                throw new InvalidOperationException(
                    "Unable to locate adb. Configure the Android SDK path in Unity Preferences or add adb to PATH."
                );
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = BuildCommandLine(arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = false,
            };

            StringBuilder standardOutputBuilder = new StringBuilder();
            StringBuilder standardErrorBuilder = new StringBuilder();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    standardOutputBuilder.AppendLine(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    standardErrorBuilder.AppendLine(eventArgs.Data);
                }
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to start adb at '{adbPath}': {ex.Message}",
                    ex
                );
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(DefaultTimeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore kill failures; timeout is already the root error.
                }

                throw new TimeoutException("adb did not finish before the timeout expired.");
            }

            process.WaitForExit();

            return new RoapProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = standardOutputBuilder.ToString(),
                StandardError = standardErrorBuilder.ToString(),
            };
        }

        private static bool TryResolveAdbPath(out string adbPath)
        {
            string executableName =
#if UNITY_EDITOR_WIN
                "adb.exe";
#else
                "adb";
#endif
            string sdkRoot = GetUnityAndroidSdkRoot();
            if (!string.IsNullOrWhiteSpace(sdkRoot))
            {
                string sdkCandidate = Path.Combine(sdkRoot, "platform-tools", executableName);
                if (File.Exists(sdkCandidate))
                {
                    adbPath = sdkCandidate;
                    return true;
                }
            }

            if (TryFindOnPath(executableName, out adbPath))
            {
                return true;
            }

            adbPath = string.Empty;
            return false;
        }

        private static string GetUnityAndroidSdkRoot()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type settingsType = assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
                if (settingsType == null)
                {
                    continue;
                }

                PropertyInfo sdkRootProperty = settingsType.GetProperty(
                    "sdkRootPath",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (sdkRootProperty?.GetValue(null) is string sdkRootPath && !string.IsNullOrWhiteSpace(sdkRootPath))
                {
                    return sdkRootPath;
                }
            }

            return EditorPrefs.GetString(AndroidSdkRootEditorPrefsKey, string.Empty);
        }

        private static bool TryFindOnPath(string executableName, out string fullPath)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] directories = pathValue.Split(Path.PathSeparator);
            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(directory.Trim(), executableName);
                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }
            }

            fullPath = string.Empty;
            return false;
        }

        private static string BuildCommandLine(IReadOnlyList<string> arguments)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(arguments[i] ?? string.Empty));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('"');

            int backslashCount = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append(character);
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    builder.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                builder.Append(character);
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount * 2);
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(
                new[]
                {
                    "\r\n",
                    "\n",
                    "\r",
                },
                StringSplitOptions.RemoveEmptyEntries
            );
        }

        private static string[] SplitByWhitespace(string text, int maxCount)
        {
            return text.Split((char[])null, maxCount, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
