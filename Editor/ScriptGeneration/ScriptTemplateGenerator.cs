using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration
{
    /// <summary>
    /// Generates C# script templates for manager, provider, consumer,
    /// and persister classes with abstract member stubs.
    /// </summary>
    public static class ScriptTemplateGenerator
    {
        #region Constants
        private const string DefaultNamespace = "Scripts";
        #endregion

        #region Nested Types
        public enum GenerationMode
        {
            /// <summary>Generate a class implementing the target interface.</summary>
            InterfaceImplementation,
            /// <summary>Generate a class inheriting from the target abstract class.</summary>
            AbstractClassInheritance
        }
        #endregion

        #region Public Methods — Manager Scripts
        /// <summary>
        /// Creates a new C# script for a concrete manager class inheriting
        /// from the abstract manager base class that implements the given
        /// manager interface.
        /// </summary>
        public static void CreateManagerScript(
            string defaultClassName,
            string defaultFolder,
            Type managerInterfaceType)
        {
            if (managerInterfaceType == null)
            {
                Debug.LogError("[ScriptTemplateGenerator] managerInterfaceType is null.");
                return;
            }

            Type baseType = FindManagerBaseType(managerInterfaceType);
            if (baseType == null)
            {
                Debug.LogError(
                    $"[ScriptTemplateGenerator] Could not find abstract manager base "
                    + $"class implementing {managerInterfaceType.Name}.");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Create Manager Script",
                defaultFolder,
                defaultClassName,
                "cs");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string className = Path.GetFileNameWithoutExtension(path);
            string namespaceName = GetNamespaceFromFilePath(path);
            string content = BuildManagerContent(
                className,
                namespaceName,
                managerInterfaceType,
                baseType);

            WriteScriptFile(path, content);
        }
        #endregion

        #region Public Methods — Provider / Consumer / Persister Scripts
        /// <summary>
        /// Creates a new C# script for a custom provider, consumer, or
        /// persister class based on the target type (interface or abstract class).
        /// </summary>
        public static void CreatePluginScript(
            string defaultClassName,
            string defaultFolder,
            Type targetType,
            GenerationMode mode)
        {
            if (targetType == null)
            {
                Debug.LogError("[ScriptTemplateGenerator] targetType is null.");
                return;
            }

            string extension = targetType.IsInterface ? "Provider" : "Plugin";
            string safeDefaultName = string.IsNullOrEmpty(defaultClassName)
                ? $"Custom{targetType.Name}"
                : defaultClassName;

            string path = EditorUtility.SaveFilePanel(
                $"Create {targetType.Name} Script",
                defaultFolder,
                safeDefaultName,
                "cs");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string className = Path.GetFileNameWithoutExtension(path);
            string namespaceName = GetNamespaceFromFilePath(path);

            string content = mode == GenerationMode.AbstractClassInheritance
                ? BuildDerivedClassContent(className, namespaceName, targetType)
                : BuildInterfaceImplementationContent(className, namespaceName, targetType);

            WriteScriptFile(path, content);
        }
        #endregion

        #region Public Methods — Utility
        /// <summary>
        /// Finds the abstract ScriptableObject base class that implements the
        /// given manager interface (e.g. IAdsManager → AdsManagerBase&lt;T&gt;).
        /// </summary>
        public static Type FindManagerBaseType(Type managerInterfaceType)
        {
            return GetAllTypes()
                .FirstOrDefault(t =>
                    t.IsAbstract
                    && !t.IsInterface
                    && typeof(ScriptableObject).IsAssignableFrom(t)
                    && managerInterfaceType.IsAssignableFrom(t));
        }

        /// <summary>
        /// Resolves a type by full name. Returns null if not found.
        /// </summary>
        public static Type ResolveType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullTypeName);
                if (type != null)
                {
                    return type;
                }
            }

            return GetAllTypes()
                .FirstOrDefault(t =>
                    string.Equals(t.FullName, fullTypeName, StringComparison.Ordinal));
        }
        #endregion

        #region Private Methods — Content Building
        private static string BuildManagerContent(
            string className,
            string namespaceName,
            Type interfaceType,
            Type baseType)
        {
            HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);
            string baseClassDeclaration = BuildBaseClassDeclaration(
                className, baseType, namespaces);

            List<string> members = GetAbstractMembers(baseType, namespaces);

            return AssembleScript(
                className, namespaceName, baseClassDeclaration, members,
                GetCollectedUsings(namespaces));
        }

        private static string BuildInterfaceImplementationContent(
            string className,
            string namespaceName,
            Type interfaceType)
        {
            HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);

            // ScriptableObject + interface
            string scriptableObjName = GetCSharpTypeName(typeof(ScriptableObject), namespaces);
            string interfaceName = GetCSharpTypeName(interfaceType, namespaces);

            string baseClassDeclaration = $"{scriptableObjName}, {interfaceName}";

            List<string> members = GetAllInterfaceMembers(interfaceType, namespaces);

            return AssembleScript(
                className, namespaceName, baseClassDeclaration, members,
                GetCollectedUsings(namespaces));
        }

        private static string BuildDerivedClassContent(
            string className,
            string namespaceName,
            Type baseType)
        {
            HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);
            string baseClassDeclaration = GetCSharpTypeName(baseType, namespaces);

            List<string> members = GetAbstractMembers(baseType, namespaces);

            return AssembleScript(
                className, namespaceName, baseClassDeclaration, members,
                GetCollectedUsings(namespaces));
        }

        private static string AssembleScript(
            string className,
            string namespaceName,
            string baseClassDeclaration,
            List<string> members,
            IEnumerable<string> usingStatements)
        {
            StringBuilder sb = new StringBuilder();

            foreach (string ns in usingStatements)
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");

            // Class declaration
            if (members.Count > 0)
            {
                sb.AppendLine(
                    $"    public sealed class {className} : {baseClassDeclaration}");
                sb.AppendLine("    {");

                foreach (string member in members)
                {
                    sb.AppendLine($"        {member}");
                }

                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine(
                    $"    public sealed class {className} : {baseClassDeclaration} {{ }}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Builds the base class declaration for a manager class, handling the
        /// self-referencing generic pattern (e.g. AdsManagerBase&lt;MyAdsManager&gt;).
        /// </summary>
        private static string BuildBaseClassDeclaration(
            string className,
            Type baseType,
            HashSet<string> namespaces)
        {
            string baseTypeName = GetCSharpTypeName(baseType, namespaces);

            if (!baseType.IsGenericTypeDefinition)
            {
                return baseTypeName;
            }

            Type[] genericArgs = baseType.GetGenericArguments();
            List<string> filledArgs = new List<string>();

            foreach (Type arg in genericArgs)
            {
                // Check constraint: if the arg is constrained to be the class itself
                // (e.g. where T : AdsManagerBase<T>), use className
                Type[] constraints = arg.GetGenericParameterConstraints();
                bool isSelfRef = constraints.Any(c =>
                    c.IsGenericType
                    && c.GetGenericTypeDefinition() == baseType);

                if (isSelfRef)
                {
                    filledArgs.Add(className);
                }
                else
                {
                    // For non-self-referencing args (like RemoteConfigManagerBase's TData),
                    // use the constraint's simplest type or generate a stub
                    Type bestConstraint = FindBestConstraint(constraints);
                    if (bestConstraint != null)
                    {
                        string constraintName = GetCSharpTypeName(bestConstraint, namespaces);
                        filledArgs.Add(constraintName);

                        // If the constraint is IRemoteConfigData, generate a config stub
                        // We'll add a comment about this
                    }
                    else
                    {
                        filledArgs.Add(className + "Data");
                    }
                }
            }

            return $"{baseTypeName}<{string.Join(", ", filledArgs)}>";
        }

        private static Type FindBestConstraint(Type[] constraints)
        {
            if (constraints == null || constraints.Length == 0)
            {
                return null;
            }

            // Return the first non-class constraint (interface) if possible
            Type firstInterface = constraints.FirstOrDefault(c => c.IsInterface);
            return firstInterface ?? constraints[0];
        }
        #endregion

        #region Private Methods — Member Detection
        /// <summary>
        /// Returns all members (methods and properties) from an interface and
        /// its parent interfaces that need implementation.
        /// </summary>
        private static List<string> GetAllInterfaceMembers(
            Type interfaceType,
            HashSet<string> namespaces)
        {
            List<string> members = new List<string>();

            // Methods
            foreach (MethodInfo method in interfaceType.GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                if (!method.IsAbstract)
                {
                    continue;
                }

                members.Add(GenerateMethodSignature(method, isOverride: false, namespaces));
            }

            // Properties
            foreach (PropertyInfo prop in interfaceType.GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            {
                members.Add(GeneratePropertySignature(
                    prop, isOverride: false, namespaces));
            }

            // Events
            foreach (EventInfo evt in interfaceType.GetEvents(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            {
                members.Add(GenerateEventSignature(evt, isOverride: false, namespaces));
            }

            // Recurse into parent interfaces
            foreach (Type parent in interfaceType.GetInterfaces())
            {
                members.AddRange(GetAllInterfaceMembers(parent, namespaces));
            }

            return members;
        }

        /// <summary>
        /// Returns all abstract members from a base class that must be overridden.
        /// </summary>
        private static List<string> GetAbstractMembers(
            Type baseType,
            HashSet<string> namespaces)
        {
            List<string> members = new List<string>();

            // Abstract methods
            foreach (MethodInfo method in baseType.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance))
            {
                if (!method.IsAbstract || method.IsSpecialName)
                {
                    continue;
                }

                // Only include protected/public (not private abstract, though rare)
                if (method.IsPrivate)
                {
                    continue;
                }

                members.Add(GenerateMethodSignature(
                    method, isOverride: true, namespaces));
            }

            // Abstract properties
            foreach (PropertyInfo prop in baseType.GetProperties(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance))
            {
                MethodInfo getMethod = prop.GetGetMethod(nonPublic: true);
                MethodInfo setMethod = prop.GetSetMethod(nonPublic: true);

                bool isAbstract = (getMethod != null && getMethod.IsAbstract)
                    || (setMethod != null && setMethod.IsAbstract);

                if (!isAbstract)
                {
                    continue;
                }

                bool hasGetter = getMethod != null && getMethod.IsAbstract;
                bool hasSetter = setMethod != null && setMethod.IsAbstract;

                members.Add(GeneratePropertySignatureForAbstract(
                    prop, hasGetter, hasSetter, namespaces));
            }

            // Also check base classes recursively
            Type parent = baseType.BaseType;
            if (parent != null
                && parent != typeof(ScriptableObject)
                && parent != typeof(object))
            {
                members.InsertRange(
                    0, GetAbstractMembers(parent, namespaces));
            }

            return members;
        }
        #endregion

        #region Private Methods — C# Code Generation
        private static string GenerateMethodSignature(
            MethodInfo method,
            bool isOverride,
            HashSet<string> namespaces)
        {
            string returnType = GetCSharpTypeName(method.ReturnType, namespaces);
            string modifier = isOverride ? "override " : "";
            string accessibility = method.IsFamily
                ? "protected "
                : "public ";

            ParameterInfo[] parameters = method.GetParameters();
            string paramStr = string.Join(
                ", ",
                parameters.Select(p => GetParameterDeclaration(p, namespaces)));

            StringBuilder sb = new StringBuilder();
            sb.Append($"{accessibility}{modifier}{returnType} {method.Name}({paramStr})");

            bool hasOutParams = parameters.Any(p => p.IsOut);
            if (hasOutParams || returnType != "void")
            {
                sb.AppendLine();
                sb.Append("        {");

                // Assign out params before throw
                foreach (ParameterInfo p in parameters)
                {
                    if (p.IsOut)
                    {
                        sb.AppendLine();
                        sb.Append($"            {p.Name} = default;");
                    }
                }

                sb.AppendLine();
                sb.Append("            throw new NotImplementedException();");
                sb.AppendLine();
                sb.Append("        }");
            }
            else
            {
                sb.Append(" { }");
            }

            return sb.ToString();
        }

        private static string GeneratePropertySignature(
            PropertyInfo prop,
            bool isOverride,
            HashSet<string> namespaces)
        {
            string typeName = GetCSharpTypeName(prop.PropertyType, namespaces);
            string modifier = isOverride ? "override " : "";

            MethodInfo getMethod = prop.GetGetMethod();
            MethodInfo setMethod = prop.GetSetMethod();

            if (setMethod != null && getMethod != null)
            {
                return $"public {modifier}{typeName} {prop.Name} {{ get; set; }}";
            }

            if (getMethod != null)
            {
                // For abstract getter-only properties from a base class
                if (isOverride)
                {
                    return $"public {modifier}{typeName} {prop.Name} => " +
                        "throw new NotImplementedException();";
                }

                // For interface properties
                return $"public {typeName} {prop.Name} => " +
                    "throw new NotImplementedException();";
            }

            return $"public {modifier}{typeName} {prop.Name} {{ set; }}";
        }

        private static string GeneratePropertySignatureForAbstract(
            PropertyInfo prop,
            bool hasGetter,
            bool hasSetter,
            HashSet<string> namespaces)
        {
            string typeName = GetCSharpTypeName(prop.PropertyType, namespaces);

            if (hasGetter && hasSetter)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"public override {typeName} {prop.Name}");
                sb.Append("        {");
                sb.AppendLine();
                sb.AppendLine("            get => throw new NotImplementedException();");
                sb.Append("            set => throw new NotImplementedException();");
                sb.AppendLine();
                sb.Append("        }");
                return sb.ToString();
            }

            if (hasGetter)
            {
                return $"public override {typeName} {prop.Name} => " +
                    "throw new NotImplementedException();";
            }

            return $"public override {typeName} {prop.Name} {{ " +
                "set => throw new NotImplementedException(); }";
        }

        private static string GenerateEventSignature(
            EventInfo evt,
            bool isOverride,
            HashSet<string> namespaces)
        {
            string typeName = GetCSharpTypeName(evt.EventHandlerType, namespaces);
            return $"public event {typeName} {evt.Name};";
        }

        private static string GetParameterDeclaration(
            ParameterInfo param,
            HashSet<string> namespaces)
        {
            Type paramType = param.ParameterType;

            if (param.IsOut)
            {
                string typeName = GetCSharpTypeName(
                    paramType.GetElementType(), namespaces);
                return $"out {typeName} {param.Name}";
            }

            if (paramType.IsByRef)
            {
                string typeName = GetCSharpTypeName(
                    paramType.GetElementType(), namespaces);
                return $"ref {typeName} {param.Name}";
            }

            return $"{GetCSharpTypeName(paramType, namespaces)} {param.Name}";
        }
        #endregion

        #region Private Methods — C# Type Name Resolution
        private static readonly Dictionary<Type, string> BuiltInTypeNames =
            new Dictionary<Type, string>
            {
                { typeof(void), "void" },
                { typeof(bool), "bool" },
                { typeof(byte), "byte" },
                { typeof(sbyte), "sbyte" },
                { typeof(char), "char" },
                { typeof(decimal), "decimal" },
                { typeof(double), "double" },
                { typeof(float), "float" },
                { typeof(int), "int" },
                { typeof(uint), "uint" },
                { typeof(long), "long" },
                { typeof(ulong), "ulong" },
                { typeof(short), "short" },
                { typeof(ushort), "ushort" },
                { typeof(object), "object" },
                { typeof(string), "string" },
            };

        /// <summary>
        /// Returns the C# keyword or type name for a given System.Type,
        /// collecting required namespaces for the generated file.
        /// </summary>
        private static string GetCSharpTypeName(
            Type type,
            HashSet<string> namespaces)
        {
            if (type == null)
            {
                return "void";
            }

            // Built-in C# keywords
            if (BuiltInTypeNames.TryGetValue(type, out string keyword))
            {
                return keyword;
            }

            // Nullable<T>
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return GetCSharpTypeName(
                    type.GenericTypeArguments[0], namespaces) + "?";
            }

            // Generic types
            if (type.IsGenericType)
            {
                string genericName = type.Name;
                int backtickIndex = genericName.IndexOf('`');
                if (backtickIndex >= 0)
                {
                    genericName = genericName.Substring(0, backtickIndex);
                }

                string typeArgs = string.Join(
                    ", ",
                    type.GenericTypeArguments.Select(
                        t => GetCSharpTypeName(t, namespaces)));

                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    namespaces.Add(type.Namespace);
                }

                return $"{genericName}<{typeArgs}>";
            }

            // Array types
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                return GetCSharpTypeName(elementType, namespaces) + "[]";
            }

            // Regular type
            if (!string.IsNullOrEmpty(type.Namespace)
                && type.Namespace != "UnityEngine"
                && type.Namespace != "UnityEditor"
                && type.Namespace != "System"
                && type.Namespace != "System.Collections"
                && type.Namespace != "System.Collections.Generic")
            {
                namespaces.Add(type.Namespace);
            }

            return type.Name;
        }
        #endregion

        #region Private Methods — File Operations
        private static void WriteScriptFile(string absolutePath, string content)
        {
            try
            {
                string directory = Path.GetDirectoryName(absolutePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(absolutePath, content, Encoding.UTF8);
                AssetDatabase.Refresh();

                string relativePath = GetProjectRelativePath(absolutePath);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    AssetDatabase.ImportAsset(relativePath);
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                        relativePath);
                    if (script != null)
                    {
                        EditorGUIUtility.PingObject(script);
                    }
                }

                Debug.Log(
                    $"[ScriptTemplateGenerator] Created script at '{absolutePath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[ScriptTemplateGenerator] Failed to write script: {ex.Message}");
            }
        }

        private static string GetProjectRelativePath(string absolutePath)
        {
            string dataPath = Application.dataPath;
            if (absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + absolutePath.Substring(dataPath.Length);
            }

            return null;
        }

        private static string GetNamespaceFromFilePath(string absolutePath)
        {
            string relativePath = GetProjectRelativePath(absolutePath);
            if (string.IsNullOrEmpty(relativePath))
            {
                return GetRootNamespace();
            }

            // Remove "Assets/" and the file name
            string dirPath = Path.GetDirectoryName(relativePath);
            if (string.IsNullOrEmpty(dirPath) || dirPath == "Assets")
            {
                return GetRootNamespace();
            }

            string subPath = dirPath.Substring("Assets/".Length);
            string[] parts = subPath.Split(
                new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            string rootNs = GetRootNamespace();
            if (string.IsNullOrEmpty(rootNs))
            {
                return string.Join(".", parts);
            }

            return rootNs + "." + string.Join(".", parts);
        }

        private static string GetRootNamespace()
        {
            string rootNs = EditorSettings.projectGenerationRootNamespace;
            return string.IsNullOrEmpty(rootNs) ? DefaultNamespace : rootNs;
        }

        private static IEnumerable<string> GetCollectedUsings(HashSet<string> namespaces)
        {
            // Always include System for NotImplementedException, Action, etc.
            List<string> result = new List<string> { "System", "UnityEngine" };

            foreach (string ns in namespaces
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n))
            {
                if (!result.Contains(ns))
                {
                    result.Add(ns);
                }
            }

            result.Sort((a, b) =>
            {
                // System namespaces first, then alphabetically
                bool aSys = a == "System" || a.StartsWith("System.");
                bool bSys = b == "System" || b.StartsWith("System.");
                if (aSys && !bSys)
                {
                    return -1;
                }

                if (!aSys && bSys)
                {
                    return 1;
                }

                return string.CompareOrdinal(a, b);
            });

            return result;
        }
        #endregion

        #region Private Methods — Reflection Helpers
        private static Type[] _allTypesCache;

        private static Type[] GetAllTypes()
        {
            if (_allTypesCache != null)
            {
                return _allTypesCache;
            }

            List<Type> allTypes = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    allTypes.AddRange(assembly.GetTypes());
                }
                catch (ReflectionTypeLoadException ex)
                {
                    allTypes.AddRange(
                        ex.Types.Where(t => t != null));
                }
            }

            _allTypesCache = allTypes.ToArray();
            return _allTypesCache;
        }
        #endregion
    }
}
