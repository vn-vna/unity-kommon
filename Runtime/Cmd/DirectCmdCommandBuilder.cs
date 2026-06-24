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
        public Action<DirectCmdContext> Callback { get; }

        public DirectCmdRegistration(
            string commandName,
            IReadOnlyList<IDirectCmdParameter> parameters,
            Action<DirectCmdContext> callback)
        {
            CommandName = commandName;
            Parameters = parameters;
            Callback = callback;
        }
    }

    public sealed class DirectCmdCommandBuilder
    {
        private readonly string _commandName;
        private readonly List<IDirectCmdParameter> _parameters = new();

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

        public void OnExecute(Action<DirectCmdContext> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            DirectCmdForwarding.Register(_commandName, _parameters, callback);
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
