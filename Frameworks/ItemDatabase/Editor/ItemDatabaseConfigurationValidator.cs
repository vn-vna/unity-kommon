using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using CompilationAssembly = UnityEditor.Compilation.Assembly;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    internal enum ItemDatabaseDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class ItemDatabaseDiagnostic
    {
        internal string Code { get; }

        internal ItemDatabaseDiagnosticSeverity Severity { get; }

        internal string Message { get; }

        internal Object Context { get; }

        internal ItemDatabaseDiagnostic(
            string code,
            ItemDatabaseDiagnosticSeverity severity,
            string message,
            Object context = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            Context = context;
        }
    }

    internal static class ItemDatabaseConfigurationValidator
    {
        private const string CanonicalAssetPath
            = "Assets/Resources/Integration/Managers/ItemDatabaseConfiguration.asset";

        private static readonly Dictionary<Type, bool> PlayerAvailabilityCache
            = new Dictionary<Type, bool>();

        private static Dictionary<string, CompilationAssembly> _playerAssemblies;
        private static BuildTarget _playerAssembliesBuildTarget;

        internal static IReadOnlyList<ItemDatabaseDiagnostic> Validate(
            ItemDatabaseConfiguration config)
        {
            var diagnostics = new List<ItemDatabaseDiagnostic>();
            if (config == null)
            {
                diagnostics.Add(Info(
                    "CONFIG_MISSING",
                    "Item Database is disabled because no configuration asset exists."
                ));
                return diagnostics;
            }

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.Equals(
                    assetPath,
                    CanonicalAssetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "CONFIG_PATH",
                    $"Configuration must be stored at '{CanonicalAssetPath}', not '{assetPath}'.",
                    config
                ));
            }

            ValidateDefinitions(config, assetPath, diagnostics);
            ValidateDefaultTags(config, assetPath, diagnostics);
            ValidateMiddlewares(config, diagnostics);
            ValidateTagRegistry(diagnostics);
            ValidateSubAssetGraph(config, assetPath, diagnostics);
            return diagnostics;
        }

        internal static bool HasErrors(
            IReadOnlyList<ItemDatabaseDiagnostic> diagnostics)
        {
            return diagnostics != null
                && diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ItemDatabaseDiagnosticSeverity.Error);
        }

        private static void ValidateDefinitions(
            ItemDatabaseConfiguration config,
            string assetPath,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            var definitionsById = new Dictionary<string, List<ItemDefinition>>(
                StringComparer.Ordinal
            );
            var metadataOwners = new Dictionary<ItemMetadata, ItemDefinition>();
            var tagOwners = new Dictionary<TagDefinition, ItemDefinition>();
            var defaultTags = new HashSet<TagDefinition>(
                config.TagDefinitions.Where(tag => tag != null)
            );

            foreach (ItemDefinition definition in config.ItemDefinitions)
            {
                if (definition == null)
                {
                    diagnostics.Add(Error(
                        "DEFINITION_MISSING",
                        "Definitions list contains a missing reference.",
                        config
                    ));
                    continue;
                }

                ValidateObjectPath(
                    definition,
                    assetPath,
                    "DEFINITION_EXTERNAL",
                    diagnostics
                );

                string itemId = definition.ItemId;
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    diagnostics.Add(Error(
                        "DEFINITION_ID_EMPTY",
                        $"Definition '{definition.name}' has an empty item ID.",
                        definition
                    ));
                }
                else
                {
                    if (!definitionsById.TryGetValue(
                            itemId,
                            out List<ItemDefinition> definitions))
                    {
                        definitions = new List<ItemDefinition>();
                        definitionsById.Add(itemId, definitions);
                    }

                    definitions.Add(definition);
                }

                if (definition.Metadata == null)
                {
                    diagnostics.Add(Error(
                        "METADATA_MISSING",
                        $"Definition '{DisplayId(definition)}' has no metadata.",
                        definition
                    ));
                    continue;
                }

                ValidateObjectPath(
                    definition.Metadata,
                    assetPath,
                    "METADATA_EXTERNAL",
                    diagnostics
                );
                if (metadataOwners.TryGetValue(
                        definition.Metadata,
                        out ItemDefinition metadataOwner))
                {
                    diagnostics.Add(Error(
                        "METADATA_SHARED",
                        $"Definitions '{DisplayId(metadataOwner)}' and "
                        + $"'{DisplayId(definition)}' share the same metadata object.",
                        definition.Metadata
                    ));
                }
                else
                {
                    metadataOwners.Add(definition.Metadata, definition);
                }

                ValidateMetadata(definition, diagnostics);
                var tagTypes = new HashSet<Type>();
                foreach (TagDefinition tag in definition.Tags)
                {
                    if (tag == null)
                    {
                        diagnostics.Add(Error(
                            "TAG_REFERENCE_MISSING",
                            $"Definition '{DisplayId(definition)}' contains a missing tag reference.",
                            definition
                        ));
                        continue;
                    }

                    Type tagType = tag.GetType();
                    ValidateTagPlayerAvailability(
                        tagType,
                        tag,
                        diagnostics
                    );
                    if (!tagTypes.Add(tagType))
                    {
                        diagnostics.Add(Error(
                            "TAG_TYPE_DUPLICATE",
                            $"Definition '{DisplayId(definition)}' contains duplicate "
                            + $"tag type '{tagType.Name}'.",
                            tag
                        ));
                    }

                    ValidateObjectPath(
                        tag,
                        assetPath,
                        "TAG_EXTERNAL",
                        diagnostics
                    );
                    ValidateTagValues(tag, definition, diagnostics);

                    if (defaultTags.Contains(tag))
                    {
                        diagnostics.Add(Warning(
                            "SHARED_DEFAULT_REFERENCE",
                            $"Definition '{DisplayId(definition)}' directly references "
                            + $"the '{tagType.Name}' default template. Clone it before editing or clearing defaults.",
                            tag
                        ));
                    }

                    if (tagOwners.TryGetValue(tag, out ItemDefinition tagOwner)
                        && tagOwner != definition)
                    {
                        diagnostics.Add(Error(
                            "TAG_INSTANCE_SHARED",
                            $"Definitions '{DisplayId(tagOwner)}' and "
                            + $"'{DisplayId(definition)}' share tag instance '{tag.name}'.",
                            tag
                        ));
                    }
                    else
                    {
                        tagOwners[tag] = definition;
                    }
                }

                ValidateOwnership(definition, diagnostics);
                ValidateNames(definition, diagnostics);
            }

            foreach (KeyValuePair<string, List<ItemDefinition>> entry in definitionsById)
            {
                if (entry.Value.Count < 2) continue;

                foreach (ItemDefinition duplicate in entry.Value)
                {
                    diagnostics.Add(Error(
                        "DEFINITION_ID_DUPLICATE",
                        $"Item ID '{entry.Key}' is used by {entry.Value.Count} definitions.",
                        duplicate
                    ));
                }
            }
        }

        private static void ValidateDefaultTags(
            ItemDatabaseConfiguration config,
            string assetPath,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            var types = new HashSet<Type>();
            foreach (TagDefinition tag in config.TagDefinitions)
            {
                if (tag == null)
                {
                    diagnostics.Add(Error(
                        "DEFAULT_TAG_MISSING",
                        "Default tag list contains a missing reference.",
                        config
                    ));
                    continue;
                }

                if (!types.Add(tag.GetType()))
                {
                    diagnostics.Add(Error(
                        "DEFAULT_TAG_DUPLICATE",
                        $"More than one default template exists for '{tag.GetType().Name}'.",
                        tag
                    ));
                }

                ValidateTagPlayerAvailability(
                    tag.GetType(),
                    tag,
                    diagnostics
                );

                ValidateObjectPath(
                    tag,
                    assetPath,
                    "DEFAULT_TAG_EXTERNAL",
                    diagnostics
                );
                ValidateTagValues(tag, null, diagnostics);
            }
        }

        private static void ValidateMiddlewares(
            ItemDatabaseConfiguration config,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            var types = new HashSet<Type>();
            foreach (string typeName in config.MiddlewareTypeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_NAME_EMPTY",
                        "Middleware list contains an empty type name.",
                        config
                    ));
                    continue;
                }

                Type type = Type.GetType(typeName);
                if (type == null)
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_TYPE_MISSING",
                        $"Middleware type cannot be resolved: '{typeName}'.",
                        config
                    ));
                    continue;
                }

                if (!types.Add(type))
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_DUPLICATE",
                        $"Middleware '{type.FullName}' is configured more than once.",
                        config
                    ));
                }

                if (!IsAvailableInPlayer(type))
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_EDITOR_ONLY",
                        $"Middleware '{type.FullName}' is not compiled into players.",
                        config
                    ));
                }

                if (type.IsAbstract
                    || type.ContainsGenericParameters
                    || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_NOT_CONSTRUCTIBLE",
                        $"Middleware '{type.FullName}' must be concrete and have a public parameterless constructor.",
                        config
                    ));
                }

                if (type.GetCustomAttribute<ItemDatabaseMiddlewareAttribute>() == null)
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_ATTRIBUTE_MISSING",
                        $"Middleware '{type.FullName}' is missing [ItemDatabaseMiddleware].",
                        config
                    ));
                }

                if (!InventoryMiddlewarePipeline.ImplementsSupportedHook(type))
                {
                    diagnostics.Add(Error(
                        "MIDDLEWARE_HOOK_MISSING",
                        $"Middleware '{type.FullName}' does not implement a supported hook.",
                        config
                    ));
                }
            }
        }

        private static void ValidateTagPlayerAvailability(
            Type tagType,
            Object context,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            if (IsAvailableInPlayer(tagType)) return;

            diagnostics.Add(Error(
                "TAG_EDITOR_ONLY",
                $"Tag '{tagType.FullName}' is not compiled into players.",
                context
            ));
        }

        private static void ValidateTagRegistry(
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            try
            {
                TagDataRegistry.EnsureInitialized();
                foreach (Type type in TypeCache.GetTypesDerivedFrom<TagDefinition>())
                {
                    if (type.IsAbstract || !IsAvailableInPlayer(type)) continue;
                    string id = TagDataRegistry.GetPersistentId(type);
                    Type dataType = TagDataRegistry.GetDataType(type);
                    if (dataType != null && string.IsNullOrWhiteSpace(id))
                    {
                        diagnostics.Add(Error(
                            "TAG_ID_MISSING",
                            $"Tag type '{type.FullName}' has no persistent ID."
                        ));
                    }

                    if (dataType != null && !IsAvailableInPlayer(dataType))
                    {
                        diagnostics.Add(Error(
                            "TAG_DATA_EDITOR_ONLY",
                            $"Tag data type '{dataType.FullName}' mapped to "
                            + $"'{type.FullName}' is not compiled into players."
                        ));
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error(
                    "TAG_REGISTRY_INVALID",
                    exception.Message
                ));
            }
        }

        private static void ValidateSubAssetGraph(
            ItemDatabaseConfiguration config,
            string assetPath,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            var reachable = new HashSet<Object> { config };
            foreach (ItemDefinition definition in config.ItemDefinitions)
            {
                if (definition == null) continue;
                reachable.Add(definition);
                if (definition.Metadata == null) continue;
                reachable.Add(definition.Metadata);
                foreach (TagDefinition tag in definition.Tags)
                {
                    if (tag != null) reachable.Add(tag);
                }
            }

            foreach (TagDefinition tag in config.TagDefinitions)
            {
                if (tag != null) reachable.Add(tag);
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset == null || reachable.Contains(asset)) continue;
                if (asset is ItemDefinition
                    || asset is ItemMetadata
                    || asset is TagDefinition)
                {
                    diagnostics.Add(Warning(
                        "ORPHAN_SUB_ASSET",
                        $"Unreferenced Item Database sub-asset '{asset.name}' "
                        + "is retained to protect incoming references.",
                        asset
                    ));
                }
            }
        }

        private static void ValidateMetadata(
            ItemDefinition definition,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            string extras = definition.Metadata.Extras;
            if (string.IsNullOrWhiteSpace(extras)) return;

            try
            {
                JsonUtility.FromJson<JsonProbe>(extras);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error(
                    "METADATA_JSON_INVALID",
                    $"Definition '{DisplayId(definition)}' has invalid extras JSON: "
                    + exception.Message,
                    definition.Metadata
                ));
            }
        }

        private static void ValidateTagValues(
            TagDefinition tag,
            ItemDefinition definition,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            string owner = definition != null
                ? $" on '{DisplayId(definition)}'"
                : string.Empty;
            ValidateOwnershipName(tag, diagnostics);
            if (tag is StackableTag stackable && stackable.MaxStack <= 0)
            {
                diagnostics.Add(Error(
                    "STACK_CAPACITY_INVALID",
                    $"StackableTag{owner} has MaxStack {stackable.MaxStack}; it must be at least 1.",
                    tag
                ));
            }

            if (tag is WeaponTag weapon && weapon.AtkRange.x > weapon.AtkRange.y)
            {
                diagnostics.Add(Error(
                    "WEAPON_RANGE_INVALID",
                    $"WeaponTag{owner} has an inverted attack range.",
                    tag
                ));
            }

            if (tag is ArmorTag armor
                && (armor.HpRange.x > armor.HpRange.y
                    || armor.DefRange.x > armor.DefRange.y))
            {
                diagnostics.Add(Error(
                    "ARMOR_RANGE_INVALID",
                    $"ArmorTag{owner} has an inverted stat range.",
                    tag
                ));
            }
        }

        private static void ValidateOwnership(
            ItemDefinition definition,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (TagDefinition tag in definition.Tags)
            {
                if (tag == null) continue;
                string owner = ItemDatabase.GetTagOwner(tag.GetType());
                if (!string.IsNullOrEmpty(owner)) owners.Add(owner);
            }

            if (owners.Count > 1)
            {
                diagnostics.Add(Error(
                    "OWNERSHIP_CONFLICT",
                    $"Definition '{DisplayId(definition)}' is owned by multiple modules: "
                    + string.Join(", ", owners),
                    definition
                ));
            }
        }

        private static void ValidateOwnershipName(
            TagDefinition tag,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            var ownership = tag.GetType()
                .GetCustomAttribute<OwnedByModuleAttribute>();
            if (ownership == null
                || !string.IsNullOrWhiteSpace(ownership.ModuleName))
            {
                return;
            }

            diagnostics.Add(Error(
                "OWNERSHIP_NAME_MISSING",
                $"Tag '{tag.GetType().FullName}' declares an empty owner "
                + "module name.",
                tag
            ));
        }

        private static void ValidateNames(
            ItemDefinition definition,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(definition.ItemId)) return;

            if (!definition.name.Contains(definition.ItemId))
            {
                diagnostics.Add(Warning(
                    "DEFINITION_NAME_STALE",
                    $"Definition asset name '{definition.name}' does not include ID '{definition.ItemId}'.",
                    definition
                ));
            }

            if (definition.Metadata != null
                && !definition.Metadata.name.Contains(definition.ItemId))
            {
                diagnostics.Add(Warning(
                    "METADATA_NAME_STALE",
                    $"Metadata asset name '{definition.Metadata.name}' does not include ID '{definition.ItemId}'.",
                    definition.Metadata
                ));
            }
        }

        private static void ValidateObjectPath(
            Object target,
            string expectedPath,
            string code,
            List<ItemDatabaseDiagnostic> diagnostics)
        {
            string targetPath = AssetDatabase.GetAssetPath(target);
            if (!string.Equals(
                    targetPath,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    code,
                    $"'{target.name}' belongs to '{targetPath}', expected '{expectedPath}'.",
                    target
                ));
            }
        }

        internal static bool IsAvailableInPlayer(Type type)
        {
            if (type == null) return false;
            EnsurePlayerAssemblyCache();
            if (PlayerAvailabilityCache.TryGetValue(
                    type,
                    out bool cachedAvailability))
            {
                return cachedAvailability;
            }

            if (!_playerAssemblies.TryGetValue(
                    type.Assembly.GetName().Name,
                    out CompilationAssembly playerAssembly))
            {
                return IsPrecompiledAssemblyAvailable(
                    type,
                    GetCommonPlayerDefines()
                );
            }

            bool isAvailable = IsTypeDeclarationAvailable(
                type,
                playerAssembly
            );
            PlayerAvailabilityCache[type] = isAvailable;
            return isAvailable;
        }

        private static void EnsurePlayerAssemblyCache()
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings
                .activeBuildTarget;
            if (_playerAssemblies != null
                && _playerAssembliesBuildTarget == activeBuildTarget)
            {
                return;
            }

            _playerAssemblies = CompilationPipeline
                .GetAssemblies(AssembliesType.Player)
                .ToDictionary(
                    assembly => assembly.name,
                    StringComparer.Ordinal
                );
            _playerAssembliesBuildTarget = activeBuildTarget;
            PlayerAvailabilityCache.Clear();
        }

        private static string[] GetCommonPlayerDefines()
        {
            CompilationAssembly[] assemblies = _playerAssemblies.Values
                .ToArray();
            if (assemblies.Length == 0) return Array.Empty<string>();

            var commonDefines = new HashSet<string>(
                assemblies[0].defines ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
            for (int index = 1; index < assemblies.Length; index++)
            {
                commonDefines.IntersectWith(
                    assemblies[index].defines ?? Array.Empty<string>()
                );
            }

            return commonDefines.ToArray();
        }

        private static bool IsDeclarationAvailableInNamespace(
            string source,
            string typeName,
            string expectedNamespace,
            IEnumerable<string> playerDefines)
        {
            return IsDeclarationAvailable(
                source,
                typeName,
                playerDefines,
                expectedNamespace ?? string.Empty,
                out _
            );
        }

        private static bool IsTypeDeclarationAvailable(
            Type type,
            CompilationAssembly playerAssembly)
        {
            if (playerAssembly.sourceFiles == null
                || playerAssembly.sourceFiles.Length == 0)
            {
                return false;
            }

            string[] playerDefines = playerAssembly.defines
                ?? Array.Empty<string>();
            Type rootType = type;
            while (rootType.DeclaringType != null)
            {
                rootType = rootType.DeclaringType;
            }

            string[] boundSourceFiles = playerAssembly.sourceFiles
                .Where(sourcePath => IsScriptForType(sourcePath, rootType))
                .ToArray();
            bool hasBoundSource = boundSourceFiles.Length > 0;
            IEnumerable<string> sourceFiles = hasBoundSource
                ? boundSourceFiles
                : playerAssembly.sourceFiles.OrderByDescending(sourcePath =>
                    string.Equals(
                        Path.GetFileNameWithoutExtension(sourcePath),
                        rootType.Name,
                        StringComparison.Ordinal
                    ));
            foreach (string sourcePath in sourceFiles)
            {
                string absolutePath = GetAbsoluteSourcePath(sourcePath);
                if (!File.Exists(absolutePath)) continue;

                string source;
                try
                {
                    source = File.ReadAllText(absolutePath);
                }
                catch
                {
                    continue;
                }

                bool declarationIsAvailable
                    = IsDeclarationAvailableInNamespace(
                        source,
                        type.Name,
                        type.Namespace ?? string.Empty,
                        playerDefines
                    );
                if (declarationIsAvailable)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPrecompiledAssemblyAvailable(
            Type type,
            string[] playerDefines)
        {
            if (type == null) return false;

            Type metadataType = type.IsGenericType
                ? type.GetGenericTypeDefinition()
                : type;
            string assemblyName = metadataType.Assembly.GetName().Name;
            string typeFullName = metadataType.FullName;
            if (string.IsNullOrEmpty(assemblyName)
                || string.IsNullOrEmpty(typeFullName))
            {
                return false;
            }

            foreach (PluginImporter importer in PluginImporter.GetAllImporters())
            {
                if (importer == null || importer.isNativePlugin) continue;
                if (!ImporterMatchesAssembly(importer, assemblyName))
                {
                    continue;
                }

                BuildTarget buildTarget = EditorUserBuildSettings
                    .activeBuildTarget;
                bool platformIsCompatible;
                if (importer.GetCompatibleWithAnyPlatform())
                {
                    platformIsCompatible = !importer
                        .GetExcludeFromAnyPlatform(buildTarget);
                }
                else
                {
                    platformIsCompatible = importer
                        .GetCompatibleWithPlatform(buildTarget);
                }

                if (!platformIsCompatible) continue;

                string[] defineConstraints = importer.DefineConstraints
                    ?? Array.Empty<string>();
                bool definesAreCompatible = CompilationPipeline
                    .IsDefineConstraintsCompatible(
                        playerDefines ?? Array.Empty<string>(),
                        defineConstraints
                    );
                if (!definesAreCompatible) continue;

                string absolutePath = GetAbsoluteSourcePath(
                    importer.assetPath
                );
                if (AssemblyFileDefinesType(absolutePath, typeFullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImporterMatchesAssembly(
            PluginImporter importer,
            string expectedAssemblyName)
        {
            string absolutePath = GetAbsoluteSourcePath(importer.assetPath);
            if (File.Exists(absolutePath))
            {
                try
                {
                    var assemblyName = System.Reflection.AssemblyName
                        .GetAssemblyName(absolutePath);
                    return string.Equals(
                        assemblyName.Name,
                        expectedAssemblyName,
                        StringComparison.OrdinalIgnoreCase
                    );
                }
                catch
                {
                }
            }

            return string.Equals(
                Path.GetFileNameWithoutExtension(importer.assetPath),
                expectedAssemblyName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool AssemblyFileDefinesType(
            string absolutePath,
            string typeFullName)
        {
            if (string.IsNullOrEmpty(absolutePath)
                || string.IsNullOrEmpty(typeFullName)
                || !File.Exists(absolutePath))
            {
                return false;
            }

            string cecilTypeFullName = typeFullName.Replace('+', '/');
            try
            {
                using (var assemblyDefinition
                    = Mono.Cecil.AssemblyDefinition.ReadAssembly(absolutePath))
                {
                    foreach (Mono.Cecil.ModuleDefinition module
                             in assemblyDefinition.Modules)
                    {
                        bool containsDefinition = module.GetTypes().Any(
                            typeDefinition => string.Equals(
                                typeDefinition.FullName,
                                cecilTypeFullName,
                                StringComparison.Ordinal
                            )
                        );
                        if (containsDefinition) return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool IsScriptForType(string sourcePath, Type rootType)
        {
            string assetPath = GetAssetSourcePath(sourcePath);
            if (string.IsNullOrEmpty(assetPath)) return false;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                assetPath
            );
            return script != null && script.GetClass() == rootType;
        }

        private static string GetAbsoluteSourcePath(string sourcePath)
        {
            if (Path.IsPathRooted(sourcePath)) return sourcePath;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, sourcePath));
        }

        private static string GetAssetSourcePath(string sourcePath)
        {
            if (!Path.IsPathRooted(sourcePath))
            {
                return sourcePath.Replace('\\', '/');
            }

            string projectRoot = Path.GetFullPath(
                Path.GetDirectoryName(Application.dataPath)
            ).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string absolutePath = Path.GetFullPath(sourcePath);
            if (!absolutePath.StartsWith(
                    projectRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return absolutePath
                .Substring(projectRoot.Length + 1)
                .Replace('\\', '/');
        }

        private static bool IsDeclarationAvailable(
            string source,
            string typeName,
            IEnumerable<string> playerDefines,
            out bool declarationFound)
        {
            return IsDeclarationAvailable(
                source,
                typeName,
                playerDefines,
                expectedNamespace: null,
                out declarationFound
            );
        }

        private static bool IsDeclarationAvailable(
            string source,
            string typeName,
            IEnumerable<string> playerDefines,
            string expectedNamespace,
            out bool declarationFound)
        {
            declarationFound = false;
            if (string.IsNullOrEmpty(source)
                || string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            var defines = new HashSet<string>(
                playerDefines ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
            source = StripCommentsAndLiterals(source)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            NamespaceSourceRange[] namespaceRanges = expectedNamespace != null
                ? FindNamespaceRanges(source)
                : null;
            string declarationPattern =
                @"^\s*(?:\[[^\]\r\n]+\]\s*)*"
                + @"(?:(?:public|internal|private|protected|abstract|sealed|"
                + @"static|partial|new|readonly|ref|file|unsafe)\s+)*"
                + @"(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+"
                + Regex.Escape(typeName)
                + @"\b";
            var conditionalScopes = new List<ConditionalScope>();
            string[] lines = source.Split('\n');
            int lineOffset = 0;
            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                bool isDirective = TryApplyConditionalDirective(
                        trimmed,
                        conditionalScopes,
                        defines);
                Match declarationMatch = isDirective
                    ? Match.Empty
                    : Regex.Match(line, declarationPattern);
                bool namespaceMatches = declarationMatch.Success
                    && IsPositionInExpectedNamespace(
                        lineOffset + declarationMatch.Index,
                        expectedNamespace,
                        namespaceRanges
                    );
                if (namespaceMatches)
                {
                    declarationFound = true;
                    if (conditionalScopes.All(scope => scope.IsActive))
                    {
                        return true;
                    }
                }

                lineOffset += line.Length + 1;
            }

            return false;
        }

        private static bool IsPositionInExpectedNamespace(
            int position,
            string expectedNamespace,
            NamespaceSourceRange[] namespaceRanges)
        {
            if (expectedNamespace == null) return true;

            NamespaceSourceRange enclosingNamespace = namespaceRanges
                .Where(candidate => candidate.Range.Contains(position))
                .OrderBy(candidate => candidate.Range.Length)
                .FirstOrDefault();
            string actualNamespace = enclosingNamespace.Name ?? string.Empty;
            return string.Equals(
                actualNamespace,
                expectedNamespace,
                StringComparison.Ordinal
            );
        }

        private static NamespaceSourceRange[] FindNamespaceRanges(string source)
        {
            const string namespacePattern =
                @"\bnamespace\s+(?<name>(?:global::)?[A-Za-z_]\w*"
                + @"(?:\s*\.\s*[A-Za-z_]\w*)*)\s*(?<terminator>[;{])";
            var discoveredRanges = new List<NamespaceSourceRange>();
            foreach (Match match in Regex.Matches(source, namespacePattern))
            {
                string declaredNamespace = Regex.Replace(
                        match.Groups["name"].Value,
                        @"\s+",
                        string.Empty
                    )
                    .Replace("global::", string.Empty);

                Group terminator = match.Groups["terminator"];
                SourceRange range;
                if (terminator.Value == ";")
                {
                    range = new SourceRange(
                        match.Index + match.Length,
                        source.Length
                    );
                }
                else
                {
                    int closingBrace = FindMatchingBrace(
                        source,
                        terminator.Index
                    );
                    if (closingBrace <= terminator.Index) continue;
                    range = new SourceRange(
                        terminator.Index + 1,
                        closingBrace
                    );
                }

                NamespaceSourceRange parent = discoveredRanges
                    .Where(candidate => candidate.Range.Contains(match.Index))
                    .OrderBy(candidate => candidate.Range.Length)
                    .FirstOrDefault();
                string effectiveNamespace = string.IsNullOrEmpty(parent.Name)
                    ? declaredNamespace
                    : $"{parent.Name}.{declaredNamespace}";
                discoveredRanges.Add(new NamespaceSourceRange(
                    effectiveNamespace,
                    range
                ));
            }

            return discoveredRanges.ToArray();
        }

        private static int FindMatchingBrace(string source, int openingBrace)
        {
            int depth = 0;
            for (int index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string StripCommentsAndLiterals(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            var builder = new StringBuilder(source.Length);
            LexicalState state = LexicalState.Code;
            bool escaped = false;
            for (int index = 0; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length
                    ? source[index + 1]
                    : '\0';

                if (state == LexicalState.LineComment)
                {
                    AppendMaskedCharacter(builder, current);
                    if (current == '\n') state = LexicalState.Code;
                    continue;
                }

                if (state == LexicalState.BlockComment)
                {
                    AppendMaskedCharacter(builder, current);
                    if (current == '*' && next == '/')
                    {
                        AppendMaskedCharacter(builder, next);
                        index++;
                        state = LexicalState.Code;
                    }

                    continue;
                }

                if (state == LexicalState.VerbatimString)
                {
                    AppendMaskedCharacter(builder, current);
                    if (current != '"') continue;
                    if (next == '"')
                    {
                        AppendMaskedCharacter(builder, next);
                        index++;
                        continue;
                    }

                    state = LexicalState.Code;
                    continue;
                }

                if (state == LexicalState.String
                    || state == LexicalState.Character)
                {
                    AppendMaskedCharacter(builder, current);
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if ((state == LexicalState.String && current == '"')
                        || (state == LexicalState.Character && current == '\''))
                    {
                        state = LexicalState.Code;
                    }

                    continue;
                }

                if (current == '/' && next == '/')
                {
                    builder.Append("  ");
                    index++;
                    state = LexicalState.LineComment;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    builder.Append("  ");
                    index++;
                    state = LexicalState.BlockComment;
                    continue;
                }

                if (current == '@' && next == '"')
                {
                    builder.Append("  ");
                    index++;
                    state = LexicalState.VerbatimString;
                    continue;
                }

                if (current == '"' || current == '\'')
                {
                    builder.Append(' ');
                    state = current == '"'
                        ? LexicalState.String
                        : LexicalState.Character;
                    escaped = false;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static void AppendMaskedCharacter(
            StringBuilder builder,
            char character)
        {
            builder.Append(character == '\r' || character == '\n'
                ? character
                : ' ');
        }

        private static bool TryApplyConditionalDirective(
            string line,
            List<ConditionalScope> scopes,
            HashSet<string> defines)
        {
            if (TryGetDirectiveExpression(line, "#define", out string symbol))
            {
                if (scopes.All(scope => scope.IsActive))
                {
                    defines.Add(symbol.Trim());
                }

                return true;
            }

            if (TryGetDirectiveExpression(line, "#undef", out symbol))
            {
                if (scopes.All(scope => scope.IsActive))
                {
                    defines.Remove(symbol.Trim());
                }

                return true;
            }

            if (TryGetDirectiveExpression(line, "#if", out string expression))
            {
                bool parentIsActive = scopes.All(scope => scope.IsActive);
                bool condition = EvaluateConditionalExpression(
                    expression,
                    defines
                );
                scopes.Add(new ConditionalScope(
                    parentIsActive,
                    condition,
                    parentIsActive && condition
                ));
                return true;
            }

            if (TryGetDirectiveExpression(line, "#elif", out expression))
            {
                if (scopes.Count > 0)
                {
                    int lastIndex = scopes.Count - 1;
                    ConditionalScope scope = scopes[lastIndex];
                    bool condition = EvaluateConditionalExpression(
                        expression,
                        defines
                    );
                    bool branchIsActive = scope.ParentIsActive
                        && !scope.AnyBranchMatched
                        && condition;
                    scopes[lastIndex] = new ConditionalScope(
                        scope.ParentIsActive,
                        scope.AnyBranchMatched || condition,
                        branchIsActive
                    );
                }

                return true;
            }

            if (line.StartsWith("#else", StringComparison.Ordinal))
            {
                if (scopes.Count > 0)
                {
                    int lastIndex = scopes.Count - 1;
                    ConditionalScope scope = scopes[lastIndex];
                    scopes[lastIndex] = new ConditionalScope(
                        scope.ParentIsActive,
                        true,
                        scope.ParentIsActive && !scope.AnyBranchMatched
                    );
                }

                return true;
            }

            if (!line.StartsWith("#endif", StringComparison.Ordinal))
            {
                return false;
            }

            if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
            return true;
        }

        private static bool TryGetDirectiveExpression(
            string line,
            string directive,
            out string expression)
        {
            expression = null;
            if (!line.StartsWith(directive, StringComparison.Ordinal))
            {
                return false;
            }

            if (line.Length == directive.Length)
            {
                expression = string.Empty;
                return true;
            }

            char separator = line[directive.Length];
            if (!char.IsWhiteSpace(separator) && separator != '(')
            {
                return false;
            }

            expression = line.Substring(directive.Length).Trim();
            return true;
        }

        private static bool EvaluateConditionalExpression(
            string expression,
            HashSet<string> defines)
        {
            if (string.IsNullOrWhiteSpace(expression)) return false;

            int commentIndex = expression.IndexOf(
                "//",
                StringComparison.Ordinal
            );
            if (commentIndex >= 0)
            {
                expression = expression.Substring(0, commentIndex);
            }

            try
            {
                var parser = new ConditionalExpressionParser(
                    expression,
                    defines
                );
                return parser.Evaluate();
            }
            catch
            {
                return false;
            }
        }

        private static string DisplayId(ItemDefinition definition)
        {
            return string.IsNullOrWhiteSpace(definition?.ItemId)
                ? definition?.name ?? "missing"
                : definition.ItemId;
        }

        private readonly struct ConditionalScope
        {
            internal bool ParentIsActive { get; }

            internal bool AnyBranchMatched { get; }

            internal bool IsActive { get; }

            internal ConditionalScope(
                bool parentIsActive,
                bool anyBranchMatched,
                bool isActive)
            {
                ParentIsActive = parentIsActive;
                AnyBranchMatched = anyBranchMatched;
                IsActive = isActive;
            }
        }

        private readonly struct SourceRange
        {
            private readonly int _start;
            private readonly int _end;

            internal SourceRange(int start, int end)
            {
                _start = start;
                _end = end;
            }

            internal bool Contains(int position)
            {
                return position >= _start && position < _end;
            }

            internal int Length => _end - _start;
        }

        private readonly struct NamespaceSourceRange
        {
            internal string Name { get; }

            internal SourceRange Range { get; }

            internal NamespaceSourceRange(string name, SourceRange range)
            {
                Name = name;
                Range = range;
            }
        }

        private enum LexicalState
        {
            Code,
            LineComment,
            BlockComment,
            String,
            VerbatimString,
            Character
        }

        private sealed class ConditionalExpressionParser
        {
            private readonly string _expression;
            private readonly HashSet<string> _defines;
            private int _index;

            internal ConditionalExpressionParser(
                string expression,
                HashSet<string> defines)
            {
                _expression = expression ?? string.Empty;
                _defines = defines ?? new HashSet<string>(StringComparer.Ordinal);
            }

            internal bool Evaluate()
            {
                bool value = ParseOr();
                SkipWhitespace();
                if (_index != _expression.Length)
                {
                    throw new FormatException(
                        $"Unexpected preprocessor token at index {_index}."
                    );
                }

                return value;
            }

            private bool ParseOr()
            {
                bool value = ParseAnd();
                while (TryConsume("||"))
                {
                    bool right = ParseAnd();
                    value = value || right;
                }

                return value;
            }

            private bool ParseAnd()
            {
                bool value = ParseEquality();
                while (TryConsume("&&"))
                {
                    bool right = ParseEquality();
                    value = value && right;
                }

                return value;
            }

            private bool ParseEquality()
            {
                bool value = ParseUnary();
                while (true)
                {
                    if (TryConsume("=="))
                    {
                        value = value == ParseUnary();
                        continue;
                    }

                    if (TryConsume("!="))
                    {
                        value = value != ParseUnary();
                        continue;
                    }

                    return value;
                }
            }

            private bool ParseUnary()
            {
                if (TryConsume("!")) return !ParseUnary();
                return ParsePrimary();
            }

            private bool ParsePrimary()
            {
                if (TryConsume("("))
                {
                    bool value = ParseOr();
                    if (!TryConsume(")"))
                    {
                        throw new FormatException(
                            "Preprocessor expression has an unmatched parenthesis."
                        );
                    }

                    return value;
                }

                string identifier = ReadIdentifier();
                if (string.Equals(identifier, "true", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(identifier, "false", StringComparison.Ordinal))
                {
                    return false;
                }

                return _defines.Contains(identifier);
            }

            private string ReadIdentifier()
            {
                SkipWhitespace();
                int start = _index;
                if (_index >= _expression.Length
                    || (!char.IsLetter(_expression[_index])
                        && _expression[_index] != '_'))
                {
                    throw new FormatException(
                        $"Expected a preprocessor symbol at index {_index}."
                    );
                }

                _index++;
                while (_index < _expression.Length
                       && (char.IsLetterOrDigit(_expression[_index])
                           || _expression[_index] == '_'))
                {
                    _index++;
                }

                return _expression.Substring(start, _index - start);
            }

            private bool TryConsume(string token)
            {
                SkipWhitespace();
                if (_index + token.Length > _expression.Length)
                {
                    return false;
                }

                if (string.Compare(
                        _expression,
                        _index,
                        token,
                        0,
                        token.Length,
                        StringComparison.Ordinal) != 0)
                {
                    return false;
                }

                _index += token.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _expression.Length
                       && char.IsWhiteSpace(_expression[_index]))
                {
                    _index++;
                }
            }
        }

        private static ItemDatabaseDiagnostic Error(
            string code,
            string message,
            Object context = null)
        {
            return new ItemDatabaseDiagnostic(
                code,
                ItemDatabaseDiagnosticSeverity.Error,
                message,
                context
            );
        }

        private static ItemDatabaseDiagnostic Warning(
            string code,
            string message,
            Object context = null)
        {
            return new ItemDatabaseDiagnostic(
                code,
                ItemDatabaseDiagnosticSeverity.Warning,
                message,
                context
            );
        }

        private static ItemDatabaseDiagnostic Info(
            string code,
            string message,
            Object context = null)
        {
            return new ItemDatabaseDiagnostic(
                code,
                ItemDatabaseDiagnosticSeverity.Info,
                message,
                context
            );
        }

        [Serializable]
        private sealed class JsonProbe
        {
        }
    }
}
