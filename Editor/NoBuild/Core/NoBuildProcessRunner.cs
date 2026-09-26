using System;
using System.Diagnostics;
using System.Text;

namespace Com.Scheherazade.Common.NoBuild.Editor
{
    internal readonly struct ProcessExecutionResult
    {
        public ProcessExecutionResult(
            int exitCode,
            string standardOutput,
            string standardError,
            bool timedOut)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            TimedOut = timedOut;
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool TimedOut { get; }
        public string CombinedOutput => string.IsNullOrEmpty(StandardError)
            ? StandardOutput
            : StandardOutput + Environment.NewLine + StandardError;
    }

    internal static class NoBuildProcessRunner
    {
        public static ProcessExecutionResult Run(
            string fileName,
            string arguments,
            int timeoutMs,
            string workingDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException(
                    "Process file name cannot be empty.",
                    nameof(fileName));
            }

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            object outputLock = new();
            object errorLock = new();

            using Process process = CreateProcess(
                fileName,
                arguments,
                workingDirectory,
                standardOutput,
                standardError,
                outputLock,
                errorLock);

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Failed to start process: {fileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                StopTimedOutProcess(process);
                return new ProcessExecutionResult(
                    -1,
                    standardOutput.ToString().TrimEnd(),
                    standardError.ToString().TrimEnd(),
                    true);
            }

            process.WaitForExit();
            return new ProcessExecutionResult(
                process.ExitCode,
                standardOutput.ToString().TrimEnd(),
                standardError.ToString().TrimEnd(),
                false);
        }

        private static Process CreateProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            StringBuilder standardOutput,
            StringBuilder standardError,
            object outputLock,
            object errorLock)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data == null) return;
                lock (outputLock)
                {
                    standardOutput.AppendLine(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data == null) return;
                lock (errorLock)
                {
                    standardError.AppendLine(eventArgs.Data);
                }
            };
            return process;
        }

        private static void StopTimedOutProcess(Process process)
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch
            {
                // Best effort. The timeout result remains actionable.
            }
        }
    }
}
