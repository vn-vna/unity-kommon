using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem.PlayModeTests
{
    public sealed class ConveyorLifecycleTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private GameObject _root;
        private ConveyorManager _manager;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _root = new GameObject("Conveyor lifecycle test root");
            _manager = Child("manager").AddComponent<ConveyorManager>();
            _manager.Running = false;
            // Prevent Start from running an unconfigured manager before each test sets its bindings.
            _manager.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_manager != null && _manager.World != null) _manager.Shutdown(ConveyorShutdownDisposition.Discard);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            yield return null;
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                if (_assets[index] != null) UnityEngine.Object.Destroy(_assets[index]);
            }
            _assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableSuspendsCargoAndEnableResumesWithoutChangingOwnership()
        {
            Conveyor owner = CreateOwner("a");
            Initialize(owner);
            Assert.That(_manager.World.TryInsertAt(owner.Runtime, new ConveyorPayloadDescriptor(45), 0, 1, out ConveyorPayloadHandle handle), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            owner.gameObject.SetActive(false);
            _manager.StepOnce();
            _manager.World.TryGetState(handle, out ConveyorTransportState held);
            Assert.That(held.Distance, Is.EqualTo(1).Within(0.00001));
            Assert.That(_manager.World.Count, Is.EqualTo(1));
            owner.gameObject.SetActive(true);
            _manager.StepOnce();
            _manager.World.TryGetState(handle, out ConveyorTransportState moved);
            Assert.That(moved.Distance, Is.GreaterThan(1));
            Assert.Throws<InvalidOperationException>(() => _manager.World.Tick(0.1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingOwnerUsesDeclaredDiscardAndPublishesTerminalIdentity()
        {
            Conveyor owner = CreateOwner("a");
            Initialize(owner);
            var results = new List<ConveyorResult>();
            _manager.ResultPublished += results.Add;
            _manager.World.TryInsertAt(owner.Runtime, new ConveyorPayloadDescriptor(819), 0, 1, out _);
            _manager.StepOnce(); results.Clear();
            UnityEngine.Object.Destroy(owner.gameObject);
            yield return null;
            _manager.StepOnce();
            Assert.That(_manager.World.Count, Is.Zero);
            Assert.That(_manager.World.TryGetConveyor("a", out _), Is.False);
            Assert.That(results.Exists(result => result.Kind == ConveyorResultKind.Removed
                && result.RemovalReason == ConveyorRemovalReason.Shutdown && result.Payload.ConsumerKey == 819), Is.True);
        }

        [UnityTest]
        public IEnumerator SharedConfigurationHasIndependentBackendsAcrossSceneOwners()
        {
            Conveyor first = CreateOwner("a");
            Conveyor second = Child("b").AddComponent<Conveyor>();
            second.transform.position = Vector3.right * 10;
            second.Configure("b", first.Definition, first.Configuration);
            Initialize(first, second);
            _manager.World.TryInsertAt(first.Runtime, new ConveyorPayloadDescriptor(1), 0, 1, out ConveyorPayloadHandle a);
            _manager.World.TryInsertAt(second.Runtime, new ConveyorPayloadDescriptor(2), 0, 1, out ConveyorPayloadHandle b);
            _manager.World.SetSpeed(first.Runtime, 0);
            _manager.StepOnce();
            _manager.World.TryGetState(a, out ConveyorTransportState still);
            _manager.World.TryGetState(b, out ConveyorTransportState moving);
            Assert.That(still.Distance, Is.EqualTo(1).Within(0.00001));
            Assert.That(moving.Distance, Is.GreaterThan(1));
            Assert.That(moving.Pose.position.x - still.Pose.position.x, Is.GreaterThan(9));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstancedViewProducesVisiblePixelsInBuiltInPipeline()
        {
            RenderPipelineAsset graphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset qualityPipeline = QualitySettings.renderPipeline;
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            try { yield return InstancedViewProducesVisiblePixelsWithoutPerPayloadRenderers(); }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = graphicsPipeline;
                QualitySettings.renderPipeline = qualityPipeline;
            }
        }

        [UnityTest]
        public IEnumerator InstancedViewProducesVisiblePixelsWithoutPerPayloadRenderers()
            => RenderSmoke(false);

        [UnityTest]
        public IEnumerator PaletteViewShowsFourThousandGrainsWithDifferentInstanceColors()
            => RenderSmoke(true);

        [UnityTest]
        public IEnumerator PaletteViewShowsDifferentInstanceColorsInBuiltInPipeline()
        {
            RenderPipelineAsset graphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset qualityPipeline = QualitySettings.renderPipeline;
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            try { yield return RenderSmoke(true); }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = graphicsPipeline;
                QualitySettings.renderPipeline = qualityPipeline;
            }
        }

        private IEnumerator RenderSmoke(bool palette)
        {
            if (!SystemInfo.supportsInstancing) Assert.Ignore("This graphics device does not support instancing.");
            Shader shader = LoadShader();
            Material material = Own(new Material(shader));
            GameObject primitive = Child("mesh source");
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Own(UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh));
            UnityEngine.Object.Destroy(cube);
            primitive.SetActive(false);
            var view = Own(ScriptableObject.CreateInstance<InstancedPayloadViewConfiguration>());
            view.Configure(new[] { new ConveyorVisualProfile
            {
                id = 0, mesh = mesh, material = material, tint = new Color(0.1f, 1, 0.25f),
                scale = Vector3.one * 0.6f, heightOffset = 0.4f
            } });
            var paletteView = Own(ScriptableObject.CreateInstance<PalettePayloadViewConfiguration>());
            paletteView.Configure(mesh, material, new[] { new ConveyorPaletteEntry(0, new Color(1, 0.1f, 0.1f)),
                new ConveyorPaletteEntry(1, new Color(0.1f, 1, 0.1f)) }, Vector3.one * 0.08f, jitter: 0.04f, rotation: 45,
                variation: 0.4f, verticalJitter: 0.04f);
            Conveyor owner = CreateOwner("a", palette ? (ScriptableObject)paletteView : view);
            int particleCount = palette ? 4000 : 530;
            int lanes = palette ? 16 : 2;
            ConveyorConfiguration settings = owner.Configuration;
            ConveyorPath path = settings.PathProvider.Bake(owner.Definition.Snapshot(), Matrix4x4.identity);
            double spacing = path.Length / 250;
            ((LogicalLaneTransportConfiguration)settings.TransportProvider).Configure(0, palette ? (float)(spacing * 0.99999) : 0.02f,
                new[] { new LogicalPayloadProfile(0, 1), new LogicalPayloadProfile(1, 1) }, palette);
            settings.Configure((ScriptableObject)settings.PathProvider, (ScriptableObject)settings.TransportProvider,
                view: palette ? (ScriptableObject)paletteView : view, laneCount: lanes);
            owner.gameObject.layer = 30;
            _manager.Configure(new[] { owner }, particleCount + 100, 0.1f);
            _manager.Initialize();
            for (int index = 0; index < particleCount; index++)
                Assert.That(_manager.World.TryInsertAt(owner.Runtime, new ConveyorPayloadDescriptor(index, palette && index >= 3584 ? 1 : 0),
                    palette ? index % lanes : 0, palette ? index / lanes * spacing : index * 0.025, out _), Is.EqualTo(ConveyorAdmissionStatus.Accepted));
            _manager.StepOnce();
            _manager.enabled = true;
            var camera = Child("camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0, 8, 0), Quaternion.Euler(90, 0, 0));
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var light = Child("main light").AddComponent<Light>(); light.type = LightType.Directional;
            int resolution = palette ? 256 : 128;
            var texture = Own(new RenderTexture(resolution, resolution, 24)); texture.Create(); camera.targetTexture = texture;
            yield return null;
            yield return null;
            var readback = Own(new Texture2D(resolution, resolution, TextureFormat.RGBA32, false));
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = texture; readback.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            int greenPixels = 0;
            int redPixels = 0;
            foreach (Color pixel in readback.GetPixels())
            {
                if (pixel.g > 0.1f && pixel.g > pixel.r * 1.5f) greenPixels++;
                if (pixel.r > 0.1f && pixel.r > pixel.g * 1.5f) redPixels++;
            }
            Assert.That(greenPixels, Is.GreaterThan(5), "The instanced mesh should be visible in the camera target.");
            if (palette) Assert.That(redPixels, Is.GreaterThan(5), "Colors must vary per instance, not merely per shared material.");
            Assert.That(owner.Runtime.Count, Is.EqualTo(particleCount), "The frame must cross the 511-instance batch boundary.");
            Assert.That(owner.GetComponentsInChildren<MeshRenderer>().Length, Is.Zero, "No Renderer is created for logical cargo.");
            Assert.That(material.enableInstancing, Is.False, "Borrowed material asset must not be modified.");
            TestContext.Out.WriteLine("Graphics device: " + SystemInfo.graphicsDeviceType);
            TestContext.Out.WriteLine("Pipeline: " + (GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.GetType().FullName));
            camera.targetTexture = null;
        }

        private void Initialize(params Conveyor[] owners)
        {
            _manager.Configure(owners, 32, 0.1f);
            _manager.Initialize();
        }

        private Conveyor CreateOwner(string id, ScriptableObject view = null)
        {
            var definition = Own(ScriptableObject.CreateInstance<ConveyorDefinition>());
            var path = Own(ScriptableObject.CreateInstance<RoundedConveyorPathConfiguration>());
            var transport = Own(ScriptableObject.CreateInstance<LogicalLaneTransportConfiguration>());
            var configuration = Own(ScriptableObject.CreateInstance<ConveyorConfiguration>());
            configuration.Configure(path, transport, view: view);
            Conveyor owner = Child(id).AddComponent<Conveyor>();
            owner.Configure(id, definition, configuration);
            return owner;
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(_root.transform, false); return child;
        }

        private T Own<T>(T value) where T : UnityEngine.Object { _assets.Add(value); return value; }

        private static Shader LoadShader()
        {
#if UNITY_EDITOR
            string[] assets = AssetDatabase.FindAssets("ConveyorColor t:Shader");
            Assert.That(assets.Length, Is.GreaterThan(0));
            return AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(assets[0]));
#else
            Assert.Ignore("This smoke fixture loads its actual shader asset in the Editor; player/device builds require separate acceptance.");
            return null;
#endif
        }
    }
}
