using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor.Tests
{
    public sealed class ConveyorEditorWindowTests
    {
        [Test]
        public void BindingSceneOwnerDoesNotCreateRuntimeOrModifyDefinition()
        {
            var root = new GameObject("Window binding test");
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var window = ScriptableObject.CreateInstance<ConveyorEditorWindow>();
            try
            {
                Conveyor owner = root.AddComponent<Conveyor>();
                owner.Configure("test", definition, null);
                Vector3 firstPoint = definition.Snapshot().Points[0];
                window.Bind(owner);
                Assert.That(window.Target, Is.SameAs(owner));
                Assert.That(owner.IsInitialized, Is.False);
                Assert.That(definition.Snapshot().Points[0], Is.EqualTo(firstPoint));
                window.Bind(null);
                Assert.That(window.Target, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void BindingPrefabAssetRejectsWithoutChangingSelectedSceneOwner()
        {
            string folder = "Assets/__ConveyorWindowTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var root = new GameObject("Window prefab binding test");
            var window = ScriptableObject.CreateInstance<ConveyorEditorWindow>();
            try
            {
                Conveyor owner = root.AddComponent<Conveyor>();
                window.Bind(owner);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Conveyor.prefab");
                Assert.Throws<ArgumentException>(() => window.Bind(prefab.GetComponent<Conveyor>()));
                Assert.That(window.Target, Is.SameAs(owner));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
