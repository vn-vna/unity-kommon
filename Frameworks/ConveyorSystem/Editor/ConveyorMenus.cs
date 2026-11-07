using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor
{
    public static class ConveyorMenus
    {
        [MenuItem("GameObject/Scheherazade/Conveyor System/Conveyor", false, 10)]
        private static void CreateConveyor(MenuCommand command)
        {
            var root = new GameObject("Conveyor");
            GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(root, "Create conveyor");
            Undo.AddComponent<Conveyor>(root);
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/Scheherazade/Conveyor System/Manager", false, 11)]
        private static void CreateManager(MenuCommand command)
        {
            var root = new GameObject("Conveyor Manager");
            GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(root, "Create conveyor manager");
            Undo.AddComponent<ConveyorManager>(root);
            Selection.activeGameObject = root;
        }

        [MenuItem("Tools/Scheherazade/Conveyor System/Create Starter Assets…")]
        private static void CreateStarterAssets()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create conveyor starter assets", "Conveyor", "asset", "Choose a new configuration path; related assets are created beside it.");
            if (string.IsNullOrEmpty(path)) return;
            try { Selection.activeObject = CreateStarterSet(path); }
            catch (Exception error) { EditorUtility.DisplayDialog("Conveyor setup", error.Message, "OK"); }
        }

        public static ConveyorConfiguration CreateStarterSet(string configurationPath)
        {
            ConveyorEditorBaker.ValidateNewPath(configurationPath, ".asset");
            string prefix = Path.ChangeExtension(configurationPath, null);
            string[] suffixes = { "-Definition.asset", "-Path.asset", "-Transport.asset", "-Geometry.asset", "-View.asset", "-CargoMesh.asset", "-Belt.mat", "-Cargo.mat" };
            for (int index = 0; index < suffixes.Length; index++)
                ConveyorEditorBaker.ValidateNewPath(prefix + suffixes[index], suffixes[index].EndsWith(".mat", StringComparison.Ordinal) ? ".mat" : ".asset");
            string[] shaders = AssetDatabase.FindAssets("ConveyorColor t:Shader");
            if (shaders.Length == 0) throw new ArgumentException("Import ConveyorSystem's ConveyorColor.shader first.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(shaders[0]));
            if (shader == null || shader.name != "Scheherazade/Conveyor/Color") throw new ArgumentException("The conveyor shader search returned an incompatible asset.");
            var created = new List<string>();
            var unsaved = new List<UnityEngine.Object>();
            GameObject cube = null;
            try
            {
                var definition = ScriptableObject.CreateInstance<ConveyorDefinition>(); unsaved.Add(definition);
                var path = ScriptableObject.CreateInstance<RoundedConveyorPathConfiguration>(); unsaved.Add(path);
                var transport = ScriptableObject.CreateInstance<LogicalLaneTransportConfiguration>(); unsaved.Add(transport);
                var geometry = ScriptableObject.CreateInstance<GeneratedConveyorGeometryProvider>(); unsaved.Add(geometry);
                var view = ScriptableObject.CreateInstance<InstancedPayloadViewConfiguration>(); unsaved.Add(view);
                var belt = new Material(shader); unsaved.Add(belt); belt.SetColor("_BaseColor", new Color(0.12f, 0.22f, 0.28f));
                var cargo = new Material(shader) { enableInstancing = true }; unsaved.Add(cargo);
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh mesh = UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh); unsaved.Add(mesh);
                mesh.name = "Conveyor cargo cube";
                geometry.Configure(belt);
                view.Configure(new[] { new ConveyorVisualProfile
                {
                    id = 0, mesh = mesh, material = cargo, scale = Vector3.one * 0.25f,
                    tint = new Color(0.45f, 0.9f, 0.75f), heightOffset = 0.15f
                } });
                var configuration = ScriptableObject.CreateInstance<ConveyorConfiguration>(); unsaved.Add(configuration);
                configuration.Configure(path, transport, geometry, view);
                UnityEngine.Object[] assets = { definition, path, transport, geometry, view, mesh, belt, cargo };
                for (int index = 0; index < assets.Length; index++)
                {
                    AssetDatabase.CreateAsset(assets[index], prefix + suffixes[index]);
                    created.Add(prefix + suffixes[index]); unsaved.Remove(assets[index]);
                }
                AssetDatabase.CreateAsset(configuration, configurationPath);
                created.Add(configurationPath); unsaved.Remove(configuration);
                AssetDatabase.SaveAssets();
                return configuration;
            }
            catch
            {
                for (int index = created.Count - 1; index >= 0; index--) AssetDatabase.DeleteAsset(created[index]);
                throw;
            }
            finally
            {
                if (cube != null) UnityEngine.Object.DestroyImmediate(cube);
                for (int index = 0; index < unsaved.Count; index++) UnityEngine.Object.DestroyImmediate(unsaved[index]);
            }
        }
    }
}
