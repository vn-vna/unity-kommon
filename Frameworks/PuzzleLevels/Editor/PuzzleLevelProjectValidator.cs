using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Editor
{
    internal sealed class PuzzleLevelProjectValidator :
        IPreprocessBuildWithReport
    {
        #region Constants

        private const string CanonicalManagerPath
            = "Assets/Resources/PuzzleLevelManager.asset";
        private const string CanonicalConfigurationPath
            = "Assets/Resources/AsyncResourceLoaderConfiguration.asset";

        #endregion

        #region Private Fields

        private static readonly Regex TrailingNumberRegex = new Regex(
            @"(\d+)$",
            RegexOptions.Compiled);

        private static readonly Regex TemplateTokenRegex = new Regex(
            @"\{[^{}]+\}",
            RegexOptions.Compiled);

        #endregion

        #region Interfaces & Properties

        public int callbackOrder => 0;

        #endregion

        #region Public Methods

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidationResult result = ValidateProject();
            LogWarnings(result);
            if (result.Errors.Count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                "Puzzle level configuration validation failed:\n"
                + string.Join("\n", result.Errors));
        }

        [MenuItem("Tools/Puzzle Levels/Validate Project")]
        public static void ValidateFromMenu()
        {
            ValidationResult result = ValidateProject();
            LogWarnings(result);
            LogErrors(result);

            string summary = result.Errors.Count == 0
                ? $"Validation passed with {result.Warnings.Count} warning(s)."
                : $"Validation found {result.Errors.Count} error(s) and "
                    + $"{result.Warnings.Count} warning(s).";
            EditorUtility.DisplayDialog(
                "Puzzle Level Validation",
                summary,
                "OK");
        }

        #endregion

        #region Private Methods

        private static ValidationResult ValidateProject()
        {
            ValidationResult result = new ValidationResult();
            ValidateManagers(result);
            ValidateProviders(result);
            ValidateReferenceTables(result);
            ValidateOverrideConfigs(result);
            return result;
        }

        private static void ValidateManagers(ValidationResult result)
        {
            string[] guids = AssetDatabase.FindAssets(
                $"t:{nameof(PuzzleLevelManager)}");
            if (guids.Length == 0)
            {
                result.Errors.Add("No PuzzleLevelManager asset exists.");
                return;
            }

            if (guids.Length > 1)
            {
                result.Errors.Add(
                    $"Found {guids.Length} PuzzleLevelManager assets. "
                    + "Only one canonical singleton is supported.");
            }

            PuzzleLevelManager canonical
                = AssetDatabase.LoadAssetAtPath<PuzzleLevelManager>(
                    CanonicalManagerPath);
            if (canonical == null)
            {
                result.Errors.Add(
                    $"Canonical manager is missing at '{CanonicalManagerPath}'.");
            }
            else
            {
                ValidateManagerRegistration(canonical, result);
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PuzzleLevelManager manager
                    = AssetDatabase.LoadAssetAtPath<PuzzleLevelManager>(path);
                ValidateManagerProviders(manager, path, result);
            }
        }

        private static void ValidateManagerProviders(
            PuzzleLevelManager manager,
            string path,
            ValidationResult result)
        {
            if (manager == null)
            {
                return;
            }

            using SerializedObject serializedManager
                = new SerializedObject(manager);
            SerializedProperty providers
                = serializedManager.FindProperty("initialProviders");
            if (providers == null || providers.arraySize == 0)
            {
                result.Errors.Add(
                    $"Manager '{path}' has no resource providers.");
                return;
            }

            HashSet<string> configuredTags = GetConfiguredTags(
                serializedManager);

            for (int i = 0; i < providers.arraySize; i++)
            {
                ScriptableObject providerAsset = providers
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue as ScriptableObject;
                if (providerAsset == null)
                {
                    result.Errors.Add(
                        $"Manager '{path}' has a missing provider at index {i}.");
                    continue;
                }

                ValidateActiveProvider(
                    providerAsset,
                    path,
                    i,
                    result,
                    new HashSet<int>(),
                    configuredTags);
            }
        }

        private static void ValidateManagerRegistration(
            PuzzleLevelManager canonicalManager,
            ValidationResult result)
        {
            AsyncResourceLoadingConfiguration configuration
                = AssetDatabase.LoadAssetAtPath<
                    AsyncResourceLoadingConfiguration>(
                    CanonicalConfigurationPath);
            if (configuration == null)
            {
                result.Errors.Add(
                    $"Async resource configuration is missing at "
                    + $"'{CanonicalConfigurationPath}'.");
                return;
            }

            using SerializedObject serializedConfiguration
                = new SerializedObject(configuration);
            SerializedProperty managerAssets
                = serializedConfiguration.FindProperty("managerAssets");
            if (managerAssets == null)
            {
                result.Errors.Add(
                    "Async resource configuration has no managerAssets field.");
                return;
            }

            int canonicalReferenceCount = 0;
            Dictionary<Type, int> managerTypeIndices
                = new Dictionary<Type, int>();
            for (int i = 0; i < managerAssets.arraySize; i++)
            {
                ScriptableObject managerAsset = managerAssets
                    .GetArrayElementAtIndex(i)
                    .objectReferenceValue as ScriptableObject;
                if (managerAsset == null)
                {
                    result.Errors.Add(
                        $"Async resource configuration has a missing manager "
                        + $"at index {i}.");
                    continue;
                }

                if (managerAsset == canonicalManager)
                {
                    canonicalReferenceCount++;
                }

                if (managerAsset is not IResourceManager)
                {
                    result.Errors.Add(
                        $"Configuration entry {i} ('{managerAsset.name}') is "
                        + "not an IResourceManager.");
                    continue;
                }

                Type managerType = managerAsset.GetType();
                if (managerTypeIndices.TryGetValue(
                        managerType,
                        out int previousIndex))
                {
                    result.Errors.Add(
                        $"Configuration contains duplicate manager type "
                        + $"'{managerType.Name}' at indices {previousIndex} "
                        + $"and {i}.");
                }
                else
                {
                    managerTypeIndices[managerType] = i;
                }
            }

            if (canonicalReferenceCount != 1)
            {
                result.Errors.Add(
                    $"Canonical PuzzleLevelManager must appear exactly once "
                    + $"in '{CanonicalConfigurationPath}', but appears "
                    + $"{canonicalReferenceCount} time(s).");
            }
        }

        private static void ValidateActiveProvider(
            ScriptableObject providerAsset,
            string managerPath,
            int providerIndex,
            ValidationResult result,
            HashSet<int> validationStack,
            HashSet<string> configuredTags)
        {
            if (providerAsset is not IAsyncResourceProvider provider)
            {
                result.Errors.Add(
                    $"Manager '{managerPath}' provider {providerIndex} "
                    + $"('{providerAsset.name}') is not an async resource provider.");
                return;
            }

            int instanceId = providerAsset.GetInstanceID();
            if (!validationStack.Add(instanceId))
            {
                result.Errors.Add(
                    $"Provider graph for manager '{managerPath}' contains a "
                    + $"cycle at '{providerAsset.name}'.");
                return;
            }

            if (provider.ResourceLoadingTimeout <= 0f
                || float.IsNaN(provider.ResourceLoadingTimeout))
            {
                result.Errors.Add(
                    $"Active provider '{AssetDatabase.GetAssetPath(providerAsset)}' "
                    + "has a non-positive or NaN loading timeout.");
            }

            using SerializedObject serializedProvider
                = new SerializedObject(providerAsset);
            if (providerAsset is PuzzleLevelReferenceTableProvider)
            {
                ValidateRequiredAssetReference(
                    serializedProvider,
                    "_table",
                    providerAsset,
                    result);
            }

            else if (providerAsset is PuzzleLevelDownloadableProvider)
            {
                ValidateDownloadableProvider(
                    serializedProvider,
                    providerAsset,
                    result,
                    configuredTags);
            }

            if (providerAsset is PuzzleLevelCachedProvider)
            {
                SerializedProperty wrappedProvider = serializedProvider
                    .FindProperty("_wrappedProviderAsset");
                ScriptableObject wrappedAsset
                    = wrappedProvider?.objectReferenceValue as ScriptableObject;
                if (wrappedAsset == null)
                {
                    result.Errors.Add(
                        $"Active cached provider "
                        + $"'{AssetDatabase.GetAssetPath(providerAsset)}' has "
                        + "no wrapped provider.");
                }
                else
                {
                    ValidateActiveProvider(
                        wrappedAsset,
                        managerPath,
                        providerIndex,
                        result,
                        validationStack,
                        configuredTags);
                }
            }

#if UNITY_ADDRESSABLES
            if (providerAsset is PuzzleLevelAddressableProvider
                && !HasAddressableAssetSettings())
            {
                result.Errors.Add(
                    $"Active Addressables provider "
                    + $"'{AssetDatabase.GetAssetPath(providerAsset)}' requires "
                    + "AddressableAssetSettings.");
            }
#endif

            validationStack.Remove(instanceId);
        }

#if UNITY_ADDRESSABLES
        private static bool HasAddressableAssetSettings()
        {
            Type settingsType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings."
                + "AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            System.Reflection.PropertyInfo settingsProperty
                = settingsType?.GetProperty(
                    "Settings",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static);
            return settingsProperty?.GetValue(null) != null;
        }
#endif

        private static void ValidateRequiredAssetReference(
            SerializedObject serializedProvider,
            string propertyName,
            ScriptableObject providerAsset,
            ValidationResult result)
        {
            SerializedProperty property = serializedProvider.FindProperty(
                propertyName);
            if (property?.objectReferenceValue != null)
            {
                return;
            }

            result.Errors.Add(
                $"Active provider '{AssetDatabase.GetAssetPath(providerAsset)}' "
                + $"has no required '{propertyName}' reference.");
        }

        private static void ValidateDownloadableProvider(
            SerializedObject serializedProvider,
            ScriptableObject providerAsset,
            ValidationResult result,
            HashSet<string> configuredTags)
        {
            string assetPath = AssetDatabase.GetAssetPath(providerAsset);
            string baseUrl = serializedProvider.FindProperty("_baseUrl")
                ?.stringValue;
            string urlFormat = serializedProvider.FindProperty("_urlFormat")
                ?.stringValue;
            bool useCatalog = serializedProvider.FindProperty("_useCatalog")
                ?.boolValue ?? false;
            string catalogFileName = serializedProvider
                .FindProperty("_catalogFileName")
                ?.stringValue;
            bool forceRequiredCatalog = serializedProvider
                .FindProperty("_forceRequiredCatalog")
                ?.boolValue ?? false;

            if (string.IsNullOrWhiteSpace(urlFormat))
            {
                result.Errors.Add(
                    $"Active downloadable provider '{assetPath}' has no URL "
                    + "format.");
            }
            else if (!TryResolveAbsoluteUrl(
                    baseUrl,
                    urlFormat,
                    configuredTags,
                    out string downloadUrlError))
            {
                result.Errors.Add(
                    $"Active downloadable provider '{assetPath}' has invalid "
                    + $"download URL settings: {downloadUrlError}");
            }

            if (forceRequiredCatalog && !useCatalog)
            {
                result.Errors.Add(
                    $"Active downloadable provider '{assetPath}' requires a "
                    + "catalog but catalog loading is disabled.");
            }

            if (!useCatalog)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(catalogFileName))
            {
                result.Errors.Add(
                    $"Active downloadable provider '{assetPath}' enables "
                    + "catalog loading but has no catalog file name.");
                return;
            }

            if (!TryResolveAbsoluteUrl(
                    baseUrl,
                    catalogFileName,
                    configuredTags,
                    out string catalogUrlError))
            {
                result.Errors.Add(
                    $"Active downloadable provider '{assetPath}' has invalid "
                    + $"catalog URL settings: {catalogUrlError}");
            }
        }

        private static bool TryResolveAbsoluteUrl(
            string baseUrl,
            string relativeTemplate,
            HashSet<string> configuredTags,
            out string error)
        {
            string evaluatedBaseUrl = EvaluateUrlTemplate(
                baseUrl,
                configuredTags);
            string evaluatedRelativePath = EvaluateUrlTemplate(
                relativeTemplate,
                configuredTags);
            if (ContainsUnresolvedBraces(evaluatedBaseUrl)
                || ContainsUnresolvedBraces(evaluatedRelativePath))
            {
                error = "a URL template contains unmatched braces.";
                return false;
            }

            string combinedUrl;
            if (Uri.TryCreate(
                    evaluatedRelativePath,
                    UriKind.Absolute,
                    out Uri absoluteRelativeUri))
            {
                combinedUrl = absoluteRelativeUri.AbsoluteUri;
            }
            else if (string.IsNullOrWhiteSpace(evaluatedBaseUrl))
            {
                error = "the base URL is empty and the path is not absolute.";
                return false;
            }
            else
            {
                combinedUrl = evaluatedBaseUrl.TrimEnd('/')
                    + "/"
                    + evaluatedRelativePath.TrimStart('/');
            }

            if (!Uri.TryCreate(
                    combinedUrl,
                    UriKind.Absolute,
                    out Uri absoluteUri)
                || string.IsNullOrWhiteSpace(absoluteUri.Scheme))
            {
                error = $"'{combinedUrl}' is not an absolute URL.";
                return false;
            }

            if (!IsSupportedDownloadScheme(absoluteUri.Scheme))
            {
                error = $"URL scheme '{absoluteUri.Scheme}' is not supported.";
                return false;
            }

            error = null;
            return true;
        }

        private static string EvaluateUrlTemplate(
            string value,
            HashSet<string> configuredTags)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return TemplateTokenRegex.Replace(value.Trim(), match =>
            {
                string token = match.Value.Substring(
                    1,
                    match.Value.Length - 2);
                return string.Equals(token, "0", StringComparison.Ordinal)
                    || string.Equals(token, "id", StringComparison.Ordinal)
                    || configuredTags?.Contains(token) == true
                        ? "sample"
                        : match.Value;
            });
        }

        private static bool ContainsUnresolvedBraces(string value)
        {
            return !string.IsNullOrEmpty(value)
                && (value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0);
        }

        private static bool IsSupportedDownloadScheme(string scheme)
        {
            return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    scheme,
                    Uri.UriSchemeFile,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> GetConfiguredTags(
            SerializedObject serializedManager)
        {
            HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);
            SerializedProperty customTags = serializedManager.FindProperty(
                "_customTags");
            if (customTags == null)
            {
                return tags;
            }

            for (int i = 0; i < customTags.arraySize; i++)
            {
                string key = customTags.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("key")
                    ?.stringValue;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    tags.Add(key);
                }
            }

            return tags;
        }

        private static void ValidateReferenceTables(ValidationResult result)
        {
            string[] guids = AssetDatabase.FindAssets(
                $"t:{nameof(PuzzleLevelReferenceTable)}");
            foreach (string guid in guids)
            {
                string tablePath = AssetDatabase.GUIDToAssetPath(guid);
                PuzzleLevelReferenceTable table
                    = AssetDatabase.LoadAssetAtPath<PuzzleLevelReferenceTable>(
                        tablePath);
                if (table == null)
                {
                    continue;
                }

                ValidateReferenceTable(table, tablePath, result);
            }
        }

        private static void ValidateProviders(ValidationResult result)
        {
            HashSet<string> validatedPaths = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (Type providerType
                in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (providerType.IsAbstract
                    || providerType.ContainsGenericParameters
                    || !typeof(IAsyncResourceProvider).IsAssignableFrom(
                        providerType))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets(
                    $"t:{providerType.Name}");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!validatedPaths.Add(path))
                    {
                        continue;
                    }

                    ScriptableObject asset
                        = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (asset is not IAsyncResourceProvider provider
                        || provider.ResourceLoadingTimeout > 0f)
                    {
                        continue;
                    }

                    result.Errors.Add(
                        $"Provider '{path}' has a non-positive resource "
                        + $"loading timeout ({provider.ResourceLoadingTimeout}).");
                }
            }
        }

        private static void ValidateReferenceTable(
            PuzzleLevelReferenceTable table,
            string tablePath,
            ValidationResult result)
        {
            using SerializedObject serializedTable = new SerializedObject(table);
            SerializedProperty entries = serializedTable.FindProperty("_entries");
            if (entries == null || entries.arraySize == 0)
            {
                result.Warnings.Add(
                    $"Reference table '{tablePath}' contains no entries.");
                return;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> referencedAssetPaths = new HashSet<string>(
                StringComparer.Ordinal);
            HashSet<string> inventoryFolders = new HashSet<string>(
                StringComparer.Ordinal);

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string id = entry.FindPropertyRelative("Id")?.stringValue;
                Object asset
                    = entry.FindPropertyRelative("Asset")?.objectReferenceValue;
                SerializedProperty dataTypeProperty
                    = entry.FindPropertyRelative("DataType");

                ValidateEntryIdentity(
                    tablePath,
                    i,
                    id,
                    asset,
                    ids,
                    result);
                ValidateEntryDataType(
                    tablePath,
                    i,
                    dataTypeProperty,
                    result);

                if (asset == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(asset);
                referencedAssetPaths.Add(assetPath);
                string folder = Path.GetDirectoryName(assetPath)
                    ?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder))
                {
                    inventoryFolders.Add(folder);
                }

                ValidateIdFileAlignment(id, asset.name, tablePath, result);
            }

            ValidateOrphanedAssets(
                inventoryFolders,
                referencedAssetPaths,
                tablePath,
                result);
        }

        private static void ValidateEntryIdentity(
            string tablePath,
            int index,
            string id,
            Object asset,
            HashSet<string> ids,
            ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                result.Errors.Add(
                    $"Reference table '{tablePath}' entry {index} has no ID.");
            }
            else if (!ids.Add(id))
            {
                result.Errors.Add(
                    $"Reference table '{tablePath}' contains duplicate ID "
                    + $"'{id}'.");
            }

            if (asset == null)
            {
                result.Errors.Add(
                    $"Reference table '{tablePath}' entry {index} has no asset.");
            }
        }

        private static void ValidateIdFileAlignment(
            string id,
            string assetName,
            string tablePath,
            ValidationResult result)
        {
            if (!TryGetTrailingNumber(id, out int idNumber)
                || !TryGetTrailingNumber(assetName, out int assetNumber)
                || idNumber == assetNumber)
            {
                return;
            }

            result.Warnings.Add(
                $"Reference table '{tablePath}' maps logical ID '{id}' to "
                + $"file '{assetName}', whose numeric suffix differs.");
        }

        private static void ValidateEntryDataType(
            string tablePath,
            int index,
            SerializedProperty dataTypeProperty,
            ValidationResult result)
        {
            int serializedValue = dataTypeProperty?.intValue
                ?? (int)DataType.Unknown;
            if (!Enum.IsDefined(typeof(DataType), serializedValue))
            {
                result.Errors.Add(
                    $"Reference table '{tablePath}' entry {index} has invalid "
                    + $"data type value {serializedValue}.");
                return;
            }

            DataType dataType = (DataType)serializedValue;
            if (dataType != DataType.Unknown)
            {
                return;
            }

            result.Warnings.Add(
                $"Reference table '{tablePath}' entry {index} has no explicit "
                + "data type and will default to Text.");
        }

        private static bool TryGetTrailingNumber(
            string value,
            out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = TrailingNumberRegex.Match(value);
            return match.Success && int.TryParse(match.Groups[1].Value, out number);
        }

        private static void ValidateOrphanedAssets(
            HashSet<string> inventoryFolders,
            HashSet<string> referencedAssetPaths,
            string tablePath,
            ValidationResult result)
        {
            if (inventoryFolders.Count == 0)
            {
                return;
            }

            string[] folders = new string[inventoryFolders.Count];
            inventoryFolders.CopyTo(folders);
            string[] assetGuids = AssetDatabase.FindAssets(
                "t:TextAsset",
                folders);
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (referencedAssetPaths.Contains(assetPath))
                {
                    continue;
                }

                result.Warnings.Add(
                    $"Text asset '{assetPath}' is not referenced by table "
                    + $"'{tablePath}'.");
            }
        }

        private static void ValidateOverrideConfigs(ValidationResult result)
        {
            string[] guids = AssetDatabase.FindAssets(
                $"t:{nameof(PuzzleLevelOverrideConfig)}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PuzzleLevelOverrideConfig config
                    = AssetDatabase.LoadAssetAtPath<PuzzleLevelOverrideConfig>(
                        path);
                if (config == null)
                {
                    continue;
                }

                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < config.Entries.Count; i++)
                {
                    PuzzleLevelOverrideEntry entry = config.Entries[i];
                    ValidateEntryIdentity(
                        path,
                        i,
                        entry.LevelId,
                        entry.OverrideAsset,
                        ids,
                        result);
                    ValidateConfiguredDataType(
                        path,
                        i,
                        entry.DataType,
                        result);
                }
            }
        }

        private static void ValidateConfiguredDataType(
            string assetPath,
            int index,
            DataType dataType,
            ValidationResult result)
        {
            if (!Enum.IsDefined(typeof(DataType), dataType))
            {
                result.Errors.Add(
                    $"Configuration '{assetPath}' entry {index} has invalid "
                    + $"data type value {(int)dataType}.");
                return;
            }

            if (dataType == DataType.Unknown)
            {
                result.Warnings.Add(
                    $"Configuration '{assetPath}' entry {index} has no "
                    + "explicit data type and will default to Text.");
            }
        }

        private static void LogWarnings(ValidationResult result)
        {
            foreach (string warning in result.Warnings)
            {
                QuickLog.Warning<PuzzleLevelProjectValidator>(warning);
            }
        }

        private static void LogErrors(ValidationResult result)
        {
            foreach (string error in result.Errors)
            {
                QuickLog.Error<PuzzleLevelProjectValidator>(error);
            }
        }

        #endregion

        #region Nested Types

        private sealed class ValidationResult
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
        }

        #endregion
    }
}
