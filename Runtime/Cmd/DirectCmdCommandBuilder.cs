using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common
{
    internal interface IDirectCmdParameter
    {
        string LongName { get; }
        string ShortName { get; }
        bool IsRequired { get; }
        object DefaultValue { get; }
        Type ValueType { get; }
    }

    public class DirectCmdParameter<T>
    {
        public string LongName { get; }
        public string ShortName { get; }
        public bool IsRequired { get; }
        public T DefaultValue { get; }

        internal DirectCmdParameter(
            string longName,
            string shortName,
            bool isRequired,
            T defaultValue)
        {
            if (string.IsNullOrWhiteSpace(longName))
                throw new ArgumentException("Long name cannot be empty.", nameof(longName));

            LongName = longName;
            ShortName = shortName;
            IsRequired = isRequired;
            DefaultValue = defaultValue;
        }
    }

    internal sealed class DirectCmdRegistration
    {
        public string CommandName { get; }
        public IReadOnlyList<IDirectCmdParameter> Parameters { get; }
        public IReadOnlyDictionary<string, DirectCmdRegistration> Subcommands { get; }
        public Action<DirectCmdContext[]> Callback { get; }
        public bool AllowPositional { get; }

        public DirectCmdRegistration(
            string commandName,
            IReadOnlyList<IDirectCmdParameter> parameters,
            IReadOnlyDictionary<string, DirectCmdRegistration> subcommands,
            Action<DirectCmdContext[]> callback,
            bool allowPositional)
        {
            CommandName = commandName;
            Parameters = parameters;
            Subcommands = subcommands;
            Callback = callback;
            AllowPositional = allowPositional;
        }
    }

    public sealed class DirectCmdCommandBuilder
    {
        private readonly string _commandName;
        private readonly List<IDirectCmdParameter> _parameters = new();
        private readonly Dictionary<string, DirectCmdCommandBuilder> _subcommands =
            new(StringComparer.OrdinalIgnoreCase);
        private Action<DirectCmdContext[]> _callback;
        private bool _allowPositional = true;

        internal string CommandName => _commandName;
        internal IReadOnlyList<IDirectCmdParameter> Parameters => _parameters;
        internal IReadOnlyDictionary<string, DirectCmdCommandBuilder> Subcommands => _subcommands;
        internal Action<DirectCmdContext[]> Callback => _callback;
        internal bool AllowPositional => _allowPositional;

        internal DirectCmdCommandBuilder(string commandName)
        {
            _commandName = commandName;
        }

        public DirectCmdCommandBuilder WithParameter<T>(
            string longName,
            string shortName = null,
            bool required = false,
            T defaultValue = default)
        {
            _parameters.Add(new DirectCmdParameterImpl<T>(longName, shortName, required, defaultValue));
            return this;
        }

        public DirectCmdCommandBuilder WithSubcommand(
            string name,
            Action<DirectCmdCommandBuilder> configure)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Subcommand name cannot be empty.", nameof(name));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var subBuilder = new DirectCmdCommandBuilder(name);
            configure(subBuilder);
            _subcommands[name] = subBuilder;
            return this;
        }

        public DirectCmdCommandBuilder DisablePositional()
        {
            _allowPositional = false;
            return this;
        }

        public void OnExecute(Action<DirectCmdContext> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            OnExecute(chain => callback(chain[^1]));
        }

        public void OnExecute(Action<DirectCmdContext[]> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _callback = callback;
            DirectCmdForwarding.Register(this);
        }

        private sealed class DirectCmdParameterImpl<T> : DirectCmdParameter<T>, IDirectCmdParameter
        {
            Type IDirectCmdParameter.ValueType => typeof(T);
            object IDirectCmdParameter.DefaultValue => DefaultValue;

            public DirectCmdParameterImpl(
                string longName,
                string shortName,
                bool isRequired,
                T defaultValue)
                : base(longName, shortName, isRequired, defaultValue)
            {
            }
        }
    }
}
