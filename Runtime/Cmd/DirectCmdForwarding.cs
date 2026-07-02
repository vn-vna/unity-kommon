using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        internal static void Register(DirectCmdCommandBuilder builder)
        {
            DirectCmdForwarding instance = Instance;
            if (instance == null)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "DirectCmdForwarding instance is not available. Cannot register command '{0}'.",
                    builder.CommandName);
                return;
            }

            if (instance._registrations.ContainsKey(builder.CommandName))
            {
                QuickLog.Warning<DirectCmdForwarding>(
                    "Command '{0}' is already registered and will be overwritten.",
                    builder.CommandName);
            }

            DirectCmdRegistration registration = ConvertToRegistration(builder);
            instance._registrations[builder.CommandName] = registration;

            QuickLog.Debug<DirectCmdForwarding>(
                "Registered command '{0}' with {1} parameter(s) and {2} subcommand(s).",
                builder.CommandName,
                builder.Parameters.Count,
                builder.Subcommands.Count);
        }

        private static DirectCmdRegistration ConvertToRegistration(DirectCmdCommandBuilder builder)
        {
            var subRegs = new Dictionary<string, DirectCmdRegistration>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, DirectCmdCommandBuilder> kvp in builder.Subcommands)
            {
                subRegs[kvp.Key] = ConvertToRegistration(kvp.Value);
            }

            return new DirectCmdRegistration(
                builder.CommandName,
                builder.Parameters,
                subRegs,
                builder.Callback,
                builder.AllowPositional);
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

            var chain = new List<DirectCmdContext>();
            ResolveAndDispatch(registration, tokens, 1, chain, null);
        }

        private bool ResolveAndDispatch(
            DirectCmdRegistration registration,
            List<string> tokens,
            int startIndex,
            List<DirectCmdContext> chain,
            string subcommandPath)
        {
            var namedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positionalArgs = new List<string>();
            int i = startIndex;
            DirectCmdRegistration subRegistration = null;
            string subName = null;
            int subTokenIndex = -1;

            while (i < tokens.Count)
            {
                string token = tokens[i];

                if (token.StartsWith("--") && token.Length > 2)
                {
                    string key;
                    int eqIndex = token.IndexOf('=', 2);
                    if (eqIndex >= 0)
                    {
                        key = token.Substring(2, eqIndex - 2);
                        string value = token.Substring(eqIndex + 1);
                        namedValues[key] = UnquoteValue(value);
                        i++;
                        continue;
                    }

                    key = token.Substring(2);
                    namedValues[key] = ReadParameterValue(tokens, ref i);
                    i++;
                    continue;
                }

                if (token.StartsWith("-") && token.Length >= 4 && token[2] == '=' && char.IsLetter(token[1]))
                {
                    string key = token.Substring(1, 1);
                    string value = token.Substring(3);
                    namedValues[key] = UnquoteValue(value);
                    i++;
                    continue;
                }

                if (token.StartsWith("-") && token.Length == 2 && char.IsLetter(token[1]))
                {
                    string key = token.Substring(1);
                    namedValues[key] = ReadParameterValue(tokens, ref i);
                    i++;
                    continue;
                }

                if (registration.Subcommands.Count > 0 &&
                    registration.Subcommands.TryGetValue(token, out subRegistration))
                {
                    subName = token;
                    subTokenIndex = i;
                    break;
                }

                positionalArgs.Add(token);
                i++;
            }

            if (!registration.AllowPositional && positionalArgs.Count > 0)
            {
                QuickLog.Error<DirectCmdForwarding>(
                    "Command '{0}' does not accept positional arguments.",
                    registration.CommandName);
                return false;
            }

            if (!ValidateRequiredParams(registration.Parameters, namedValues))
                return false;

            var context = new DirectCmdContext(
                registration.CommandName,
                subcommandPath,
                namedValues,
                positionalArgs.ToArray(),
                registration.Parameters);
            chain.Add(context);

            if (subRegistration != null)
            {
                string newPath = string.IsNullOrEmpty(subcommandPath)
                    ? subName
                    : subcommandPath + "." + subName;
                return ResolveAndDispatch(subRegistration, tokens, subTokenIndex + 1, chain, newPath);
            }

            if (registration.Callback == null)
            {
                QuickLog.Warning<DirectCmdForwarding>(
                    "No handler registered for command '{0}'.",
                    string.Join(" ", chain.ConvertAll(c => c.CommandName)));
                return false;
            }

            QuickLog.Info<DirectCmdForwarding>(
                "Executing command '{0}', with positional parameters: [{1}] and arguments: [{2}]",
                string.Join(" ", chain.ConvertAll(c => c.CommandName)),
                (Func<object>)(() => string.Join(",", positionalArgs)),
                (Func<object>)(() => string.Join(";", namedValues.Select(p => $"{{{p.Key} = {p.Value?.ToString() ?? "null"}}}")))
            );

            registration.Callback(chain.ToArray());
            return true;
        }

        private static bool ValidateRequiredParams(
            IReadOnlyList<IDirectCmdParameter> parameters,
            Dictionary<string, string> namedValues)
        {
            foreach (IDirectCmdParameter param in parameters)
            {
                if (!param.IsRequired)
                    continue;

                bool hasValue = namedValues.ContainsKey(param.LongName);
                if (!hasValue && param.ShortName != null)
                    hasValue = namedValues.ContainsKey(param.ShortName);

                if (hasValue)
                    continue;

                QuickLog.Error<DirectCmdForwarding>(
                    "Missing required parameter '--{0}'.",
                    param.LongName);
                return false;
            }

            return true;
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
            if (token.StartsWith("--"))
                return true;

            if (token.StartsWith("-") && token.Length >= 2)
            {
                if (token.Length == 2 && char.IsLetter(token[1]))
                    return true;

                if (token.Length >= 4 && token[2] == '=' && char.IsLetter(token[1]))
                    return true;
            }

            return false;
        }

        private static string UnquoteValue(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                return value.Substring(1, value.Length - 2);
            return value;
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
                {
                    if (line[i] == '"')
                    {
                        i++;
                        while (i < line.Length && line[i] != '"')
                            i++;
                        if (i < line.Length)
                            i++;
                    }
                    else
                    {
                        i++;
                    }
                }

                tokens.Add(line.Substring(tokenStart, i - tokenStart));
            }

            return tokens;
        }
    }
}
