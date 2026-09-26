using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.Scheherazade.Common.Integration.InAppPurchase.Validation;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Com.Scheherazade.Integration
{
    internal static class InAppPurchaseReceiptValidationSettingsTab
    {
        private static readonly RuntimePlatform[] Platforms =
        {
            RuntimePlatform.WindowsEditor,
            RuntimePlatform.OSXEditor,
            RuntimePlatform.LinuxEditor,
            RuntimePlatform.IPhonePlayer,
            RuntimePlatform.Android
        };

        private static readonly string[] PlatformLabels =
        {
            "Windows Editor", "macOS Editor", "Linux Editor", "iOS", "Android"
        };

        private const string SelectedPlatformPrefKey =
            "Scheherazade.IAP.ReceiptValidation.SelectedPlatform";

        private static readonly Dictionary<string, ReorderableList> Lists = new();
        private static int _selectedPlatformIndex = EditorPrefs.GetInt(SelectedPlatformPrefKey, 0);
        private static InAppPurchaseReceiptValidationStep _selectedStep;
        private static UnityEditor.Editor _selectedStepEditor;
        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;

        static InAppPurchaseReceiptValidationSettingsTab()
        {
            Undo.undoRedoPerformed += ClearCachedEditors;
            AssemblyReloadEvents.beforeAssemblyReload += ClearCachedEditors;
        }

        internal static void Draw(ScriptableObject manager)
        {
            if (manager == null)
            {
                EditorGUILayout.HelpBox("Create an In-App Purchase manager before configuring receipt validation.", MessageType.Info);
                return;
            }

            DrawHeader();

            var managerObject = new SerializedObject(manager);
            managerObject.Update();
            SerializedProperty pipelineProperty = managerObject.FindProperty("receiptValidationPipeline");
            if (pipelineProperty == null)
            {
                EditorGUILayout.HelpBox("The selected manager does not expose the Scheherazade receipt-validation binding.", MessageType.Error);
                return;
            }

            DrawPipelineBinding(manager, managerObject, pipelineProperty);
            var pipeline = pipelineProperty.objectReferenceValue as InAppPurchaseReceiptValidationPipeline;
            if (pipeline == null)
            {
                DrawEmptyState(manager, managerObject, pipelineProperty);
                return;
            }

            DrawPipelineEditor(pipeline);
        }

        private static void DrawHeader()
        {
            _titleStyle ??= new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                fixedHeight = 25
            };
            _subtitleStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11
            };

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Receipt Validation", _titleStyle);
                GUILayout.Label(
                    "Build an ordered, fail-closed validation flow for each target platform. " +
                    "Drag steps to reorder them and edit each step's accepted platforms in place.",
                    _subtitleStyle
                );
            }
            EditorGUILayout.Space(4);
        }

        private static void DrawPipelineBinding(
            ScriptableObject manager,
            SerializedObject managerObject,
            SerializedProperty pipelineProperty
        )
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Pipeline Asset", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(pipelineProperty, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                {
                    managerObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                    Lists.Clear();
                    _selectedStep = null;
                    DestroyStepEditor();
                }

                var pipeline = pipelineProperty.objectReferenceValue as InAppPurchaseReceiptValidationPipeline;
                if (pipeline == null) return;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Select Asset", EditorStyles.miniButton, GUILayout.Width(96)))
                    {
                        Selection.activeObject = pipeline;
                        EditorGUIUtility.PingObject(pipeline);
                    }
                }
            }
        }

        private static void DrawEmptyState(
            ScriptableObject manager,
            SerializedObject managerObject,
            SerializedProperty pipelineProperty
        )
        {
            EditorGUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(8);
                EditorGUILayout.LabelField("No receipt-validation pipeline is assigned", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Create a ready-to-edit pipeline with separate Editor, iOS, and Android groups. " +
                    "Android remains safely blocked until a generated Tangle-backed validator is added.",
                    EditorStyles.wordWrappedLabel
                );
                GUILayout.Space(8);

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
                if (GUILayout.Button("Create Default Receipt Validation Pipeline", GUILayout.Height(32)))
                {
                    CreateDefaultPipeline(manager, managerObject, pipelineProperty);
                }
                GUI.backgroundColor = previous;
                GUILayout.Space(5);
            }
        }

        private static void DrawPipelineEditor(InAppPurchaseReceiptValidationPipeline pipeline)
        {
            var pipelineObject = new SerializedObject(pipeline);
            pipelineObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty required = pipelineObject.FindProperty("requireValidation");
                EditorGUILayout.PropertyField(required, new GUIContent(
                    "Require Validation",
                    "Require at least one authenticity step to pass before fulfillment."
                ));
                pipelineObject.ApplyModifiedProperties();
            }

            _selectedPlatformIndex = Mathf.Clamp(_selectedPlatformIndex, 0, Platforms.Length - 1);
            EditorGUILayout.Space(4);
            int selected = GUILayout.Toolbar(_selectedPlatformIndex, PlatformLabels, GUILayout.Height(27));
            if (selected != _selectedPlatformIndex)
            {
                _selectedPlatformIndex = selected;
                EditorPrefs.SetInt(SelectedPlatformPrefKey, selected);
                _selectedStep = null;
                DestroyStepEditor();
            }
            RuntimePlatform platform = Platforms[_selectedPlatformIndex];

            pipelineObject.Update();
            SerializedProperty entries = pipelineObject.FindProperty("platformSteps");
            int entryIndex = FindPlatformEntry(entries, platform);
            if (entryIndex < 0)
            {
                DrawMissingPlatform(pipeline, pipelineObject, entries, platform);
                return;
            }

            SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
            SerializedProperty steps = entry.FindPropertyRelative("steps");
            DrawPlatformStatus(pipeline, platform);
            DrawStepsList(pipeline, pipelineObject, steps, platform);
            DrawSelectedStep(platform);
        }

        private static void DrawMissingPlatform(
            InAppPurchaseReceiptValidationPipeline pipeline,
            SerializedObject pipelineObject,
            SerializedProperty entries,
            RuntimePlatform platform
        )
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "This platform has no validation group. Purchases for it fail closed.",
                    MessageType.Warning
                );
                if (GUILayout.Button("Add " + PlatformDisplayName(platform) + " Configuration", GUILayout.Height(28)))
                {
                    Undo.RecordObject(pipeline, "Add Receipt Validation Platform");
                    int index = entries.arraySize;
                    entries.InsertArrayElementAtIndex(index);
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("platform").intValue = (int)platform;
                    entry.FindPropertyRelative("steps").ClearArray();
                    pipelineObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(pipeline);
                    Lists.Clear();
                }
            }
        }

        private static void DrawPlatformStatus(
            InAppPurchaseReceiptValidationPipeline pipeline,
            RuntimePlatform platform
        )
        {
            bool valid = pipeline.ValidateConfiguration(platform, out string reason);
            Color color = valid ? new Color(0.25f, 0.62f, 0.34f) : new Color(0.78f, 0.43f, 0.16f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect row = EditorGUILayout.GetControlRect(false, 24);
                Rect badge = new Rect(row.x, row.y + 3, 9, 9);
                EditorGUI.DrawRect(badge, color);
                EditorGUI.LabelField(
                    new Rect(row.x + 16, row.y, row.width - 16, row.height),
                    valid ? PlatformDisplayName(platform) + " is ready" : PlatformDisplayName(platform) + " requires attention",
                    EditorStyles.boldLabel
                );
                if (!valid)
                {
                    EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
                }
                if (platform == RuntimePlatform.Android &&
                    (!pipeline.StepsByPlatform.TryGetValue(platform, out var androidSteps) ||
                     !androidSteps.Any(step => step is GooglePlayTangleReceiptValidationStepBase)))
                {
                    EditorGUILayout.HelpBox(
                        "Generate GooglePlayTangle with Unity IAP Receipt Validation Obfuscator, " +
                        "compile a concrete Tangle validation step, then add it to this Android list.",
                        MessageType.Info
                    );
                }
            }
        }

        private static void DrawStepsList(
            InAppPurchaseReceiptValidationPipeline pipeline,
            SerializedObject pipelineObject,
            SerializedProperty steps,
            RuntimePlatform platform
        )
        {
            string key = pipeline.GetInstanceID() + ":" + (int)platform;
            if (!Lists.TryGetValue(key, out ReorderableList list) ||
                list.serializedProperty.serializedObject.targetObject != pipeline)
            {
                list = CreateList(pipeline, pipelineObject, steps, platform);
                Lists[key] = list;
            }
            else
            {
                list.serializedProperty = steps;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                list.DoLayoutList();
            }
            if (pipelineObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(pipeline);
            }
        }

        private static ReorderableList CreateList(
            InAppPurchaseReceiptValidationPipeline pipeline,
            SerializedObject pipelineObject,
            SerializedProperty steps,
            RuntimePlatform platform
        )
        {
            ReorderableList list = null;
            list = new ReorderableList(pipelineObject, steps, true, true, true, true)
            {
                elementHeight = 39,
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, PlatformDisplayName(platform) + " Validation Steps", EditorStyles.boldLabel),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    SerializedProperty currentSteps = list.serializedProperty;
                    if (index < 0 || index >= currentSteps.arraySize) return;
                    SerializedProperty element = currentSteps.GetArrayElementAtIndex(index);
                    Rect fieldRect = new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight);
                    EditorGUI.PropertyField(fieldRect, element, new GUIContent((index + 1) + "."));
                    var step = element.objectReferenceValue as InAppPurchaseReceiptValidationStep;
                    if (step != null && !step.IsApplicable(platform))
                    {
                        Rect warningRect = new Rect(rect.x + 18, rect.y + 21, rect.width - 18, 16);
                        EditorGUI.LabelField(warningRect, "Step does not accept " + PlatformDisplayName(platform), EditorStyles.miniLabel);
                    }
                },
                onSelectCallback = selected =>
                {
                    SerializedProperty currentSteps = selected.serializedProperty;
                    _selectedStep = selected.index >= 0 && selected.index < currentSteps.arraySize
                        ? currentSteps.GetArrayElementAtIndex(selected.index).objectReferenceValue as InAppPurchaseReceiptValidationStep
                        : null;
                    DestroyStepEditor();
                },
                onAddDropdownCallback = (rect, selected) => ShowAddStepMenu(pipeline, platform),
                onRemoveCallback = selected =>
                {
                    if (selected.index < 0) return;
                    SerializedProperty currentSteps = selected.serializedProperty;
                    int previousSize = currentSteps.arraySize;
                    currentSteps.DeleteArrayElementAtIndex(selected.index);
                    if (currentSteps.arraySize == previousSize)
                    {
                        currentSteps.DeleteArrayElementAtIndex(selected.index);
                    }
                    _selectedStep = null;
                    DestroyStepEditor();
                    pipelineObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(pipeline);
                },
                onReorderCallback = selected =>
                {
                    pipelineObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(pipeline);
                }
            };
            return list;
        }

        private static void ShowAddStepMenu(
            InAppPurchaseReceiptValidationPipeline pipeline,
            RuntimePlatform platform
        )
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Existing Step Slot"), false, () =>
            {
                var serialized = new SerializedObject(pipeline);
                SerializedProperty targetSteps = FindPlatformSteps(serialized, platform);
                if (targetSteps == null) return;
                targetSteps.InsertArrayElementAtIndex(targetSteps.arraySize);
                targetSteps.GetArrayElementAtIndex(targetSteps.arraySize - 1).objectReferenceValue = null;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(pipeline);
            });
            menu.AddSeparator(string.Empty);

            Type[] types = TypeCache.GetTypesDerivedFrom<InAppPurchaseReceiptValidationStep>()
                .Where(type => !type.IsAbstract && !type.ContainsGenericParameters &&
                    typeof(ScriptableObject).IsAssignableFrom(type) &&
                    !type.Assembly.GetName().Name.EndsWith("Tests", StringComparison.Ordinal))
                .OrderBy(type => type.Name)
                .ToArray();
            foreach (Type type in types)
            {
                Type captured = type;
                menu.AddItem(
                    new GUIContent("Create New/" + ObjectNames.NicifyVariableName(type.Name)),
                    false,
                    () => CreateAndAddStep(pipeline, platform, captured)
                );
            }
            menu.ShowAsContext();
        }

        private static void CreateAndAddStep(
            InAppPurchaseReceiptValidationPipeline pipeline,
            RuntimePlatform platform,
            Type type
        )
        {
            string pipelinePath = AssetDatabase.GetAssetPath(pipeline);
            string folder = Path.GetDirectoryName(pipelinePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) return;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + type.Name + ".asset");
            var step = ScriptableObject.CreateInstance(type) as InAppPurchaseReceiptValidationStep;
            if (step == null) return;
            step.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(step, path);
            Undo.RegisterCreatedObjectUndo(step, "Create Receipt Validation Step");
            SetAcceptedPlatforms(step, platform);

            var serialized = new SerializedObject(pipeline);
            SerializedProperty targetSteps = FindPlatformSteps(serialized, platform);
            if (targetSteps != null)
            {
                int index = targetSteps.arraySize;
                targetSteps.InsertArrayElementAtIndex(index);
                targetSteps.GetArrayElementAtIndex(index).objectReferenceValue = step;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(pipeline);
                _selectedStep = step;
                DestroyStepEditor();
            }
            AssetDatabase.SaveAssets();
        }

        private static void DrawSelectedStep(RuntimePlatform platform)
        {
            if (_selectedStep == null) return;
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Selected Step", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(54)))
                    {
                        EditorGUIUtility.PingObject(_selectedStep);
                    }
                }
                EditorGUILayout.LabelField(_selectedStep.name, EditorStyles.largeLabel);
                if (!_selectedStep.IsApplicable(platform))
                {
                    EditorGUILayout.HelpBox(
                        "Add " + PlatformDisplayName(platform) + " to this step's Accepted Platforms before using it here.",
                        MessageType.Warning
                    );
                }
                UnityEditor.Editor.CreateCachedEditor(_selectedStep, null, ref _selectedStepEditor);
                _selectedStepEditor?.OnInspectorGUI();
            }
        }

        private static void CreateDefaultPipeline(
            ScriptableObject manager,
            SerializedObject managerObject,
            SerializedProperty pipelineProperty
        )
        {
            string managerPath = AssetDatabase.GetAssetPath(manager);
            string parent = Path.GetDirectoryName(managerPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                EditorUtility.DisplayDialog("Receipt Validation", "Save the manager asset before creating validation assets.", "OK");
                return;
            }

            string folder = parent + "/Validation";
            EnsureFolder(folder);
            string ownerName = manager.name.EndsWith("Manager", StringComparison.Ordinal)
                ? manager.name.Substring(0, manager.name.Length - "Manager".Length)
                : manager.name;
            var pipeline = LoadOrCreate<InAppPurchaseReceiptValidationPipeline>(
                folder + "/" + ownerName + "ReceiptValidationPipeline.asset"
            );
            var single = LoadOrCreate<SingleProductOrderValidationStep>(
                folder + "/SingleProductOrderValidationStep.asset"
            );
            var editor = LoadOrCreate<EditorSimulatedReceiptValidationStep>(
                folder + "/EditorSimulatedReceiptValidationStep.asset"
            );
            var storeKit = LoadOrCreate<StoreKit2ReceiptValidationStep>(
                folder + "/StoreKit2ReceiptValidationStep.asset"
            );

            if (pipeline == null || single == null || editor == null || storeKit == null) return;

            SetAcceptedPlatforms(single, Platforms);
            SetAcceptedPlatforms(
                editor,
                RuntimePlatform.WindowsEditor,
                RuntimePlatform.OSXEditor,
                RuntimePlatform.LinuxEditor
            );
            SetAcceptedPlatforms(storeKit, RuntimePlatform.IPhonePlayer);
            ConfigureDefaultPipeline(pipeline, single, editor, storeKit);

            Undo.RecordObject(manager, "Assign Receipt Validation Pipeline");
            pipelineProperty.objectReferenceValue = pipeline;
            managerObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            Lists.Clear();
            Selection.activeObject = pipeline;
        }

        private static void ConfigureDefaultPipeline(
            InAppPurchaseReceiptValidationPipeline pipeline,
            SingleProductOrderValidationStep single,
            EditorSimulatedReceiptValidationStep editor,
            StoreKit2ReceiptValidationStep storeKit
        )
        {
            Undo.RecordObject(pipeline, "Configure Receipt Validation Pipeline");
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("requireValidation").boolValue = true;
            SerializedProperty entries = serialized.FindProperty("platformSteps");
            entries.arraySize = Platforms.Length;
            for (int i = 0; i < Platforms.Length; i++)
            {
                RuntimePlatform platform = Platforms[i];
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("platform").intValue = (int)platform;
                SerializedProperty steps = entry.FindPropertyRelative("steps");
                bool isEditor = platform == RuntimePlatform.WindowsEditor ||
                    platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.LinuxEditor;
                bool isIos = platform == RuntimePlatform.IPhonePlayer;
                steps.arraySize = isEditor || isIos ? 2 : 1;
                steps.GetArrayElementAtIndex(0).objectReferenceValue = single;
                if (isEditor) steps.GetArrayElementAtIndex(1).objectReferenceValue = editor;
                if (isIos) steps.GetArrayElementAtIndex(1).objectReferenceValue = storeKit;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing is T typed) return typed;
            if (existing != null)
            {
                EditorUtility.DisplayDialog(
                    "Receipt Validation Asset Conflict",
                    "Cannot create " + typeof(T).Name + " because another asset already exists at:\n" + path,
                    "OK"
                );
                return null;
            }

            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            Undo.RegisterCreatedObjectUndo(asset, "Create " + typeof(T).Name);
            return asset;
        }

        private static void SetAcceptedPlatforms(
            InAppPurchaseReceiptValidationStep step,
            params RuntimePlatform[] platforms
        )
        {
            var serialized = new SerializedObject(step);
            SerializedProperty accepted = serialized.FindProperty("acceptedPlatforms");
            accepted.arraySize = platforms.Length;
            for (int i = 0; i < platforms.Length; i++)
            {
                accepted.GetArrayElementAtIndex(i).intValue = (int)platforms[i];
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(step);
        }

        private static int FindPlatformEntry(SerializedProperty entries, RuntimePlatform platform)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("platform").intValue == (int)platform)
                    return i;
            }
            return -1;
        }

        private static SerializedProperty FindPlatformSteps(
            SerializedObject pipeline,
            RuntimePlatform platform
        )
        {
            pipeline.Update();
            SerializedProperty entries = pipeline.FindProperty("platformSteps");
            int index = FindPlatformEntry(entries, platform);
            return index < 0 ? null : entries.GetArrayElementAtIndex(index).FindPropertyRelative("steps");
        }

        private static void EnsureFolder(string folder)
        {
            string normalized = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized)) return;
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(normalized));
        }

        private static string PlatformDisplayName(RuntimePlatform platform)
        {
            int index = Array.IndexOf(Platforms, platform);
            return index >= 0 ? PlatformLabels[index] : ObjectNames.NicifyVariableName(platform.ToString());
        }

        private static void ClearCachedEditors()
        {
            Lists.Clear();
            _selectedStep = null;
            DestroyStepEditor();
        }

        private static void DestroyStepEditor()
        {
            if (_selectedStepEditor == null) return;
            UnityEngine.Object.DestroyImmediate(_selectedStepEditor);
            _selectedStepEditor = null;
        }
    }
}
