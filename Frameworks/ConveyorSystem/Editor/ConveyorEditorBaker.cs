using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor
{
    public static class ConveyorEditorBaker
    {
        public static GameObject Bake(Conveyor owner, string prefabPath)
        {
            if (owner == null || owner.Configuration == null || owner.Definition == null) throw new ArgumentException("Conveyor bindings are required.");
            ValidateNewPath(prefabPath, ".prefab");
            using ConveyorBuildResult candidate = ConveyorManufacturer.Build(owner.Definition.Snapshot(), owner.Configuration.PathProvider,
                owner.Configuration.GeometryProvider, owner.transform, owner.Configuration.Width, owner.Configuration.Channels);
            if (candidate.Geometry?.Root == null) throw new ArgumentException("Choose a geometry provider before baking.");
            GameObject root = candidate.Geometry.Root;
            var created = new List<string>();
            var copies = new Dictionary<Mesh, Mesh>();
            try
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (int index = 0; index < filters.Length; index++)
                {
                    Mesh source = filters[index].sharedMesh;
                    if (source == null || AssetDatabase.Contains(source)) continue;
                    if (!copies.TryGetValue(source, out Mesh copy))
                    {
                        string meshPath = Path.ChangeExtension(prefabPath, null) + "-mesh-" + copies.Count + ".asset";
                        ValidateNewPath(meshPath, ".asset");
                        copy = UnityEngine.Object.Instantiate(source);
                        try { AssetDatabase.CreateAsset(copy, meshPath); }
                        catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
                        copies.Add(source, copy);
                        created.Add(meshPath);
                    }
                    filters[index].sharedMesh = copy;
                }
                root.SetActive(true);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Unity could not save the geometry prefab.");
                AssetDatabase.SaveAssets();
                return prefab;
            }
            catch
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) AssetDatabase.DeleteAsset(prefabPath);
                for (int index = 0; index < created.Count; index++) AssetDatabase.DeleteAsset(created[index]);
                throw;
            }
        }

        public static void ValidateNewPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains("..") || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a new " + extension + " path under Assets/.");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path) || File.Exists(path + ".meta"))
                throw new ArgumentException("The selected path already exists; existing user assets are never overwritten.");
            if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\', '/')))
                throw new ArgumentException("Choose an existing destination folder.");
        }
    }
}
