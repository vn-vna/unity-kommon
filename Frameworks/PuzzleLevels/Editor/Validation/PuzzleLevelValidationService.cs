using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor.Validation
{
    public static class PuzzleLevelValidationService
    {
        #region Private Fields

        private static readonly Dictionary<int, SourceObservation> SourceObservations
            = new Dictionary<int, SourceObservation>();
        private static readonly Dictionary<string, ValidationCacheValue> ValidationCache
            = new Dictionary<string, ValidationCacheValue>();
        private static readonly HashSet<string> LoggedValidatorFailures
            = new HashSet<string>(StringComparer.Ordinal);

        private static List<ValidatorDescriptor> _validators;

        #endregion

        #region Public Methods

        public static PuzzleLevelValidationRequest CreateRequest(
            string levelId,
            TextAsset asset,
            DataType declaredDataType)
        {
            DataType effectiveDataType = declaredDataType == DataType.Unknown
                ? DataType.Text
                : declaredDataType;
            byte[] content = Array.Empty<byte>();
            string contentHash = string.Empty;

            if (asset != null)
            {
                SourceObservation observation = GetSourceObservation(asset);
                content = observation.Content;
                contentHash = observation.ContentHash;
            }

            return new PuzzleLevelValidationRequest(
                levelId,
                asset,
                asset == null ? string.Empty : asset.name,
                declaredDataType,
                effectiveDataType,
                contentHash,
                content);
        }

        public static PuzzleLevelValidationResult Validate(
            PuzzleLevelValidationRequest request)
        {
            List<PuzzleLevelValidationDiagnostic> diagnostics
                = CreatePreflightDiagnostics(request);
            if (HasErrors(diagnostics))
            {
                return new PuzzleLevelValidationResult(
                    diagnostics,
                    request?.ContentHash,
                    string.Empty);
            }

            ValidatorSelection selection = SelectValidator(request);
            diagnostics.AddRange(selection.Diagnostics);
            if (selection.Validator == null)
            {
                return new PuzzleLevelValidationResult(
                    diagnostics,
                    request.ContentHash,
                    string.Empty);
            }

            string cacheKey = CreateValidationCacheKey(request, selection.Validator);
            if (!ValidationCache.TryGetValue(cacheKey, out ValidationCacheValue cacheValue))
            {
                cacheValue = CreateValidationCacheValue(request, selection.Validator);
                ValidationCache[cacheKey] = cacheValue;
            }

            diagnostics.AddRange(cacheValue.Diagnostics);
            return new PuzzleLevelValidationResult(
                diagnostics,
                request.ContentHash,
                selection.Validator.DisplayName);
        }

        public static bool TryDeserialize(
            PuzzleLevelValidationRequest request,
            out PuzzleLevelDeserializationResult result,
            out string error)
        {
            result = null;
            error = null;
            List<PuzzleLevelValidationDiagnostic> diagnostics
                = CreatePreflightDiagnostics(request);
            if (HasErrors(diagnostics))
            {
                error = diagnostics[0].Message;
                return false;
            }

            ValidatorSelection selection = SelectValidator(request);
            if (selection.Validator == null)
            {
                error = GetFirstDiagnosticMessage(
                    selection.Diagnostics,
                    "No puzzle level validator accepts this entry.");
                return false;
            }

            try
            {
                if (selection.Validator.Instance.TryDeserialize(
                        request,
                        out result,
                        out error))
                {
                    return true;
                }

                error ??= "The selected validator did not provide deserialized data.";
                return false;
            }
            catch (Exception exception)
            {
                LogValidatorFailure(
                    selection.Validator,
                    "deserializing",
                    exception);
                error = "The selected validator failed while deserializing this entry.";
                return false;
            }
        }

        public static void InvalidateAll()
        {
            SourceObservations.Clear();
            ValidationCache.Clear();
        }

        public static void InvalidateAsset(TextAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            SourceObservations.Remove(asset.GetInstanceID());
        }

        #endregion

        #region Private Methods

        private static SourceObservation GetSourceObservation(TextAsset asset)
        {
            int instanceId = asset.GetInstanceID();
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string dependencyHash = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(assetPath).ToString();

            if (!string.IsNullOrEmpty(dependencyHash)
                && SourceObservations.TryGetValue(instanceId, out SourceObservation cached)
                && cached.DependencyHash == dependencyHash)
            {
                return cached;
            }

            byte[] content = asset.bytes ?? Array.Empty<byte>();
            SourceObservation observation = new SourceObservation(
                dependencyHash,
                ComputeContentHash(content),
                content);
            if (!string.IsNullOrEmpty(dependencyHash))
            {
                SourceObservations[instanceId] = observation;
            }

            return observation;
        }

        private static string ComputeContentHash(byte[] content)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(content ?? Array.Empty<byte>());
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static List<PuzzleLevelValidationDiagnostic> CreatePreflightDiagnostics(
            PuzzleLevelValidationRequest request)
        {
            List<PuzzleLevelValidationDiagnostic> diagnostics
                = new List<PuzzleLevelValidationDiagnostic>();
            if (request == null)
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "ENTRY_REQUEST_MISSING",
                    PuzzleLevelValidationSeverity.Error,
                    "Puzzle level validation request is missing."));
                return diagnostics;
            }

            if (string.IsNullOrWhiteSpace(request.LevelId))
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "ENTRY_ID_EMPTY",
                    PuzzleLevelValidationSeverity.Error,
                    "Level ID cannot be empty."));
            }

            if (request.Asset == null && string.IsNullOrWhiteSpace(request.SourceName))
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "ENTRY_ASSET_MISSING",
                    PuzzleLevelValidationSeverity.Error,
                    "Level asset is missing."));
            }

            if (!Enum.IsDefined(typeof(DataType), request.DeclaredDataType))
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "DATA_TYPE_INVALID",
                    PuzzleLevelValidationSeverity.Error,
                    $"Unsupported data type value {(int)request.DeclaredDataType}."));
                return diagnostics;
            }

            if (request.DeclaredDataType == DataType.Unknown)
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "DATA_TYPE_DEFAULTED",
                    PuzzleLevelValidationSeverity.Warning,
                    "Data type defaults to Text."));
            }

            return diagnostics;
        }

        private static ValidatorSelection SelectValidator(
            PuzzleLevelValidationRequest request)
        {
            EnsureValidators();
            List<PuzzleLevelValidationDiagnostic> diagnostics
                = new List<PuzzleLevelValidationDiagnostic>();
            List<ValidatorDescriptor> matches = new List<ValidatorDescriptor>();

            for (int i = 0; i < _validators.Count; i++)
            {
                ValidatorDescriptor validator = _validators[i];
                try
                {
                    if (validator.Instance.CanValidate(request))
                    {
                        matches.Add(validator);
                    }
                }
                catch (Exception exception)
                {
                    LogValidatorFailure(validator, "checking eligibility", exception);
                    diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                        "VALIDATOR_ELIGIBILITY_FAILED",
                        PuzzleLevelValidationSeverity.Warning,
                        $"Validator '{validator.DisplayName}' failed while checking eligibility."));
                }
            }

            if (matches.Count == 0)
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "VALIDATOR_UNAVAILABLE",
                    PuzzleLevelValidationSeverity.Warning,
                    "No puzzle level validator accepts this entry."));
                return new ValidatorSelection(null, diagnostics);
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new PuzzleLevelValidationDiagnostic(
                    "VALIDATOR_AMBIGUOUS",
                    PuzzleLevelValidationSeverity.Warning,
                    $"Multiple validators accept this entry. Using '{matches[0].DisplayName}'."));
            }

            return new ValidatorSelection(matches[0], diagnostics);
        }

        private static void EnsureValidators()
        {
            if (_validators != null)
            {
                return;
            }

            _validators = new List<ValidatorDescriptor>();
            TypeCache.TypeCollection types
                = TypeCache.GetTypesDerivedFrom<IPuzzleLevelValidator>();
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters
                    || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    IPuzzleLevelValidator validator
                        = Activator.CreateInstance(type) as IPuzzleLevelValidator;
                    if (validator != null)
                    {
                        _validators.Add(new ValidatorDescriptor(validator));
                    }
                }
                catch (Exception exception)
                {
                    string validatorName = type.FullName ?? type.Name;
                    if (LoggedValidatorFailures.Add(validatorName + ":construction"))
                    {
                        QuickLog.Warning<ValidationLogContext>(
                            "Validator '{0}' could not be created: {1}",
                            validatorName,
                            exception.Message);
                    }
                }
            }

            _validators.Sort(CompareValidators);
        }

        private static int CompareValidators(
            ValidatorDescriptor first,
            ValidatorDescriptor second)
        {
            int orderComparison = first.Instance.Order.CompareTo(second.Instance.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            int assemblyComparison = string.Compare(
                first.Type.Assembly.GetName().Name,
                second.Type.Assembly.GetName().Name,
                StringComparison.Ordinal);
            return assemblyComparison != 0
                ? assemblyComparison
                : string.Compare(
                    first.Type.FullName,
                    second.Type.FullName,
                    StringComparison.Ordinal);
        }

        private static ValidationCacheValue CreateValidationCacheValue(
            PuzzleLevelValidationRequest request,
            ValidatorDescriptor validator)
        {
            try
            {
                IReadOnlyList<PuzzleLevelValidationDiagnostic> validatorDiagnostics
                    = validator.Instance.Validate(request);
                return new ValidationCacheValue(
                    validatorDiagnostics ?? Array.Empty<PuzzleLevelValidationDiagnostic>());
            }
            catch (Exception exception)
            {
                LogValidatorFailure(validator, "validating", exception);
                return new ValidationCacheValue(
                    new[] { new PuzzleLevelValidationDiagnostic(
                        "VALIDATOR_EXCEPTION",
                        PuzzleLevelValidationSeverity.Error,
                        $"Validator '{validator.DisplayName}' failed while validating this entry.") });
            }
        }

        private static string CreateValidationCacheKey(
            PuzzleLevelValidationRequest request,
            ValidatorDescriptor validator)
        {
            return string.Concat(
                request.ContentHash,
                "|",
                request.LevelId ?? string.Empty,
                "|",
                ((int)request.DeclaredDataType).ToString(),
                "|",
                ((int)request.EffectiveDataType).ToString(),
                "|",
                validator.Type.AssemblyQualifiedName);
        }

        private static bool HasErrors(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == PuzzleLevelValidationSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetFirstDiagnosticMessage(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            string fallback)
        {
            return diagnostics.Count > 0 ? diagnostics[0].Message : fallback;
        }

        private static void LogValidatorFailure(
            ValidatorDescriptor validator,
            string operation,
            Exception exception)
        {
            string key = validator.Type.AssemblyQualifiedName + ":" + operation;
            if (!LoggedValidatorFailures.Add(key))
            {
                return;
            }

            QuickLog.Warning<ValidationLogContext>(
                "Validator '{0}' failed while {1}: {2}",
                validator.DisplayName,
                operation,
                exception.Message);
        }

        #endregion

        #region Nested Types

        private readonly struct SourceObservation
        {
            public string DependencyHash { get; }
            public string ContentHash { get; }
            public byte[] Content { get; }

            public SourceObservation(
                string dependencyHash,
                string contentHash,
                byte[] content)
            {
                DependencyHash = dependencyHash;
                ContentHash = contentHash;
                Content = content;
            }
        }

        private sealed class ValidationCacheValue
        {
            public IReadOnlyList<PuzzleLevelValidationDiagnostic> Diagnostics { get; }

            public ValidationCacheValue(
                IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
            {
                Diagnostics = diagnostics;
            }
        }

        private sealed class ValidatorDescriptor
        {
            public IPuzzleLevelValidator Instance { get; }
            public Type Type { get; }
            public string DisplayName { get; }

            public ValidatorDescriptor(IPuzzleLevelValidator instance)
            {
                Instance = instance;
                Type = instance.GetType();
                DisplayName = Type.FullName ?? Type.Name;
            }
        }

        private sealed class ValidatorSelection
        {
            public ValidatorDescriptor Validator { get; }
            public IReadOnlyList<PuzzleLevelValidationDiagnostic> Diagnostics { get; }

            public ValidatorSelection(
                ValidatorDescriptor validator,
                IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics)
            {
                Validator = validator;
                Diagnostics = diagnostics;
            }
        }

        private sealed class ValidationLogContext
        {
        }

        #endregion
    }
}
