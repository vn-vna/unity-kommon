using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common
{
    [DontDestroyOnLoad]
    [AddComponentMenu("Scheherazade/Cmd/Direct Cmd Forwarding")]
    public sealed class DirectCmdForwarding : SingletonBehavior<DirectCmdForwarding>
    {
        private const string FileName = "__dcf__";

        [SerializeField]
        private bool _pollEveryFrame = true;

        [SerializeField, Min(0.05f)]
        private float _pollInterval = 0.5f;

        private float _lastPollTime;
        private string _filePath;
        private bool _filePathInitialized;

        private readonly Dictionary<string, DirectCmdRegistration> _registrations =
            new(StringComparer.OrdinalIgnoreCase);

        public static event Action FileFound;
        public static event Action FileProcessed;

        public static DirectCmdCommandBuilder RegisterCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be empty.", nameof(commandName));

            EnsureInstance();
            return new DirectCmdCommandBuilder(commandName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null)
                return;

            GameObject go = new(nameof(DirectCmdForwarding));
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<DirectCmdForwarding>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
                return;

            InitFilePath();
        }

        private void InitFilePath()
        {
            try
            {
                _filePath = Path.Combine(Application.persistentDataPath, FileName);
                _filePathInitialized = true;

                QuickLog.Debug<DirectCmdForwarding>(
                    "Direct Cmd Forwarding initialized. Watching: {0}",
                    _filePath);
            }
            catch (Exception ex)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "Failed to initialize __dcf__ file path: {0}",
                    ex);
            }
        }

        private void Update()
        {
            if (Instance != this || !_filePathInitialized)
                return;

            if (_pollEveryFrame)
            {
                PollFile();
                return;
            }

            _lastPollTime += Time.unscaledDeltaTime;
            if (_lastPollTime >= _pollInterval)
            {
                _lastPollTime = 0f;
                PollFile();
            }
        }

        private void OnValidate()
        {
            _pollInterval = Mathf.Max(0.05f, _pollInterval);
        }

        internal static void Register(
            string commandName,
            IReadOnlyList<IDirectCmdParameter> parameters,
            Action<DirectCmdContext> callback)
        {
            DirectCmdForwarding instance = Instance;
            if (instance == null)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "DirectCmdForwarding instance is not available. Cannot register command '{0}'.",
                    commandName);
                return;
            }

            if (instance._registrations.ContainsKey(commandName))
            {
                QuickLog.Warning<DirectCmdForwarding>(
                    "Command '{0}' is already registered and will be overwritten.",
                    commandName);
            }

            instance._registrations[commandName] = new DirectCmdRegistration(
                commandName, parameters, callback);

            QuickLog.Debug<DirectCmdForwarding>(
                "Registered command '{0}' with {1} parameter(s).",
                commandName,
                parameters.Count);
        }

        private static void EnsureInstance()
        {
            if (Instance != null)
                return;

            DirectCmdForwarding existing = FindObjectOfType<DirectCmdForwarding>();
            if (existing != null)
                return;

            GameObject go = new(nameof(DirectCmdForwarding));
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<DirectCmdForwarding>();
        }

        private void PollFile()
        {
            if (!File.Exists(_filePath))
                return;

            try
            {
                FileFound?.Invoke();
            }
            catch (Exception ex)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "Exception in FileFound event handler: {0}",
                    ex);
            }

            string[] lines = null;
            try
            {
                lines = File.ReadAllLines(_filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "Failed to read {0}: {1}",
                    _filePath,
                    ex);
            }
            finally
            {
                TryDeleteFile();
            }

            if (lines != null && lines.Length > 0)
            {
                ProcessLines(lines);
            }

            try
            {
                FileProcessed?.Invoke();
            }
            catch (Exception ex)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "Exception in FileProcessed event handler: {0}",
                    ex);
            }
        }

        private void TryDeleteFile()
        {
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<DirectCmdForwarding>(
                    "Failed to delete {0}: {1}",
                    _filePath,
                    ex);
            }
        }

        private void ProcessLines(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                try
                {
                    ProcessLine(line);
                }
                catch (Exception ex)
                {
                    QuickLog.Error<DirectCmdForwarding>(
                        "Unexpected error processing command on line {0} '{1}': {2}",
                        i + 1,
                        line,
                        ex);
                }
            }
        }

        private void ProcessLine(string line)
        {
            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0)
                return;

            string commandName = tokens[0];

            if (!_registrations.TryGetValue(commandName, out DirectCmdRegistration registration))
            {
                QuickLog.Warning<DirectCmdForwarding>(
                    "Unknown command '{0}'. Registered commands: [{1}]",
                    commandName,
                    string.Join(", ", _registrations.Keys));
                return;
            }

            Dictionary<string, string> namedValues = new(StringComparer.OrdinalIgnoreCase);
            List<string> positionalArgs = new();

            for (int i = 1; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (token.StartsWith("--") && token.Length > 2)
                {
                    string key = token.Substring(2);
                    namedValues[key] = ReadParameterValue(tokens, ref i);
                }
                else if (token.StartsWith("-") && token.Length == 2 && char.IsLetter(token[1]))
                {
                    string key = token.Substring(1);
                    namedValues[key] = ReadParameterValue(tokens, ref i);
                }
                else
                {
                    positionalArgs.Add(token);
                }
            }

            foreach (IDirectCmdParameter param in registration.Parameters)
            {
                if (!param.IsRequired)
                    continue;

                bool hasValue = namedValues.ContainsKey(param.LongName);
                if (!hasValue && param.ShortName != null)
                    hasValue = namedValues.ContainsKey(param.ShortName);

                if (hasValue)
                    continue;

                QuickLog.Error<DirectCmdForwarding>(
                    "Missing required parameter '--{0}' for command '{1}'.",
                    param.LongName,
                    commandName);
                return;
            }

            DirectCmdContext context = new(
                commandName,
                namedValues,
                positionalArgs.ToArray(),
                registration.Parameters);

            registration.Callback(context);
        }

        private static string ReadParameterValue(List<string> tokens, ref int index)
        {
            if (index + 1 < tokens.Count && !IsParameterToken(tokens[index + 1]))
            {
                index++;
                return tokens[index];
            }

            return bool.TrueString;
        }

        private static bool IsParameterToken(string token)
        {
            return token.StartsWith("--") ||
                   (token.StartsWith("-") && token.Length == 2 && char.IsLetter(token[1]));
        }

        private static List<string> Tokenize(string line)
        {
            List<string> tokens = new();
            int i = 0;

            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                    i++;

                if (i >= line.Length)
                    break;

                if (line[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < line.Length && line[i] != '"')
                        i++;

                    tokens.Add(line.Substring(start, i - start));

                    if (i < line.Length)
                        i++;

                    continue;
                }

                int tokenStart = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]))
                    i++;

                tokens.Add(line.Substring(tokenStart, i - tokenStart));
            }

            return tokens;
        }
    }
}
