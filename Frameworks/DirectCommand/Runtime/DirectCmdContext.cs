using System;
using System.Collections.Generic;
using System.Globalization;

namespace Com.Hapiga.Scheherazade.Common
{
    public sealed class DirectCmdContext
    {
        public string CommandName { get; }
        public string SubcommandPath { get; }
        public int PositionalCount => _positionalArgs.Length;

        private readonly Dictionary<string, string> _namedValues;
        private readonly string[] _positionalArgs;
        private readonly IReadOnlyList<IDirectCmdParameter> _parameters;

        internal DirectCmdContext(
            string commandName,
            string subcommandPath,
            Dictionary<string, string> namedValues,
            string[] positionalArgs,
            IReadOnlyList<IDirectCmdParameter> parameters)
        {
            CommandName = commandName;
            SubcommandPath = subcommandPath;
            _namedValues = namedValues;
            _positionalArgs = positionalArgs;
            _parameters = parameters;
        }

        public string GetPositional(int index)
        {
            return index >= 0 && index < _positionalArgs.Length
                ? _positionalArgs[index]
                : null;
        }

        public T GetPositional<T>(int index, T defaultValue = default)
        {
            string raw = GetPositional(index);
            if (raw == null)
                return defaultValue;

            return TryConvertValue(raw, out T result) ? result : defaultValue;
        }

        public T GetParam<T>(string name, T defaultValue = default)
        {
            if (TryGetRawValue(name, out string raw) && TryConvertValue(raw, out T result))
                return result;

            return GetRegisteredDefault(name, defaultValue);
        }

        public bool TryGetParam<T>(string name, out T value)
        {
            if (TryGetRawValue(name, out string raw) && TryConvertValue(raw, out value))
                return true;

            value = GetRegisteredDefault(name, default(T));
            return false;
        }

        private bool TryGetRawValue(string name, out string raw)
        {
            if (_namedValues.TryGetValue(name, out raw))
                return true;

            foreach (IDirectCmdParameter param in _parameters)
            {
                if (!string.Equals(param.LongName, name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(param.ShortName, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_namedValues.TryGetValue(param.LongName, out raw))
                    return true;

                if (param.ShortName != null && _namedValues.TryGetValue(param.ShortName, out raw))
                    return true;

                break;
            }

            return false;
        }

        private T GetRegisteredDefault<T>(string name, T fallback)
        {
            foreach (IDirectCmdParameter param in _parameters)
            {
                bool nameMatches =
                    string.Equals(param.LongName, name, StringComparison.OrdinalIgnoreCase) ||
                    (param.ShortName != null && string.Equals(param.ShortName, name, StringComparison.OrdinalIgnoreCase));

                if (!nameMatches)
                    continue;

                if (param.DefaultValue is T typedDefault)
                    return typedDefault;

                break;
            }

            return fallback;
        }

        internal static bool TryConvertValue<T>(string raw, out T result)
        {
            Type targetType = typeof(T);

            try
            {
                if (targetType == typeof(string))
                {
                    result = (T)(object)raw;
                    return true;
                }

                if (targetType == typeof(int))
                {
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
                    {
                        result = (T)(object)val;
                        return true;
                    }
                }
                else if (targetType == typeof(float))
                {
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    {
                        result = (T)(object)val;
                        return true;
                    }
                }
                else if (targetType == typeof(double))
                {
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    {
                        result = (T)(object)val;
                        return true;
                    }
                }
                else if (targetType == typeof(long))
                {
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long val))
                    {
                        result = (T)(object)val;
                        return true;
                    }
                }
                else if (targetType == typeof(bool))
                {
                    result = (T)(object)ParseBool(raw);
                    return true;
                }
                else if (targetType.IsEnum)
                {
                    try
                    {
                        result = (T)Enum.Parse(targetType, raw, true);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            result = default;
            return false;
        }

        private static bool ParseBool(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;

            string lower = raw.Trim().ToLowerInvariant();
            return lower == "true" || lower == "1" || lower == "yes" || lower == "on";
        }
    }
}
