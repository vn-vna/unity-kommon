using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor.Validation
{
    public enum PuzzleLevelValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum PuzzleLevelValidationStepStatus
    {
        Success,
        Warning,
        Failed
    }

    public sealed class PuzzleLevelValidationDiagnostic
    {
        public string Code { get; }
        public PuzzleLevelValidationSeverity Severity { get; }
        public string Message { get; }

        public PuzzleLevelValidationDiagnostic(
            string code,
            PuzzleLevelValidationSeverity severity,
            string message)
        {
            Code = code;
            Severity = severity;
            Message = message;
        }
    }

    public sealed class PuzzleLevelValidationStep
    {
        public string Name { get; }
        public PuzzleLevelValidationStepStatus Status { get; }
        public IReadOnlyList<string> Messages { get; }

        public PuzzleLevelValidationStep(
            string name,
            PuzzleLevelValidationStepStatus status,
            IReadOnlyList<string> messages = null
        )
        {
            Name = name;
            Status = status;
            Messages = messages ?? Array.Empty<string>();
        }
    }

    public sealed class PuzzleLevelValidationDetails
    {
        public IReadOnlyList<PuzzleLevelValidationDiagnostic> Diagnostics { get; }
        public IReadOnlyList<PuzzleLevelValidationStep> Steps { get; }

        public PuzzleLevelValidationDetails(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            IReadOnlyList<PuzzleLevelValidationStep> steps
        )
        {
            Diagnostics = diagnostics
                ?? Array.Empty<PuzzleLevelValidationDiagnostic>();
            Steps = steps ?? Array.Empty<PuzzleLevelValidationStep>();
        }
    }

    public sealed class PuzzleLevelValidationRequest
    {
        public string LevelId { get; }
        public TextAsset Asset { get; }
        public string SourceName { get; }
        public DataType DeclaredDataType { get; }
        public DataType EffectiveDataType { get; }
        public string ContentHash { get; }
        public byte[] Content { get; }

        public PuzzleLevelValidationRequest(
            string levelId,
            TextAsset asset,
            string sourceName,
            DataType declaredDataType,
            DataType effectiveDataType,
            string contentHash,
            byte[] content)
        {
            LevelId = levelId;
            Asset = asset;
            SourceName = sourceName;
            DeclaredDataType = declaredDataType;
            EffectiveDataType = effectiveDataType;
            ContentHash = contentHash;
            Content = content ?? Array.Empty<byte>();
        }
    }

    public sealed class PuzzleLevelValidationResult
    {
        public IReadOnlyList<PuzzleLevelValidationDiagnostic> Diagnostics { get; }
        public IReadOnlyList<PuzzleLevelValidationStep> Steps { get; }
        public string ContentHash { get; }
        public string ValidatorName { get; }

        public bool IsValid => !HasSeverity(PuzzleLevelValidationSeverity.Error);
        public bool HasWarnings => HasSeverity(PuzzleLevelValidationSeverity.Warning);

        public PuzzleLevelValidationResult(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            string contentHash,
            string validatorName,
            IReadOnlyList<PuzzleLevelValidationStep> steps = null)
        {
            Diagnostics = diagnostics ?? Array.Empty<PuzzleLevelValidationDiagnostic>();
            Steps = steps ?? Array.Empty<PuzzleLevelValidationStep>();
            ContentHash = contentHash;
            ValidatorName = validatorName;
        }

        private bool HasSeverity(PuzzleLevelValidationSeverity severity)
        {
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PuzzleLevelDeserializationResult
    {
        public string DisplayName { get; }
        public string Text { get; }

        public PuzzleLevelDeserializationResult(string displayName, string text)
        {
            DisplayName = displayName;
            Text = text;
        }
    }

    public interface IPuzzleLevelValidator
    {
        int Order { get; }

        bool CanValidate(PuzzleLevelValidationRequest request);

        IReadOnlyList<PuzzleLevelValidationDiagnostic> Validate(
            PuzzleLevelValidationRequest request);

        bool TryDeserialize(
            PuzzleLevelValidationRequest request,
            out PuzzleLevelDeserializationResult result,
            out string error);
    }

    public interface IPuzzleLevelDetailedValidator : IPuzzleLevelValidator
    {
        PuzzleLevelValidationDetails ValidateDetailed(
            PuzzleLevelValidationRequest request
        );
    }
}
