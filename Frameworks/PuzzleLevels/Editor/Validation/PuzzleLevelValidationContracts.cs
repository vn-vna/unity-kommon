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
        public string ContentHash { get; }
        public string ValidatorName { get; }

        public bool IsValid => !HasSeverity(PuzzleLevelValidationSeverity.Error);
        public bool HasWarnings => HasSeverity(PuzzleLevelValidationSeverity.Warning);

        public PuzzleLevelValidationResult(
            IReadOnlyList<PuzzleLevelValidationDiagnostic> diagnostics,
            string contentHash,
            string validatorName)
        {
            Diagnostics = diagnostics ?? Array.Empty<PuzzleLevelValidationDiagnostic>();
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
}
