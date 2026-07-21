using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Editor.ScriptGeneration;
using UnityEditor;
using UnityEngine;
namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader.Editor
{
    public class CreateResourceManagerWindow : EditorWindow
    {
        private static readonly (string label, string typeName)[] BuiltInTypes =
        {
            ("GameObject", "GameObject"),
            ("Sprite", "Sprite"),
            ("Texture2D", "Texture2D"),
            ("Material", "Material"),
            ("AudioClip", "AudioClip"),
            ("Mesh", "Mesh"),
            ("ScriptableObject", "ScriptableObject"),
            ("TextAsset", "TextAsset"),
            ("Shader", "Shader"),
            ("AnimationClip", "AnimationClip"),
            ("SpriteAtlas", "UnityEngine.U2D.SpriteAtlas"),
            ("VideoClip", "UnityEngine.Video.VideoClip"),
        };

        private const int Columns = 3;

        private string _className = "CustomResourceManager";
        private int _selectedBuiltInType;
        private bool _useCustomType;
        private string _customTypeDisplay = "";
        private string _customTypeFullName = "";
        private Type _customType;
        private bool _customTypeHasAssets;
        private string _namespaceName;
        private string _folderPath = "Assets/";
        private string _errorMessage;
        private bool _folderEditing;
        private Vector2 _scrollPos;

        public static void Open()
        {
            var window = GetWindow<CreateResourceManagerWindow>(
                true, "Create Resource Manager", true);
            window.minSize = new Vector2(520, 520);
            window.maxSize = new Vector2(620, 700);

            string rootNs = EditorSettings.projectGenerationRootNamespace;
            window._namespaceName = string.IsNullOrEmpty(rootNs)
                ? "Scripts"
                : rootNs;
            window._folderPath = "Assets/";

            window.ShowUtility();
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            DrawClassNameField();
            GUILayout.Space(8);
            DrawResourceTypeGrid();
            GUILayout.Space(8);
            DrawNamespaceAndFolder();
            GUILayout.Space(8);
            DrawPreview();
            GUILayout.Space(4);
            DrawErrors();
            GUILayout.Space(12);
            DrawButtons();
            EditorGUILayout.EndScrollView();
        }

        private void DrawClassNameField()
        {
            EditorGUILayout.LabelField("Class Name", EditorStyles.boldLabel);
            _className = EditorGUILayout.TextField(_className);

            if (!string.IsNullOrEmpty(_className) && !IsValidClassName(_className))
            {
                _errorMessage = "Class name must be a valid C# identifier.";
            }
            else
            {
                _errorMessage = null;
            }
        }

        private void DrawResourceTypeGrid()
        {
            EditorGUILayout.LabelField("Resource Type", EditorStyles.boldLabel);

            int rows = Mathf.CeilToInt(BuiltInTypes.Length / (float)Columns);

            for (int row = 0; row < rows; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int col = 0; col < Columns; col++)
                    {
                        int index = row * Columns + col;
                        if (index >= BuiltInTypes.Length) break;

                        bool selected = !_useCustomType
                            && _selectedBuiltInType == index;

                        EditorGUI.BeginChangeCheck();
                        bool newSelected = GUILayout.Toggle(
                            selected,
                            BuiltInTypes[index].label,
                            EditorStyles.radioButton,
                            GUILayout.MinWidth(120));
                        if (EditorGUI.EndChangeCheck() && newSelected)
                        {
                            _useCustomType = false;
                            _selectedBuiltInType = index;
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool customSelected = _useCustomType;

                EditorGUI.BeginChangeCheck();
                bool newCustom = GUILayout.Toggle(
                    customSelected, "Custom:",
                    EditorStyles.radioButton, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck() && newCustom)
                {
                    _useCustomType = true;
                }

                string displayText = string.IsNullOrEmpty(_customTypeDisplay)
                    ? "Select Type..."
                    : _customTypeDisplay;

                if (GUILayout.Button(displayText, EditorStyles.popup))
                {
                    var content = new TypeSearchDropdownContent(selected =>
                    {
                        _customTypeDisplay = selected.displayName;
                        _customTypeFullName = selected.fullName;
                        _useCustomType = true;
                        ResolveCustomTypeAndAssets();
                    });
                    PopupWindow.Show(
                        GUILayoutUtility.GetLastRect(), content);
                }

                if (_useCustomType
                    && _customType != null
                    && typeof(ScriptableObject).IsAssignableFrom(_customType)
                    && !_customTypeHasAssets)
                {
                    if (GUILayout.Button("Create Asset", EditorStyles.miniButton,
                            GUILayout.Width(85)))
                    {
                        CreateCustomTypeAsset();
                    }
                }
            }
        }

        private void DrawNamespaceAndFolder()
        {
            EditorGUILayout.LabelField("Namespace & Folder", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Namespace", GUILayout.Width(75));
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName("NamespaceField");
                string newNs = EditorGUILayout.TextField(_namespaceName);
                if (EditorGUI.EndChangeCheck())
                {
                    _namespaceName = newNs;
                    if (!_folderEditing)
                    {
                        _folderPath = NamespaceToFolder(_namespaceName);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", GUILayout.Width(75));
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName("FolderField");
                string newFolder = EditorGUILayout.TextField(_folderPath);
                if (EditorGUI.EndChangeCheck())
                {
                    _folderPath = newFolder;
                    _folderEditing = true;
                }

                if (GUI.GetNameOfFocusedControl() == "FolderField")
                {
                    _folderEditing = true;
                }
                else if (Event.current.type == EventType.Repaint)
                {
                    if (_folderEditing)
                    {
                        _namespaceName = FolderToNamespace(_folderPath);
                    }

                    _folderEditing = false;
                }

                if (GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    string selected = EditorUtility.OpenFolderPanel(
                        "Select Script Folder", "Assets", "");
                    if (string.IsNullOrEmpty(selected)) return;

                    string dataPath = Application.dataPath;
                    if (selected.StartsWith(dataPath))
                    {
                        _folderPath = "Assets"
                            + selected.Substring(dataPath.Length).Replace('\\', '/');
                        _namespaceName = FolderToNamespace(_folderPath);
                    }
                }
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            string code = BuildScriptContent();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.TextArea(code, GUILayout.MinHeight(80));
            }
        }

        private void DrawErrors()
        {
            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            }
        }

        private void DrawButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    Close();
                }

                bool canCreate = !string.IsNullOrEmpty(_className)
                    && IsValidClassName(_className)
                    && !string.IsNullOrEmpty(_folderPath);

                EditorGUI.BeginDisabledGroup(!canCreate);

                if (GUILayout.Button("Create", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    CreateScript();
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private void CreateScript()
        {
            string content = BuildScriptContent();
            string fileName = _className + ".cs";
            string filePath = Path.Combine(
                _folderPath.TrimEnd('/'), fileName).Replace('\\', '/');

            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(filePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "File Exists",
                        $"'{filePath}' already exists. Overwrite?",
                        "Overwrite", "Cancel"))
                {
                    return;
                }
            }

            File.WriteAllText(filePath, content, Encoding.UTF8);
            AssetDatabase.Refresh();

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
            if (script != null)
            {
                EditorGUIUtility.PingObject(script);
                Selection.activeObject = script;
            }

            Close();
        }

        private string BuildScriptContent()
        {
            string resourceType = GetSelectedResourceType();
            string extraUsing = GetRequiredUsingForType(resourceType);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(extraUsing))
            {
                sb.AppendLine(extraUsing);
            }

            sb.AppendLine("using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(_namespaceName))
            {
                sb.AppendLine($"namespace {_namespaceName}");
                sb.AppendLine("{");
            }

            string indent = string.IsNullOrEmpty(_namespaceName) ? "" : "    ";
            sb.AppendLine($"{indent}public sealed class {_className}");
            sb.AppendLine($"{indent}    : AsyncResourceManagerBase<{_className}, {resourceType}>");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(_namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private string GetSelectedResourceType()
        {
            if (_useCustomType)
            {
                return string.IsNullOrEmpty(_customTypeFullName)
                    ? "UnityEngine.Object"
                    : _customTypeFullName;
            }

            return BuiltInTypes[_selectedBuiltInType].typeName;
        }

        private void ResolveCustomTypeAndAssets()
        {
            _customType = null;
            _customTypeHasAssets = false;

            if (string.IsNullOrEmpty(_customTypeFullName)) return;

            _customType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .FirstOrDefault(t => t.FullName == _customTypeFullName);

            if (_customType == null) return;

            _customTypeHasAssets = AssetDatabase.FindAssets(
                "t:" + _customType.Name).Length > 0;
        }

        private void CreateCustomTypeAsset()
        {
            if (_customType == null)
            {
                ResolveCustomTypeAndAssets();
                if (_customType == null) return;
            }

            string suggestedName = _customType.Name;
            string suggestedFolder = _folderPath;
            if (!AssetDatabase.IsValidFolder(suggestedFolder))
                suggestedFolder = "Assets/";

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Asset",
                suggestedName,
                "asset",
                $"Create a new {_customType.Name} asset",
                suggestedFolder);

            if (string.IsNullOrEmpty(path)) return;

            ScriptableObject asset = ScriptableObject.CreateInstance(_customType);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;

            _customTypeHasAssets = true;
        }

        private static string GetRequiredUsingForType(string typeName)
        {
            if (typeName == "UnityEngine.U2D.SpriteAtlas")
                return "using UnityEngine.U2D;";
            if (typeName == "UnityEngine.Video.VideoClip")
                return "using UnityEngine.Video;";

            // For custom types with namespace, extract the namespace-level using
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot > 0)
            {
                string ns = typeName.Substring(0, lastDot);
                if (ns != "UnityEngine" && ns != "UnityEngine.U2D"
                    && ns != "UnityEngine.Video")
                {
                    return $"using {ns};";
                }
            }

            return null;
        }

        private static bool IsValidClassName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }

            return true;
        }

        private static string NamespaceToFolder(string ns)
        {
            string rootNs = EditorSettings.projectGenerationRootNamespace;
            if (string.IsNullOrEmpty(rootNs))
            {
                rootNs = "Scripts";
            }

            string relative = ns;
            if (ns.StartsWith(rootNs + "."))
            {
                relative = ns.Substring(rootNs.Length + 1);
            }
            else if (ns == rootNs)
            {
                relative = "";
            }

            string folder = "Assets/";
            if (!string.IsNullOrEmpty(relative))
            {
                folder += relative.Replace('.', '/') + "/";
            }

            return folder;
        }

        private static string FolderToNamespace(string folder)
        {
            string rootNs = EditorSettings.projectGenerationRootNamespace;
            if (string.IsNullOrEmpty(rootNs))
            {
                rootNs = "Scripts";
            }

            string clean = folder.Replace('\\', '/').Trim('/');
            if (!clean.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return rootNs;
            }

            string subPath = clean.Substring("Assets/".Length).Trim('/');
            if (string.IsNullOrEmpty(subPath))
            {
                return rootNs;
            }

            return rootNs + "." + subPath.Replace('/', '.');
        }

        // ═══════════════════════════════════════════════════
        // ── Type Search Dropdown ──────────────────────────
        // ═══════════════════════════════════════════════════

        public struct TypeSelection
        {
            public string displayName;
            public string fullName;
        }

        private sealed class TypeSearchDropdownContent : PopupWindowContent
        {
            private const float ItemHeight = 24f;
            private const float SearchHeight = 28f;
            private const float FooterHeight = 30f;
            private const float MaxHeight = 400f;
            private const float MinWidth = 340f;
            private const float DebounceSeconds = 0.2f;

            private static List<Type> _allTypes;
            private static readonly Dictionary<string, string> SourceCache =
                new Dictionary<string, string>();

            private readonly Action<TypeSelection> _onSelected;
            private string _searchText = "";
            private string _appliedSearch = "";
            private double _lastTypeTime;
            private bool _needsFilter;
            private List<Type> _filteredTypes;
            private Vector2 _scrollPos;

            public TypeSearchDropdownContent(Action<TypeSelection> onSelected)
            {
                _onSelected = onSelected;
                EnsureTypeCache();
                _filteredTypes = _allTypes;
            }

            public override Vector2 GetWindowSize()
            {
                int count = _filteredTypes?.Count ?? 0;
                float h = SearchHeight + 8f + count * ItemHeight + 8f + FooterHeight;
                return new Vector2(MinWidth, Mathf.Min(h, MaxHeight));
            }

            public override void OnGUI(Rect rect)
            {
                DrawSearchBar();
                GUILayout.Space(4);
                DrawTypeList();
                GUILayout.Space(4);
                DrawFooter();
            }

            private void DrawSearchBar()
            {
                EditorGUI.BeginChangeCheck();
                _searchText = EditorGUILayout.TextField(
                    EditorGUIUtility.IconContent("Search Icon"),
                    _searchText, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck())
                {
                    _lastTypeTime = EditorApplication.timeSinceStartup;
                    _needsFilter = true;
                }

                if (_needsFilter
                    && EditorApplication.timeSinceStartup - _lastTypeTime
                    > DebounceSeconds)
                {
                    ApplyFilter();
                }
            }

            private void ApplyFilter()
            {
                _needsFilter = false;
                _appliedSearch = _searchText;

                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    _filteredTypes = _allTypes;
                }
                else
                {
                    string[] tokens = _searchText.Split(
                        new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    _filteredTypes = _allTypes
                        .Where(t =>
                        {
                            string name = GetDisplayName(t);
                            return tokens.Any(token =>
                                name.IndexOf(token,
                                    StringComparison.OrdinalIgnoreCase) >= 0);
                        })
                        .ToList();
                }

                _scrollPos = Vector2.zero;
            }

            private void DrawTypeList()
            {
                if (_filteredTypes == null || _filteredTypes.Count == 0)
                {
                    EditorGUILayout.LabelField("  No types found.",
                        EditorStyles.miniLabel);
                    return;
                }

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

                foreach (Type type in _filteredTypes)
                {
                    DrawTypeRow(type);
                }

                EditorGUILayout.EndScrollView();
            }

            private void DrawTypeRow(Type type)
            {
                string displayName = GetDisplayName(type);
                string source = GetSourceLabel(type);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button(displayName,
                            EditorStyles.label,
                            GUILayout.Height(ItemHeight - 4)))
                    {
                        _onSelected?.Invoke(new TypeSelection
                        {
                            displayName = displayName,
                            fullName = type.FullName
                        });
                        editorWindow.Close();
                    }

                    GUILayout.FlexibleSpace();

                    GUIStyle sourceStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
                    };
                    EditorGUILayout.LabelField(
                        source, sourceStyle, GUILayout.Width(120));
                }
            }

            private void DrawFooter()
            {
                Rect div = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.3f));

                if (GUILayout.Button("+ Create New Type...",
                        GUILayout.Height(FooterHeight - 4)))
                {
                    editorWindow.Close();
                    ScriptTemplateGenerator.CreatePluginScript(
                        null, "Assets/", typeof(ScriptableObject),
                        ScriptTemplateGenerator.GenerationMode.InterfaceImplementation);
                }
            }

            private static string GetDisplayName(Type type)
            {
                if (type.FullName.StartsWith("UnityEngine."))
                    return type.Name;
                if (type.FullName.StartsWith("UnityEditor."))
                    return type.Name;
                return type.FullName;
            }

            private static string GetSourceLabel(Type type)
            {
                string ns = type.Namespace ?? "";

                if (SourceCache.TryGetValue(ns, out string cached))
                    return cached;

                if (ns.StartsWith("UnityEngine") || ns.StartsWith("UnityEditor"))
                    cached = "Unity";
                else if (string.IsNullOrEmpty(ns))
                    cached = "Global";
                else
                {
                    int firstDot = ns.IndexOf('.');
                    cached = firstDot > 0 ? ns.Substring(0, firstDot) : ns;
                }

                SourceCache[ns] = cached;
                return cached;
            }

            private static void EnsureTypeCache()
            {
                if (_allTypes != null) return;

                _allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .Where(t =>
                        t.IsClass
                        && !t.IsAbstract
                        && !t.IsGenericTypeDefinition
                        && t.IsPublic
                        && typeof(UnityEngine.Object).IsAssignableFrom(t)
                        && !t.FullName.StartsWith("UnityEditor."))
                    .OrderBy(t => GetDisplayName(t), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                SourceCache.Clear();
            }
        }
    }
}
