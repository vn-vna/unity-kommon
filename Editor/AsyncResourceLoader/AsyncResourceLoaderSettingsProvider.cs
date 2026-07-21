using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    public class AsyncResourceLoaderSettingsProvider : SettingsProvider
    {
        private const string ConfigAssetPath =
            "Assets/Resources/AsyncResourceLoaderConfiguration.asset";

        private const string ResourcesFolder = "Assets/Resources";
        private const string ProviderAssetFolder = "Assets/Resources/AsyncResourceLoader";

        private static readonly string[] TabNames = { "Manager Config", "Providers" };

        private Vector2 _scrollPosition;
        private readonly Dictionary<string, int> _tabIndices = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _expandedTypes = new Dictionary<string, bool>();
        private readonly Dictionary<string, ProviderCreationWizard> _providerWizards
            = new Dictionary<string, ProviderCreationWizard>();

        private AsyncResourceLoaderSettingsProvider(
            string path, SettingsScope scopes,
            IEnumerable<string> keywords = null
        ) : base(path, scopes, keywords) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new AsyncResourceLoaderSettingsProvider(
                "Project/Tools/Async Resource Loader",
                SettingsScope.Project,
                new[] { "resource", "async", "provider", "loader", "addressable", "downloadable" }
            );
        }

        public override void OnGUI(string searchContext)
        {
            try
            {
                base.OnGUI(searchContext);

                AsyncResourceLoadingConfiguration config = GetOrCreateConfiguration();
                if (config == null) return;

                var managerTypes = FindAllResourceManagerTypes(config);

                if (managerTypes.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No concrete AsyncResourceManagerBase classes found in the project.\n\n"
                        + "Create a class that inherits from AsyncResourceManagerBase<SelfType, ResourceType>.",
                        MessageType.Info
                    );

                    GUILayout.Space(8);

                    if (GUILayout.Button("Create New Resource Manager", GUILayout.Height(30)))
                    {
                        CreateResourceManagerWindow.Open();
                    }

                    return;
                }

                GUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Async Resource Loader Configuration", EditorStyles.boldLabel);
                GUILayout.Space(4);

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                foreach (var info in managerTypes)
                {
                    DrawManagerCard(info, config);
                    GUILayout.Space(6);
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(8);

                if (GUILayout.Button("Create New Resource Manager", GUILayout.Height(28)))
                {
                    CreateResourceManagerWindow.Open();
                }
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AsyncResourceLoader] {ex}");
            }
        }

        #region Configuration Asset

        private static AsyncResourceLoadingConfiguration GetOrCreateConfiguration()
        {
            var config = AsyncResourceLoadingConfiguration.Instance;
            if (config != null) return config;

            config = AssetDatabase.LoadAssetAtPath<AsyncResourceLoadingConfiguration>(
                ConfigAssetPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<AsyncResourceLoadingConfiguration>();
            if (!Directory.Exists(ResourcesFolder))
            {
                Directory.CreateDirectory(ResourcesFolder);
            }

            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        #endregion

        #region Manager Card

        private sealed class ConcreteManagerInfo
        {
            public Type ConcreteType;
            public Type ResourceType;
            public string DisplayName;
            public List<ScriptableObject> ExistingAssets;
            public ScriptableObject AttachedAsset;
            public bool HasAsset => ExistingAssets != null && ExistingAssets.Count > 0;
            public bool IsAttached => AttachedAsset != null;
        }

        private void DrawManagerCard(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawCardHeader(info, config);

            if (info.IsAttached && IsExpanded(info))
            {
                GUILayout.Space(2);

                int tabIndex = GetTabIndex(info);
                tabIndex = GUILayout.Toolbar(tabIndex, TabNames);
                SetTabIndex(info, tabIndex);

                GUILayout.Space(4);

                Rect dividerRect = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(dividerRect,
                    new Color(0.5f, 0.5f, 0.5f, 0.3f));
                GUILayout.Space(4);

                switch (tabIndex)
                {
                    case 0:
                        DrawManagerConfig(info.AttachedAsset, info.ConcreteType,
                            info.ResourceType);
                        break;
                    case 1:
                        DrawProviders(info, config);
                        break;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCardHeader(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStateBadge(info);
                EditorGUILayout.LabelField(info.DisplayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DrawCardActions(info, config);
            }
        }

        private static void DrawStateBadge(ConcreteManagerInfo info)
        {
            string badgeText;
            Color badgeColor;

            if (info.IsAttached)
            {
                badgeText = " ACTIVE ";
                badgeColor = new Color(0.2f, 0.7f, 0.2f);
            }
            else if (info.HasAsset)
            {
                badgeText = " NOT ATTACHED ";
                badgeColor = new Color(0.8f, 0.7f, 0.2f);
            }
            else
            {
                badgeText = " NO ASSET ";
                badgeColor = Color.gray;
            }

            GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                normal = { textColor = Color.white },
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = 90
            };

            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = badgeColor;
            GUILayout.Label(badgeText, badgeStyle, GUILayout.Height(20));
            GUI.backgroundColor = prevColor;
        }

        private void DrawCardActions(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            if (info.IsAttached)
            {
                if (DrawSmallButton("Configure",
                        $"Show/hide configuration for {info.DisplayName}"))
                {
                    ToggleExpanded(info);
                }

                if (DrawSmallButton("Disable",
                        $"Remove {info.DisplayName} from configuration"))
                {
                    SetManagerInConfig(config, info.AttachedAsset, null);
                }

                if (DrawSmallButton("Delete",
                        $"Delete the {info.ConcreteType.Name} asset from disk"))
                {
                    DeleteManagerAsset(info, config);
                }
            }
            else if (info.HasAsset)
            {
                ScriptableObject firstAsset = info.ExistingAssets[0];

                if (DrawSmallButton("Enable",
                        $"Add {firstAsset.name} to configuration"))
                {
                    SetManagerInConfig(config, null, firstAsset);
                    info.AttachedAsset = firstAsset;
                }

                if (DrawSmallButton("Delete",
                        $"Delete the {info.ConcreteType.Name} asset from disk"))
                {
                    DeleteManagerAsset(info, config);
                }
            }
            else
            {
                if (DrawSmallButton("Create Asset",
                        $"Create a new {info.ConcreteType.Name} ScriptableObject asset"))
                {
                    CreateManagerAsset(info, config);
                }
            }
        }

        #endregion

        #region Manager Config Tab

        private void DrawManagerConfig(
            ScriptableObject manager, Type concreteType, Type resourceType)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Resource Type", resourceType.Name);
                EditorGUILayout.LabelField("Manager Type", concreteType.FullName);

                bool isInitialized = GetManagerStatus(manager);
                GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = isInitialized ? Color.green : Color.gray }
                };
                EditorGUILayout.LabelField(
                    "Status",
                    isInitialized ? "Initialized" : "Not Initialized",
                    statusStyle
                );
            }

            GUILayout.Space(6);
            DrawInlineInspector(
                manager,
                $"{PascalCaseToSpaced(manager.name).ToUpperInvariant()} CONFIGURATION"
            );
        }

        #endregion

        #region Providers Tab

        private void DrawProviders(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            ScriptableObject manager = info.AttachedAsset;
            if (manager == null) return;

            Type resourceType = info.ResourceType;

            var templates = FindProviderTemplates(manager, resourceType);
            if (templates.Count > 0)
            {
                EditorGUILayout.LabelField("Built-in Templates",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                foreach (var tmpl in templates)
                {
                    DrawTemplateProviderCard(tmpl, info, resourceType);
                    GUILayout.Space(4);
                }

                GUILayout.Space(4);
            }

            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp != null)
            {
                EditorGUILayout.LabelField("Enabled Providers",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                for (int i = 0; i < providersProp.arraySize; i++)
                {
                    var element = providersProp.GetArrayElementAtIndex(i);
                    ScriptableObject provider = element.objectReferenceValue
                        as ScriptableObject;
                    if (provider == null) continue;

                    DrawEnabledProviderCard(
                        provider, i, providersProp, serializedManager, manager);
                    GUILayout.Space(4);
                }
            }

            var unattached = FindUnattachedProviders(info, resourceType);
            if (unattached.Count > 0)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("Detached Providers",
                    EditorStyles.miniBoldLabel);
                GUILayout.Space(2);

                foreach (var provInfo in unattached)
                {
                    DrawDetachedProviderCard(provInfo, info, resourceType);
                    GUILayout.Space(4);
                }
            }

            GUILayout.Space(8);
            Rect div = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(4);

            if (GUILayout.Button("+ New Custom Provider", GUILayout.Height(24)))
            {
                ScriptTemplateGenerator.CreatePluginScript(
                    null,
                    "Assets/",
                    typeof(IAsyncResourceProvider<>).MakeGenericType(resourceType),
                    ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
            }
        }

        #endregion

        #region Template Provider Card

        private sealed class ProviderTemplateEntry
        {
            public ResourceProviderAttribute Attribute;
            public Type OpenGenericType;
            public Type ConcreteType;
            public ScriptableObject ExistingAsset;
            public bool IsInArray;
            public int ArrayIndex;
        }

        private void DrawTemplateProviderCard(ProviderTemplateEntry tmpl,
            ConcreteManagerInfo info, Type resourceType)
        {
            string key = info.ConcreteType.FullName + "::"
                + tmpl.OpenGenericType.FullName;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        tmpl.Attribute.DisplayName, EditorStyles.boldLabel);

                    if (tmpl.Attribute.RequiredDefines != null)
                    {
                        string[] missing = GetMissingDefines(
                            tmpl.Attribute.RequiredDefines);

                        if (missing.Length > 0)
                        {
                            GUIStyle defStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal =
                                {
                                    textColor = new Color(0.9f, 0.6f, 0.1f)
                                }
                            };
                            EditorGUILayout.LabelField(
                                $"Requires: {string.Join(", ", missing)}",
                                defStyle);

                            if (DrawSmallButton("Enable",
                                    "Enable required scripting defines"))
                            {
                                EnsureScriptingDefines(missing);
                            }

                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.LabelField(
                                tmpl.Attribute.Description,
                                EditorStyles.wordWrappedMiniLabel);
                            return;
                        }
                    }

                    GUILayout.FlexibleSpace();
                    DrawTemplateCardActions(tmpl, info, resourceType, key);
                }

                EditorGUILayout.LabelField(tmpl.Attribute.Description,
                    EditorStyles.wordWrappedMiniLabel);

                if (_providerWizards.TryGetValue(key, out var wizard)
                    && wizard.Active)
                {
                    GUILayout.Space(6);
                    DrawProviderCreationWizard(wizard, key, tmpl,
                        info, resourceType);
                }
            }
        }

        private void DrawTemplateCardActions(ProviderTemplateEntry tmpl,
            ConcreteManagerInfo info, Type resourceType, string key)
        {
            bool inWizard = _providerWizards.TryGetValue(key, out var wizard)
                && wizard.Active;

            if (tmpl.IsInArray)
            {
                if (!inWizard && DrawSmallButton("Disable",
                        $"Remove provider from the array"))
                {
                    RemoveProviderFromArray(info.AttachedAsset, tmpl.ArrayIndex);
                    tmpl.IsInArray = false;
                }

                if (!inWizard && DrawSmallButton("Delete",
                        $"Delete the asset from disk"))
                {
                    DeleteProviderAsset(tmpl.ExistingAsset);
                    tmpl.ExistingAsset = null;
                    tmpl.ConcreteType = null;
                    tmpl.IsInArray = false;
                    tmpl.ArrayIndex = -1;
                }
            }
            else if (tmpl.ConcreteType != null && tmpl.ExistingAsset != null)
            {
                if (DrawSmallButton("Enable",
                        $"Add to provider array"))
                {
                    AppendProviderToArray(info.AttachedAsset,
                        tmpl.ExistingAsset);
                    tmpl.IsInArray = true;
                }

                if (DrawSmallButton("Delete",
                        $"Delete the asset from disk"))
                {
                    DeleteProviderAsset(tmpl.ExistingAsset);
                    tmpl.ExistingAsset = null;
                    tmpl.ConcreteType = null;
                }
            }
            else
            {
                if (!inWizard && DrawSmallButton("Create",
                        $"Generate concrete subclass + ScriptableObject asset"))
                {
                    _providerWizards[key] = new ProviderCreationWizard
                    {
                        Active = true,
                        ClassName = $"{resourceType.Name}{tmpl.OpenGenericType.Name}",
                        Namespace = EditorSettings.projectGenerationRootNamespace
                            ?? "Scripts",
                        FolderPath = "Assets/"
                    };
                }
            }
        }

        #endregion

        #region Enabled Provider Card

        private void DrawEnabledProviderCard(ScriptableObject provider, int index,
            SerializedProperty providersProp, SerializedObject serializedManager,
            ScriptableObject manager)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(provider.name,
                        EditorStyles.boldLabel);

                    bool canMoveUp = index > 0;
                    bool canMoveDown = index < providersProp.arraySize - 1;

                    if (canMoveUp && DrawSmallButton("\u25B2", "Move up"))
                    {
                        providersProp.MoveArrayElement(index, index - 1);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        return;
                    }

                    if (canMoveDown && DrawSmallButton("\u25BC", "Move down"))
                    {
                        providersProp.MoveArrayElement(index, index + 1);
                        serializedManager.ApplyModifiedProperties();
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        return;
                    }

                    GUILayout.FlexibleSpace();

                    if (DrawSmallButton("Disable",
                            $"Remove {provider.name} from provider array"))
                    {
                        RemoveProviderFromArray(manager, index);
                        return;
                    }

                    if (DrawSmallButton("Delete",
                            $"Delete {provider.name} asset from disk"))
                    {
                        DeleteProviderAsset(provider);
                        return;
                    }
                }

                GUILayout.Space(2);
                DrawInlineInspector(
                    provider,
                    $"{PascalCaseToSpaced(provider.name).ToUpperInvariant()} PROVIDER"
                );
            }
        }

        #endregion

        #region Detached Provider Card

        private sealed class DetachedProviderInfo
        {
            public Type ConcreteType;
            public ScriptableObject Asset;
        }

        private void DrawDetachedProviderCard(DetachedProviderInfo provInfo,
            ConcreteManagerInfo info, Type resourceType)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(provInfo.Asset.name,
                        EditorStyles.boldLabel);

                    Color prevColor = GUI.color;
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    EditorGUILayout.LabelField(
                        $"({provInfo.ConcreteType.Name})",
                        EditorStyles.miniLabel);
                    GUI.color = prevColor;

                    GUILayout.FlexibleSpace();

                    if (DrawSmallButton("Enable",
                            $"Add {provInfo.Asset.name} to provider array"))
                    {
                        AppendProviderToArray(info.AttachedAsset, provInfo.Asset);
                    }

                    if (DrawSmallButton("Delete",
                            $"Delete {provInfo.Asset.name} from disk"))
                    {
                        DeleteProviderAsset(provInfo.Asset);
                    }
                }
            }
        }

        #endregion

        #region Provider Creation Wizard

        private sealed class ProviderCreationWizard
        {
            public bool Active;
            public string ClassName = "";
            public string Namespace = "";
            public string FolderPath = "Assets/";
        }

        private void DrawProviderCreationWizard(ProviderCreationWizard wizard,
            string key, ProviderTemplateEntry tmpl, ConcreteManagerInfo info,
            Type resourceType)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Create Provider Subclass",
                EditorStyles.miniBoldLabel);

            wizard.ClassName = EditorGUILayout.TextField("Class Name",
                wizard.ClassName);

            EditorGUI.BeginChangeCheck();
            wizard.Namespace = EditorGUILayout.TextField("Namespace",
                wizard.Namespace);
            if (EditorGUI.EndChangeCheck())
            {
                wizard.FolderPath = NamespaceToFolder(wizard.Namespace);
            }

            wizard.FolderPath = EditorGUILayout.TextField("Folder",
                wizard.FolderPath);

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    _providerWizards.Remove(key);
                }

                if (GUILayout.Button("Generate", GUILayout.Width(80)))
                {
                    GenerateProviderSubclass(wizard, tmpl, info, resourceType);
                    _providerWizards.Remove(key);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void GenerateProviderSubclass(ProviderCreationWizard wizard,
            ProviderTemplateEntry tmpl, ConcreteManagerInfo info,
            Type resourceType)
        {
            string className = wizard.ClassName;
            string ns = wizard.Namespace;
            string folder = wizard.FolderPath.TrimEnd('/');

            string openGenName = tmpl.OpenGenericType.FullName;
            int backtick = openGenName.IndexOf('`');
            string baseName = backtick > 0
                ? openGenName.Substring(0, backtick)
                : openGenName;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"using {tmpl.OpenGenericType.Namespace};");
            }

            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            string indent = string.IsNullOrEmpty(ns) ? "" : "    ";
            sb.AppendLine(
                $"{indent}public sealed class {className} : {baseName}<{resourceType.FullName}>");
            sb.AppendLine($"{indent}{{");

            Type closedGeneric = tmpl.OpenGenericType.MakeGenericType(resourceType);
            WriteAbstractMemberStubs(sb, closedGeneric, indent);

            sb.AppendLine($"{indent}}}");
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            string filePath = Path.Combine(folder, className + ".cs")
                .Replace('\\', '/');

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
            if (script != null)
            {
                EditorGUIUtility.PingObject(script);
            }
        }

        private static void WriteAbstractMemberStubs(StringBuilder sb,
            Type type, string indent)
        {
            if (!type.IsAbstract) return;

            string inner = indent + "    ";

            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.IsAbstract) continue;

                string ret = GetTypeName(method.ReturnType);
                var paramList = string.Join(", ",
                    method.GetParameters().Select(p =>
                        $"{GetTypeName(p.ParameterType)} {p.Name}"));
                string access = method.IsPublic ? "public" : "protected";

                sb.AppendLine();
                sb.AppendLine(
                    $"{inner}{access} override {ret} {method.Name}({paramList})");
                sb.AppendLine($"{inner}{{");
                sb.AppendLine(
                    $"{inner}    throw new System.NotImplementedException();");
                sb.AppendLine($"{inner}}}");
            }

            foreach (var prop in type.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                if (getter == null || !getter.IsAbstract) continue;

                string propType = GetTypeName(prop.PropertyType);
                string access = (getter.IsPublic
                                 || (setter != null && setter.IsPublic))
                    ? "public"
                    : "protected";

                sb.AppendLine();
                sb.AppendLine(
                    $"{inner}{access} override {propType} {prop.Name}");
                sb.AppendLine($"{inner}{{");

                if (getter.IsAbstract)
                {
                    sb.AppendLine(
                        $"{inner}    get => throw new System.NotImplementedException();");
                }

                if (setter != null && setter.IsAbstract)
                {
                    sb.AppendLine(
                        $"{inner}    set => throw new System.NotImplementedException();");
                }

                sb.AppendLine($"{inner}}}");
            }
        }

        private static string GetTypeName(Type type)
        {
            if (!type.IsGenericType) return type.FullName ?? type.Name;

            string name = type.Name;
            int backtick = name.IndexOf('`');
            name = backtick > 0 ? name.Substring(0, backtick) : name;
            string args = string.Join(", ",
                type.GenericTypeArguments.Select(GetTypeName));
            return $"{name}<{args}>";
        }

        #endregion

        #region Provider Array Manipulation

        private static void AppendProviderToArray(ScriptableObject manager,
            ScriptableObject provider)
        {
            if (manager == null || provider == null) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp == null) return;

            int newIdx = providersProp.arraySize++;
            providersProp.GetArrayElementAtIndex(newIdx).objectReferenceValue
                = provider;
            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
        }

        private static void RemoveProviderFromArray(ScriptableObject manager,
            int index)
        {
            if (manager == null || index < 0) return;

            SerializedObject serializedManager = new SerializedObject(manager);
            var providersProp = serializedManager.FindProperty("initialProviders");
            if (providersProp == null) return;
            if (index >= providersProp.arraySize) return;

            providersProp.DeleteArrayElementAtIndex(index);
            if (index < providersProp.arraySize
                && providersProp.GetArrayElementAtIndex(index)
                    .objectReferenceValue == null)
            {
                providersProp.DeleteArrayElementAtIndex(index);
            }

            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
        }

        private static void DeleteProviderAsset(ScriptableObject provider)
        {
            if (provider == null) return;

            string path = AssetDatabase.GetAssetPath(provider);
            if (string.IsNullOrEmpty(path)) return;

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Provider Asset",
                $"Permanently delete '{path}'?\n\nThis cannot be undone.",
                "Delete", "Cancel");
            if (!confirm) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region Manager Asset Actions

        private static void SetManagerInConfig(
            AsyncResourceLoadingConfiguration config,
            ScriptableObject oldManager,
            ScriptableObject newManager)
        {
            SerializedObject serializedConfig = new SerializedObject(config);
            var listProp = serializedConfig.FindProperty("managerAssets");

            if (listProp == null) return;

            int foundIndex = -1;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue
                    == oldManager)
                {
                    foundIndex = i;
                    break;
                }
            }

            if (newManager != null)
            {
                if (foundIndex >= 0)
                {
                    listProp.GetArrayElementAtIndex(foundIndex)
                        .objectReferenceValue = newManager;
                }
                else
                {
                    int newIndex = listProp.arraySize++;
                    listProp.GetArrayElementAtIndex(newIndex)
                        .objectReferenceValue = newManager;
                }
            }
            else if (foundIndex >= 0)
            {
                listProp.DeleteArrayElementAtIndex(foundIndex);
                if (foundIndex < listProp.arraySize
                    && listProp.GetArrayElementAtIndex(foundIndex)
                        .objectReferenceValue == null)
                {
                    listProp.DeleteArrayElementAtIndex(foundIndex);
                }
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private static void CreateManagerAsset(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                $"Create {info.ConcreteType.Name}",
                info.ConcreteType.Name,
                "asset",
                $"Create a new {info.ConcreteType.Name} asset"
            );

            if (string.IsNullOrEmpty(path)) return;

            ScriptableObject asset = ScriptableObject.CreateInstance(
                info.ConcreteType);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);

            SetManagerInConfig(config, null, asset);
            info.ExistingAssets.Add(asset);
            info.AttachedAsset = asset;
        }

        private static void DeleteManagerAsset(ConcreteManagerInfo info,
            AsyncResourceLoadingConfiguration config)
        {
            ScriptableObject assetToDelete = info.AttachedAsset;

            if (assetToDelete == null && info.ExistingAssets.Count > 0)
            {
                assetToDelete = info.ExistingAssets[0];
            }

            if (assetToDelete == null) return;

            string path = AssetDatabase.GetAssetPath(assetToDelete);

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Asset",
                $"Permanently delete '{path}'?\n\nThis cannot be undone.",
                "Delete", "Cancel"
            );

            if (!confirm) return;

            if (info.AttachedAsset == assetToDelete)
            {
                SetManagerInConfig(config, assetToDelete, null);
            }

            info.ExistingAssets.Remove(assetToDelete);
            info.AttachedAsset = null;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region Type Discovery (cached)

        private sealed class ManagerTypeEntry
        {
            public Type ConcreteType;
            public Type ResourceType;
        }

        private static List<ManagerTypeEntry> s_cachedManagerEntries;
        private static List<Type> s_cachedTemplateTypes;
        private static Dictionary<string, Type> s_cachedConcreteSubclass;
        private static Dictionary<string, List<Type>> s_cachedInterfaceTypes;

        [DidReloadScripts]
        private static void InvalidateCaches()
        {
            s_cachedManagerEntries = null;
            s_cachedTemplateTypes = null;
            s_cachedConcreteSubclass = null;
            s_cachedInterfaceTypes = null;
        }

        private static void EnsureCaches()
        {
            if (s_cachedManagerEntries != null) return;

            var managerEntries = new List<ManagerTypeEntry>();
            var templateTypes = new List<Type>();

            foreach (Type type in GetAllTypes())
            {
                var attr = type.GetCustomAttribute<ResourceProviderAttribute>();
                if (attr != null && type.IsGenericTypeDefinition
                    && type.Namespace != null
                    && type.Namespace.StartsWith(
                        "Com.Hapiga.Scheherazade.Common.AsyncResourceLoader"))
                {
                    templateTypes.Add(type);
                }
            }

            foreach (Type type in GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;

                Type resType = GetResourceManagerResourceType(type);
                if (resType != null
                    && typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    managerEntries.Add(new ManagerTypeEntry
                    {
                        ConcreteType = type, ResourceType = resType
                    });
                }
            }

            var subclassMap = new Dictionary<string, Type>();
            foreach (Type type in GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (type.IsGenericTypeDefinition) continue;
                if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                Type baseType = type.BaseType;
                while (baseType != null
                       && baseType != typeof(ScriptableObject))
                {
                    if (baseType.IsGenericType && baseType.IsGenericTypeDefinition)
                    {
                        Type[] genArgs = null;
                        Type check = type.BaseType;
                        while (check != null
                               && check != typeof(ScriptableObject)
                               && check != typeof(object))
                        {
                            if (check.IsGenericType
                                && !check.IsGenericTypeDefinition
                                && check.GetGenericTypeDefinition() == baseType)
                            {
                                genArgs = check.GetGenericArguments();
                                break;
                            }

                            check = check.BaseType;
                        }

                        if (genArgs != null && genArgs.Length > 0)
                        {
                            string key = baseType.FullName + "|"
                                + genArgs[0].FullName;
                            subclassMap[key] = type;
                        }
                    }

                    baseType = baseType.BaseType;
                }
            }

            s_cachedManagerEntries = managerEntries;
            s_cachedTemplateTypes = templateTypes;
            s_cachedConcreteSubclass = subclassMap;

            var interfaceMap = new Dictionary<string, List<Type>>();
            foreach (Type type in GetAllTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                foreach (Type iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    if (iface.GetGenericTypeDefinition()
                        != typeof(IAsyncResourceProvider<>)) continue;

                    string key = iface.FullName;
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!interfaceMap.TryGetValue(key, out var list))
                    {
                        list = new List<Type>();
                        interfaceMap[key] = list;
                    }

                    list.Add(type);
                }
            }

            s_cachedInterfaceTypes = interfaceMap;
        }

        private List<ConcreteManagerInfo> FindAllResourceManagerTypes(
            AsyncResourceLoadingConfiguration config)
        {
            EnsureCaches();

            var attachedByType = new Dictionary<Type, ScriptableObject>();
            SerializedObject serializedConfig = new SerializedObject(config);
            var listProp = serializedConfig.FindProperty("managerAssets");
            if (listProp != null)
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    ScriptableObject manager = listProp
                        .GetArrayElementAtIndex(i).objectReferenceValue
                        as ScriptableObject;
                    if (manager == null) continue;
                    Type moduleType = manager.GetType();
                    if (IsResourceManagerType(moduleType))
                    {
                        attachedByType[moduleType] = manager;
                    }
                }
            }

            var results = new List<ConcreteManagerInfo>();
            foreach (var entry in s_cachedManagerEntries)
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:" + entry.ConcreteType.Name);
                var existingAssets = new List<ScriptableObject>();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                        path, entry.ConcreteType) as ScriptableObject;
                    if (asset != null) existingAssets.Add(asset);
                }

                attachedByType.TryGetValue(entry.ConcreteType,
                    out ScriptableObject attachedAsset);

                if (attachedAsset != null
                    && !existingAssets.Contains(attachedAsset))
                {
                    existingAssets.Insert(0, attachedAsset);
                }

                results.Add(new ConcreteManagerInfo
                {
                    ConcreteType = entry.ConcreteType,
                    ResourceType = entry.ResourceType,
                    DisplayName =
                        $"{entry.ConcreteType.Name} ({entry.ResourceType.Name})",
                    ExistingAssets = existingAssets,
                    AttachedAsset = attachedAsset
                });
            }

            return results;
        }

        private static List<ProviderTemplateEntry> FindProviderTemplates(
            ScriptableObject manager, Type resourceType)
        {
            EnsureCaches();

            var assetToIndex = new Dictionary<ScriptableObject, int>();
            if (manager != null)
            {
                SerializedObject so = new SerializedObject(manager);
                var arrProp = so.FindProperty("initialProviders");
                if (arrProp != null)
                {
                    for (int i = 0; i < arrProp.arraySize; i++)
                    {
                        ScriptableObject p = arrProp
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                        if (p != null) assetToIndex[p] = i;
                    }
                }
            }

            var results = new List<ProviderTemplateEntry>();

            foreach (Type templateType in s_cachedTemplateTypes)
            {
                var attr = templateType
                    .GetCustomAttribute<ResourceProviderAttribute>();
                if (attr == null) continue;

                string key = templateType.FullName + "|"
                    + resourceType.FullName;
                s_cachedConcreteSubclass.TryGetValue(
                    key, out Type concreteType);

                ScriptableObject existingAsset = null;
                int arrayIndex = -1;

                if (concreteType != null)
                {
                    string[] guids = AssetDatabase.FindAssets(
                        "t:" + concreteType.Name);
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        existingAsset = AssetDatabase.LoadAssetAtPath(
                            path, concreteType) as ScriptableObject;
                    }
                }

                if (existingAsset != null
                    && assetToIndex.TryGetValue(existingAsset, out int idx))
                {
                    arrayIndex = idx;
                }

                results.Add(new ProviderTemplateEntry
                {
                    Attribute = attr,
                    OpenGenericType = templateType,
                    ConcreteType = concreteType,
                    ExistingAsset = existingAsset,
                    IsInArray = arrayIndex >= 0,
                    ArrayIndex = arrayIndex
                });
            }

            return results;
        }

        private static List<DetachedProviderInfo> FindUnattachedProviders(
            ConcreteManagerInfo info, Type resourceType)
        {
            EnsureCaches();

            Type iface = typeof(IAsyncResourceProvider<>)
                .MakeGenericType(resourceType);

            var inArray = new HashSet<ScriptableObject>();
            if (info.AttachedAsset != null)
            {
                SerializedObject serializedManager =
                    new SerializedObject(info.AttachedAsset);
                var providersProp = serializedManager
                    .FindProperty("initialProviders");
                if (providersProp != null)
                {
                    for (int i = 0; i < providersProp.arraySize; i++)
                    {
                        ScriptableObject so = providersProp
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue as ScriptableObject;
                        if (so != null) inArray.Add(so);
                    }
                }
            }

            var results = new List<DetachedProviderInfo>();

            string ifaceKey = iface.FullName;
            if (s_cachedInterfaceTypes != null
                && s_cachedInterfaceTypes.TryGetValue(
                    ifaceKey, out var typeList))
            {
                foreach (Type type in typeList)
                {
                    if (type.GetCustomAttribute<ResourceProviderAttribute>()
                        != null) continue;

                    string[] guids = AssetDatabase.FindAssets(
                        "t:" + type.Name);
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        ScriptableObject asset = AssetDatabase.LoadAssetAtPath(
                            path, type) as ScriptableObject;
                        if (asset == null) continue;
                        if (inArray.Contains(asset)) continue;

                        results.Add(new DetachedProviderInfo
                        {
                            ConcreteType = type,
                            Asset = asset
                        });
                    }
                }
            }

            return results;
        }

        private static bool IsResourceManagerType(Type type)
        {
            Type baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() ==
                    typeof(AsyncResourceManagerBase<,>))
                {
                    return true;
                }

                baseType = baseType.BaseType;
            }

            return false;
        }

        private static Type GetResourceManagerResourceType(Type type)
        {
            Type baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() ==
                    typeof(AsyncResourceManagerBase<,>))
                {
                    return baseType.GenericTypeArguments[1];
                }

                baseType = baseType.BaseType;
            }

            return null;
        }

        #endregion

        #region Utility Methods

        private int GetTabIndex(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _tabIndices.TryGetValue(key, out int idx);
            return idx;
        }

        private void SetTabIndex(ConcreteManagerInfo info, int idx)
        {
            _tabIndices[info.ConcreteType.FullName] = idx;
        }

        private bool IsExpanded(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _expandedTypes.TryGetValue(key, out bool expanded);
            return expanded;
        }

        private void ToggleExpanded(ConcreteManagerInfo info)
        {
            string key = info.ConcreteType.FullName;
            _expandedTypes[key] = !IsExpanded(info);
        }

        private static bool DrawSmallButton(string label, string tooltip)
        {
            return GUILayout.Button(
                new GUIContent(label, tooltip),
                EditorStyles.miniButton,
                GUILayout.Height(22)
            );
        }

        private static bool GetManagerStatus(ScriptableObject manager)
        {
            foreach (Type iface in manager.GetType().GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition()
                    != typeof(IResourceManager<>)) continue;

                var statusProp = iface.GetProperty("Status");
                if (statusProp == null) continue;

                object status = statusProp.GetValue(manager);
                return status is ResourceManagerStatus s
                    && s == ResourceManagerStatus.Initialized;
            }

            return false;
        }

        private static readonly Regex PascalCaseRegex = new Regex(
            @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled
        );

        private static string PascalCaseToSpaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return PascalCaseRegex.Replace(name, " ");
        }

        private static string NamespaceToFolder(string ns)
        {
            string rootNs = EditorSettings.projectGenerationRootNamespace;
            if (string.IsNullOrEmpty(rootNs)) return "Assets/";

            string sub = ns;
            if (ns.StartsWith(rootNs + "."))
            {
                sub = ns.Substring(rootNs.Length + 1);
            }
            else if (ns == rootNs)
            {
                sub = "";
            }

            return string.IsNullOrEmpty(sub)
                ? "Assets/"
                : "Assets/" + sub.Replace('.', '/') + "/";
        }

        private static string[] GetMissingDefines(string[] requiredDefines)
        {
            if (requiredDefines == null || requiredDefines.Length == 0)
                return Array.Empty<string>();

            string currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup);

            return requiredDefines
                .Where(d => !currentDefines.Contains(d))
                .ToArray();
        }

        private static void EnsureScriptingDefines(string[] defines)
        {
            if (defines == null || defines.Length == 0) return;

            BuildTargetGroup buildTarget =
                EditorUserBuildSettings.selectedBuildTargetGroup;
            string currentDefines =
                PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);

            bool changed = false;
            foreach (string define in defines)
            {
                if (!currentDefines.Contains(define))
                {
                    currentDefines = string.IsNullOrEmpty(currentDefines)
                        ? define
                        : currentDefines + ";" + define;
                    changed = true;
                }
            }

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    buildTarget, currentDefines);
            }
        }

        #endregion

        #region Inline Inspector

        private static void DrawInlineInspector(ScriptableObject asset,
            string header)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(asset);
                try
                {
#if ODIN_INSPECTOR
                    if (editor is Sirenix.OdinInspector.Editor.OdinEditor)
                    {
                        editor.DrawDefaultInspector();
                    }
                    else
                    {
                        editor.OnInspectorGUI();
                    }
#else
                    editor.OnInspectorGUI();
#endif
                }
                finally
                {
                    if (editor != null)
                    {
                        UnityEngine.Object.DestroyImmediate(editor);
                    }
                }
            }
        }

        #endregion

        #region Type Scanning

        private static Type[] GetAllTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .ToArray();
        }

        #endregion
    }
}
