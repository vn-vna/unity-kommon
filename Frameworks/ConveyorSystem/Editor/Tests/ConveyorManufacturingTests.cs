using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.Editor.Tests
{
    public sealed class ConveyorManufacturingTests
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private string _assetFolder;

        [SetUp]
        public void Setup()
        {
            string name = "__ConveyorTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _assetFolder = "Assets/" + name;
        }

        [TearDown]
        public void Cleanup()
        {
            for (int index = _owned.Count - 1; index >= 0; index--)
            {
                if (_owned[index] != null) UnityEngine.Object.DestroyImmediate(_owned[index]);
            }
            _owned.Clear();
            AssetDatabase.DeleteAsset(_assetFolder);
        }

        [Test]
        public void ImmutablePathCopiesInputAndWrapsArcLength()
        {
            Vector3[] points = Rectangle();
            var path = new ConveyorPath(points, Vector3.up);
            points[0] = Vector3.one * 500;
            Assert.That(path.GetPoint(0), Is.EqualTo(Vector3.zero));
            Assert.That(path.Evaluate(path.Length + 1).position, Is.EqualTo(path.Evaluate(1).position));
            Assert.That(path.Evaluate(-1).position, Is.EqualTo(path.Evaluate(path.Length - 1).position));
            Assert.That(path.FindNearestDistance(new Vector3(2, 0, -1)), Is.EqualTo(2).Within(0.0001));
        }

        [Test]
        public void RoundedBakeProducesClosedWorldSpacePathWithoutDuplicateSeam()
        {
            var source = new ConveyorDefinitionSnapshot(Rectangle());
            var provider = new RoundedPolylinePathProvider(0.5f, 8);
            Matrix4x4 transform = Matrix4x4.TRS(new Vector3(20, 3, 10), Quaternion.Euler(0, 30, 0), Vector3.one);
            ConveyorPath path = provider.Bake(source, transform);
            Assert.That(path.PointCount, Is.EqualTo(36));
            Assert.That((path.GetPoint(0) - path.GetPoint(path.PointCount - 1)).sqrMagnitude, Is.GreaterThan(0.0001));
            Assert.That(path.Evaluate(0).position.y, Is.EqualTo(3).Within(0.0001));
            Assert.That(path.Length, Is.LessThan(16));
            Assert.That(path.Length, Is.GreaterThan(14));
        }

        [Test]
        public void InvalidControlPathsRejectBeforeCreatingGeometry()
        {
            var provider = new RoundedPolylinePathProvider();
            var crossed = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1) };
            Assert.Throws<ArgumentException>(() => provider.Bake(new ConveyorDefinitionSnapshot(crossed), Matrix4x4.identity));
            var collapsed = new[] { Vector3.zero, Vector3.zero, Vector3.right };
            Assert.Throws<ArgumentException>(() => provider.Bake(new ConveyorDefinitionSnapshot(collapsed), Matrix4x4.identity));
            var nonPlanar = Rectangle(); nonPlanar[2].y = 1;
            Assert.Throws<ArgumentException>(() => provider.Bake(new ConveyorDefinitionSnapshot(nonPlanar), Matrix4x4.identity));
        }

        [Test]
        public void GeometryOwnershipDoesNotDestroyParentOrBorrowedMaterial()
        {
            var owner = Own(new GameObject("manufacture parent"));
            owner.transform.SetPositionAndRotation(new Vector3(10, 2, 4), Quaternion.Euler(0, 20, 0));
            Material material = Own(new Material(FindShader()));
            GeneratedConveyorGeometryProvider geometry = Own(ScriptableObject.CreateInstance<GeneratedConveyorGeometryProvider>());
            geometry.Configure(material);
            ConveyorBuildResult build = ConveyorManufacturer.Build(new ConveyorDefinitionSnapshot(Rectangle()),
                new RoundedPolylinePathProvider(), geometry, owner.transform);
            GameObject root = build.Geometry.Root;
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(root.activeSelf, Is.False);
            Assert.That(mesh.vertexCount, Is.EqualTo(build.Path.PointCount * 16));
            Assert.That(root.transform.parent, Is.EqualTo(owner.transform));
            build.Activate();
            Assert.That(root.activeSelf, Is.True);
            build.Dispose(); build.Dispose();
            Assert.That(root == null, Is.True);
            Assert.That(mesh == null, Is.True);
            Assert.That(owner != null && material != null, Is.True);
        }

        [Test]
        public void GeometryDepthDoesNotChangeTransportCapacity()
        {
            ConveyorPath path = new RoundedPolylinePathProvider().Bake(new ConveyorDefinitionSnapshot(Rectangle()), Matrix4x4.identity);
            Mesh shallow = Own(ConveyorMeshBuilder.Build(path, 1.5f, 0.1f, Matrix4x4.identity));
            Mesh deep = Own(ConveyorMeshBuilder.Build(path, 1.5f, 0.8f, Matrix4x4.identity));
            var context = new ConveyorTransportContext(path, 128, 2, 1.5f);
            using var first = (LogicalLaneBackend)new LogicalLaneTransportProvider().Create(context);
            using var second = (LogicalLaneBackend)new LogicalLaneTransportProvider().Create(context);
            Assert.That(first.SlotsPerLane, Is.EqualTo(second.SlotsPerLane));
            Assert.That(shallow.bounds.size.y, Is.LessThan(deep.bounds.size.y));
        }

        [Test]
        public void PrefabSegmentsSnapToTheSameClosedPathContract()
        {
            ConveyorSegmentAsset segment = CornerSegment();
            ConveyorSegmentPlacement[] placements = ClosedSegments(segment);
            var definition = new ConveyorDefinitionSnapshot(placements);
            ConveyorPath path = new RoundedPolylinePathProvider().Bake(definition, Matrix4x4.identity);
            Assert.That(path.PointCount, Is.GreaterThan(8));
            var provider = Own(ScriptableObject.CreateInstance<PrefabConveyorGeometryProvider>());
            using ConveyorBuildResult build = ConveyorManufacturer.Build(definition, new RoundedPolylinePathProvider(), provider);
            Assert.That(build.Geometry.Root.transform.childCount, Is.EqualTo(4));
            Assert.That(build.Path.Length, Is.EqualTo(path.Length).Within(0.0001));
        }

        [Test]
        public void PrefabGeometryRejectsUnsupportedRoutesWidthAndConnectorMismatch()
        {
            var provider = Own(ScriptableObject.CreateInstance<PrefabConveyorGeometryProvider>());
            Assert.Throws<ArgumentException>(() => ConveyorManufacturer.Build(new ConveyorDefinitionSnapshot(Rectangle()), new RoundedPolylinePathProvider(), provider));
            ConveyorSegmentPlacement[] placements = ClosedSegments(CornerSegment());
            var definition = new ConveyorDefinitionSnapshot(placements);
            Assert.Throws<ArgumentException>(() => ConveyorManufacturer.Build(definition, new RoundedPolylinePathProvider(), provider, width: 2));
            placements[2].position += Vector3.forward;
            Assert.Throws<ArgumentException>(() => new RoundedPolylinePathProvider().Bake(new ConveyorDefinitionSnapshot(placements), Matrix4x4.identity));
        }

        [Test]
        public void InvalidInterfaceBackedProviderReportsConfigurationError()
        {
            var configuration = Own(ScriptableObject.CreateInstance<ConveyorConfiguration>());
            var wrong = Own(ScriptableObject.CreateInstance<ConveyorDefinition>());
            var serialized = new SerializedObject(configuration);
            serialized.FindProperty("pathProvider").objectReferenceValue = wrong;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ArgumentException error = Assert.Throws<ArgumentException>(() => configuration.Validate());
            Assert.That(error.Message, Does.Contain("IConveyorPathProvider"));
        }

        [Test]
        public void FailedSceneOwnerRebuildPreservesPreviousRuntimeAndGeometry()
        {
            var configuration = Own(ScriptableObject.CreateInstance<ConveyorConfiguration>());
            var definition = Own(ScriptableObject.CreateInstance<ConveyorDefinition>());
            var path = Own(ScriptableObject.CreateInstance<RoundedConveyorPathConfiguration>());
            var transport = Own(ScriptableObject.CreateInstance<LogicalLaneTransportConfiguration>());
            var geometry = Own(ScriptableObject.CreateInstance<GeneratedConveyorGeometryProvider>());
            Material material = Own(new Material(FindShader())); geometry.Configure(material);
            configuration.Configure(path, transport, geometry);
            var root = Own(new GameObject("scene owner"));
            Conveyor owner = root.AddComponent<Conveyor>();
            owner.Configure("a", definition, configuration);
            using var world = new ConveyorWorld(32, 128);
            owner.Initialize(world);
            ConveyorRuntime previous = owner.Runtime;
            Mesh previousMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var broken = Own(ScriptableObject.CreateInstance<FailingGeometry>());
            configuration.Configure(path, transport, broken);
            Assert.Throws<InvalidOperationException>(() => owner.Initialize(world));
            Assert.That(owner.Runtime, Is.SameAs(previous));
            Assert.That(previousMesh != null, Is.True);
            Assert.That(root.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(previousMesh));
            owner.Shutdown();
        }

        [Test]
        public void StarterAssetsReloadAndBakePersistsGeneratedMeshWithoutOverwriting()
        {
            string configurationPath = _assetFolder + "/Starter.asset";
            ConveyorConfiguration configuration = ConveyorMenus.CreateStarterSet(configurationPath);
            AssetDatabase.ImportAsset(configurationPath, ImportAssetOptions.ForceUpdate);
            configuration = AssetDatabase.LoadAssetAtPath<ConveyorConfiguration>(configurationPath);
            Assert.DoesNotThrow(configuration.Validate);
            ConveyorDefinition definition = AssetDatabase.LoadAssetAtPath<ConveyorDefinition>(_assetFolder + "/Starter-Definition.asset");
            var root = Own(new GameObject("bake owner"));
            Conveyor owner = root.AddComponent<Conveyor>(); owner.Configure("a", definition, configuration);
            string prefabPath = _assetFolder + "/Baked.prefab";
            GameObject prefab = ConveyorEditorBaker.Bake(owner, prefabPath);
            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(AssetDatabase.Contains(mesh), Is.True);
            Assert.Throws<ArgumentException>(() => ConveyorEditorBaker.Bake(owner, prefabPath));
            Assert.Throws<ArgumentException>(() => ConveyorMenus.CreateStarterSet(configurationPath));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), Is.EqualTo(prefab));
        }

        [Test]
        public void ShaderHasNoCompilationErrors()
        {
            Shader shader = FindShader();
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
        }

        [Test]
        public void RimmedGeometryOwnsCombinedMeshAndPreservesBorrowedMaterials()
        {
            Material belt = Own(new Material(FindShader()));
            Material rim = Own(new Material(FindShader()));
            var provider = Own(ScriptableObject.CreateInstance<RimmedConveyorGeometryProvider>());
            provider.Configure(belt, rim);
            ConveyorBuildResult build = ConveyorManufacturer.Build(new ConveyorDefinitionSnapshot(Rectangle()), new RoundedPolylinePathProvider(), provider);
            GameObject root = build.Geometry.Root;
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(root.GetComponent<MeshRenderer>().sharedMaterials.Length, Is.EqualTo(3));
            Assert.That(mesh.bounds.max.y, Is.GreaterThan(0.2f));
            build.Dispose();
            Assert.That(mesh == null && root == null, Is.True);
            Assert.That(belt != null && rim != null, Is.True);
        }

        [Test]
        public void RoundedRimNormalsFaceAwayFromTubeCenter()
        {
            ConveyorPath path = new ConveyorPath(Rectangle(), Vector3.up);
            Mesh mesh = Own(ConveyorRimMeshBuilder.Build(path, 0.5f, 0.12f, 0.1f, Matrix4x4.identity));
            Vector3 tangent = (Vector3.right - Vector3.forward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            Assert.That(Vector3.Dot(mesh.normals[0], right), Is.GreaterThan(0));
            Assert.That(mesh.vertexCount, Is.EqualTo(path.PointCount * 10));
        }

        [Test]
        public void RoundedRimRejectsCollapsedAndInvalidSettings()
        {
            ConveyorPath path = new ConveyorPath(Rectangle(), Vector3.up);
            Assert.Throws<ArgumentOutOfRangeException>(() => ConveyorRimMeshBuilder.Build(path, 0, 0, 0, Matrix4x4.identity));
            Assert.Throws<ArgumentException>(() => ConveyorRimMeshBuilder.Build(path, 0, 0, 0.1f, Matrix4x4.identity, 2));
            Assert.Throws<ArgumentException>(() => ConveyorRimMeshBuilder.Build(path, float.NaN, 0, 0.1f, Matrix4x4.identity));
        }

        [Test]
        public void PooledViewConsumesPublishedFrameWithoutChangingPayloadOwnership()
        {
            var prefab = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            prefab.SetActive(false);
            var parent = Own(new GameObject("view parent"));
            var profile = new ConveyorVisualProfile { id = 0, prefab = prefab, scale = Vector3.one * 0.2f };
            var world = new ConveyorWorld(8, 64);
            try
            {
                ConveyorPath path = new ConveyorPath(Rectangle(), Vector3.up);
                ConveyorRuntime runtime = world.CreateConveyor("a", path, new LogicalLaneTransportProvider());
                Assert.That(world.TryInsertAt(runtime, new ConveyorPayloadDescriptor(44), 0, 1, out ConveyorPayloadHandle handle), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
                world.Publish();
                using var view = new PooledPayloadView(new ConveyorViewContext(parent.transform, 8), new[] { profile });
                view.Render(runtime.Frame);
                Assert.That(parent.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
                Assert.That(world.TryGetPayload(handle, out _, out ConveyorRuntime owner), Is.True);
                Assert.That(owner, Is.SameAs(runtime));
                Assert.That(world.TryRemove(handle), Is.True);
                world.Publish(); view.Render(runtime.Frame);
                Assert.That(parent.GetComponentsInChildren<MeshRenderer>().Length, Is.Zero);
            }
            finally
            {
                while (world.TryDequeueResult(out _)) { }
                world.Shutdown(ConveyorShutdownDisposition.Discard);
            }
        }

        private T Own<T>(T value) where T : UnityEngine.Object { _owned.Add(value); return value; }
        private static Vector3[] Rectangle() => new[] { Vector3.zero, Vector3.right * 4, new Vector3(4, 0, 4), Vector3.forward * 4 };
        private static Shader FindShader()
        {
            string[] matches = AssetDatabase.FindAssets("ConveyorColor t:Shader");
            Assert.That(matches.Length, Is.GreaterThan(0));
            return AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(matches[0]));
        }

        private ConveyorSegmentAsset CornerSegment()
        {
            var prefab = Own(new GameObject("segment visual"));
            ConveyorSegmentAsset asset = Own(ScriptableObject.CreateInstance<ConveyorSegmentAsset>());
            asset.Configure(prefab, 1, new[]
            {
                Vector3.zero, new Vector3(0, 0, 0.25f), new Vector3(0.25f, 0, 0.75f),
                new Vector3(0.75f, 0, 1), new Vector3(1, 0, 1)
            });
            return asset;
        }

        private static ConveyorSegmentPlacement[] ClosedSegments(ConveyorSegmentAsset asset)
        {
            var placements = new ConveyorSegmentPlacement[4];
            placements[0] = new ConveyorSegmentPlacement { segment = asset };
            for (int index = 1; index < placements.Length; index++) placements[index] = ConveyorPathCompiler.SnapAfter(placements[index - 1], asset);
            return placements;
        }

        private sealed class FailingGeometry : ScriptableObject, IConveyorGeometryProvider
        {
            public ConveyorGeometry Build(in ConveyorGeometryContext context) => throw new InvalidOperationException("deliberate candidate failure");
        }
    }
}
