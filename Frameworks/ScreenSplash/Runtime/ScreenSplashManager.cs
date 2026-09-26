using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Scheherazade.Common.ScreenSplash
{
    /// <summary>Opaque handle for one active screen-splash playback.</summary>
    public readonly struct ScreenSplashHandle : IEquatable<ScreenSplashHandle>
    {
        public static readonly ScreenSplashHandle Invalid = default;

        private readonly int _id;

        internal ScreenSplashHandle(int id)
        {
            _id = id;
        }

        public bool IsValid => _id != 0;
        internal int Id => _id;

        public bool Equals(ScreenSplashHandle other)
        {
            return _id == other._id;
        }

        public override bool Equals(object obj)
        {
            return obj is ScreenSplashHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _id;
        }

        public static bool operator ==(
            ScreenSplashHandle left,
            ScreenSplashHandle right
        )
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ScreenSplashHandle left,
            ScreenSplashHandle right
        )
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Draws configurable, screen-fixed radial splashes for gameplay events. The
    /// overlay is a separate Canvas, so camera motion and gameplay UI stay stable.
    /// </summary>
    [AddComponentMenu("Scheherazade/Screen Splash Manager")]
    [DisallowMultipleComponent]
    public sealed class ScreenSplashManager : MonoBehaviour
    {
        #region Constants

        private const string DefaultShaderName =
            "Com.Scheherazade/UI/Screen Splash";
        private const int SplashTypeCount = 1;

        private static readonly ScreenSplashType[] SplashRenderOrder =
        {
            ScreenSplashType.Radial
        };

        private static readonly int SplashColorId =
            Shader.PropertyToID("_SplashColor");
        private static readonly int SplashTypeId =
            Shader.PropertyToID("_SplashType");
        private static readonly int IntensityId =
            Shader.PropertyToID("_Intensity");
        private static readonly int BorderWidthId =
            Shader.PropertyToID("_BorderWidth");
        private static readonly int EdgeSoftnessId =
            Shader.PropertyToID("_EdgeSoftness");
        private static readonly int SplashReachId =
            Shader.PropertyToID("_SplashReach");
        private static readonly int NoiseScaleId =
            Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseSpeedId =
            Shader.PropertyToID("_NoiseSpeed");
        private static readonly int NoiseTimeId =
            Shader.PropertyToID("_NoiseTime");

        #endregion

        #region Properties

        public int ActiveSplashCount => _activeSplashes.Count;

        #endregion

        #region Serialized Fields

        [Header("Overlay")]
        [Tooltip("Shader used by the runtime screen-space splash overlay.")]
        [SerializeField]
        private Shader splashShader;

        [Tooltip("Overlay Canvas order. Negative values keep gameplay HUD above the splash.")]
        [SerializeField]
        private int sortingOrder = -100;

        #endregion

        #region Private Fields

        private readonly List<ActiveSplash> _activeSplashes = new();
        private readonly OverlayLayer[] _overlayLayers =
            new OverlayLayer[SplashTypeCount];
        private readonly Color[] _weightedColors =
            new Color[SplashTypeCount];
        private readonly float[] _totalWeights =
            new float[SplashTypeCount];
        private readonly float[] _dominantWeights =
            new float[SplashTypeCount];
        private readonly ScreenSplashRythm[] _dominantRythms =
            new ScreenSplashRythm[SplashTypeCount];
        private GameObject _overlayRoot;
        private float _noiseTime;
        private int _nextSplashId = 1;

        #endregion

        #region Unity Callbacks

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            DisposeOverlay();
        }

        private void Update()
        {
            AdvanceSplashes(Time.unscaledDeltaTime);
        }

        #endregion

        #region Public Methods

        /// <summary>Plays one screen-splash rythm. Concurrent rhythms are combined.</summary>
        public ScreenSplashHandle PlayRythm(ScreenSplashRythm rythm)
        {
            return PlayRythm(rythm, rythm != null ? rythm.Duration : 0f);
        }

        /// <summary>
        /// Plays one screen-splash rythm over a caller-defined duration while
        /// preserving its normalized intensity envelope.
        /// </summary>
        public ScreenSplashHandle PlayRythm(
            ScreenSplashRythm rythm,
            float duration
        )
        {
            return PlayRythm(rythm, duration, false);
        }

        /// <summary>
        /// Plays a screen-splash rythm over a caller-defined duration. Looping
        /// replays the profile envelope until the supplied duration elapses.
        /// </summary>
        public ScreenSplashHandle PlayRythm(
            ScreenSplashRythm rythm,
            float duration,
            bool loopIntensity
        )
        {
            if (rythm == null || rythm.Duration <= 0f
                || float.IsNaN(duration) || float.IsInfinity(duration)
                || duration <= 0f)
            {
                return ScreenSplashHandle.Invalid;
            }

            var handle = new ScreenSplashHandle(_nextSplashId++);
            _activeSplashes.Add(new ActiveSplash(
                handle.Id,
                rythm,
                duration,
                loopIntensity
            ));
            RefreshOverlay();
            return handle;
        }

        /// <summary>
        /// Plays a looping rythm until its returned handle is stopped.
        /// </summary>
        public ScreenSplashHandle PlayLoopingRythm(ScreenSplashRythm rythm)
        {
            return PlayRythm(rythm, float.MaxValue, true);
        }

        public void Stop(ScreenSplashHandle handle)
        {
            if (!handle.IsValid)
            {
                return;
            }

            for (int index = _activeSplashes.Count - 1;
                 index >= 0;
                 index--)
            {
                if (_activeSplashes[index].Id != handle.Id)
                {
                    continue;
                }

                _activeSplashes.RemoveAt(index);
                RefreshOverlay();
                return;
            }
        }

        public void StopAll()
        {
            _activeSplashes.Clear();
            HideOverlay();
        }

        #endregion

        #region Private Methods

        private void AdvanceSplashes(float deltaTime)
        {
            if (_activeSplashes.Count == 0)
            {
                HideOverlay();
                return;
            }

            float elapsedDelta = Mathf.Max(0f, deltaTime);
            _noiseTime += elapsedDelta;
            for (int index = _activeSplashes.Count - 1;
                 index >= 0;
                 index--)
            {
                ActiveSplash splash = _activeSplashes[index];
                splash.Elapsed += elapsedDelta;
                if (splash.Elapsed >= splash.Duration)
                {
                    _activeSplashes.RemoveAt(index);
                }
            }

            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            if (_activeSplashes.Count == 0)
            {
                HideOverlay();
                return;
            }

            if (!EnsureOverlay())
            {
                return;
            }

            ResetComposition();
            for (int index = 0; index < _activeSplashes.Count; index++)
            {
                ActiveSplash splash = _activeSplashes[index];
                ScreenSplashRythm rythm = splash.Rythm;
                float profileElapsed = splash.LoopIntensity
                    ? Mathf.Repeat(splash.Elapsed, rythm.Duration)
                    : splash.Elapsed / splash.Duration * rythm.Duration;
                float intensity = rythm.EvaluateIntensity(profileElapsed);
                if (intensity <= 0f)
                {
                    continue;
                }

                int splashTypeIndex = GetSplashTypeIndex(rythm.SplashType);
                Color color = rythm.SplashColor;
                float weight = Mathf.Max(0f, color.a) * intensity;
                if (weight <= 0f)
                {
                    continue;
                }

                _weightedColors[splashTypeIndex] += new Color(
                    color.r * weight,
                    color.g * weight,
                    color.b * weight,
                    0f
                );
                _totalWeights[splashTypeIndex] += weight;
                if (weight > _dominantWeights[splashTypeIndex])
                {
                    _dominantWeights[splashTypeIndex] = weight;
                    _dominantRythms[splashTypeIndex] = rythm;
                }
            }

            bool anyLayerActive = false;
            for (int index = 0; index < SplashTypeCount; index++)
            {
                if (!TryRefreshLayer(index))
                {
                    _overlayLayers[index].SetActive(false);
                    continue;
                }

                anyLayerActive = true;
            }

            SetOverlayActive(anyLayerActive);
        }

        private void ResetComposition()
        {
            for (int index = 0; index < SplashTypeCount; index++)
            {
                _weightedColors[index] = Color.clear;
                _totalWeights[index] = 0f;
                _dominantWeights[index] = 0f;
                _dominantRythms[index] = null;
            }
        }

        private bool TryRefreshLayer(int splashTypeIndex)
        {
            float totalWeight = _totalWeights[splashTypeIndex];
            ScreenSplashRythm dominantRythm =
                _dominantRythms[splashTypeIndex];
            if (totalWeight <= 0f || dominantRythm == null)
            {
                return false;
            }

            Color weightedColor = _weightedColors[splashTypeIndex];
            Color splashColor = new Color(
                weightedColor.r / totalWeight,
                weightedColor.g / totalWeight,
                weightedColor.b / totalWeight,
                Mathf.Clamp01(totalWeight)
            );
            OverlayLayer layer = _overlayLayers[splashTypeIndex];
            Material material = layer.Material;
            material.SetColor(SplashColorId, splashColor);
            material.SetFloat(IntensityId, 1f);
            material.SetFloat(
                BorderWidthId,
                dominantRythm.BorderWidth
            );
            material.SetFloat(
                EdgeSoftnessId,
                dominantRythm.EdgeSoftness
            );
            material.SetFloat(
                SplashReachId,
                dominantRythm.SplashReach
            );
            material.SetFloat(
                NoiseScaleId,
                dominantRythm.NoiseScale
            );
            material.SetFloat(
                NoiseSpeedId,
                dominantRythm.NoiseSpeed
            );
            material.SetFloat(NoiseTimeId, _noiseTime);
            layer.SetActive(true);
            return true;
        }

        private bool EnsureOverlay()
        {
            if (IsOverlayReady())
            {
                return true;
            }

            DisposeOverlay();
            Shader shader = splashShader != null
                ? splashShader
                : Shader.Find(DefaultShaderName);
            if (shader == null)
            {
                return false;
            }

            _overlayRoot = new GameObject(
                "[Screen Splash Overlay]",
                typeof(RectTransform)
            )
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer
            };
            _overlayRoot.transform.SetParent(transform, false);

            Canvas canvas = _overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            RectTransform rectTransform = (RectTransform)_overlayRoot.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            for (int renderIndex = 0;
                 renderIndex < SplashRenderOrder.Length;
                 renderIndex++)
            {
                ScreenSplashType splashType =
                    SplashRenderOrder[renderIndex];
                int splashTypeIndex = GetSplashTypeIndex(splashType);
                _overlayLayers[splashTypeIndex] = CreateOverlayLayer(
                    shader,
                    splashType,
                    renderIndex
                );
            }

            _overlayRoot.SetActive(false);
            return true;
        }

        private bool IsOverlayReady()
        {
            if (_overlayRoot == null)
            {
                return false;
            }

            for (int index = 0; index < SplashTypeCount; index++)
            {
                if (_overlayLayers[index] == null
                    || !_overlayLayers[index].IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        private OverlayLayer CreateOverlayLayer(
            Shader shader,
            ScreenSplashType splashType,
            int renderIndex
        )
        {
            var material = new Material(shader)
            {
                name = $"Screen Splash {splashType} Overlay (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat(SplashTypeId, (float)splashType);

            var layerRoot = new GameObject(
                $"[{splashType} Splash Layer]",
                typeof(RectTransform),
                typeof(CanvasRenderer)
            )
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer
            };
            layerRoot.transform.SetParent(_overlayRoot.transform, false);
            layerRoot.transform.SetSiblingIndex(renderIndex);

            RawImage image = layerRoot.AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.color = Color.white;
            image.material = material;
            image.raycastTarget = false;

            RectTransform rectTransform = (RectTransform)layerRoot.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            layerRoot.SetActive(false);
            return new OverlayLayer(layerRoot, material);
        }

        private static int GetSplashTypeIndex(ScreenSplashType splashType)
        {
            return (int)ScreenSplashType.Radial;
        }

        private void HideOverlay()
        {
            for (int index = 0; index < SplashTypeCount; index++)
            {
                _overlayLayers[index]?.SetActive(false);
            }

            SetOverlayActive(false);
        }

        private void SetOverlayActive(bool active)
        {
            if (_overlayRoot != null && _overlayRoot.activeSelf != active)
            {
                _overlayRoot.SetActive(active);
            }
        }

        private void DisposeOverlay()
        {
            bool foundOverlayMaterials = _overlayRoot != null;
            if (foundOverlayMaterials)
            {
                RawImage[] images =
                    _overlayRoot.GetComponentsInChildren<RawImage>(true);
                for (int index = 0; index < images.Length; index++)
                {
                    DestroyUnityObject(images[index].material);
                }
            }

            DestroyUnityObject(_overlayRoot);
            _overlayRoot = null;
            for (int index = 0; index < SplashTypeCount; index++)
            {
                OverlayLayer layer = _overlayLayers[index];
                if (layer == null)
                {
                    continue;
                }

                if (!foundOverlayMaterials)
                {
                    DestroyUnityObject(layer.Material);
                }

                _overlayLayers[index] = null;
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        #endregion

        #region Nested Types

        private sealed class OverlayLayer
        {
            private readonly GameObject _root;

            internal OverlayLayer(GameObject root, Material material)
            {
                _root = root;
                Material = material;
            }

            internal bool IsValid => _root != null && Material != null;
            internal Material Material { get; }

            internal void SetActive(bool active)
            {
                if (_root != null && _root.activeSelf != active)
                {
                    _root.SetActive(active);
                }
            }
        }

        private sealed class ActiveSplash
        {
            public readonly int Id;
            public readonly ScreenSplashRythm Rythm;
            public readonly float Duration;
            public readonly bool LoopIntensity;
            public float Elapsed;

            public ActiveSplash(
                int id,
                ScreenSplashRythm rythm,
                float duration,
                bool loopIntensity
            )
            {
                Id = id;
                Rythm = rythm;
                Duration = duration;
                LoopIntensity = loopIntensity;
            }
        }

        #endregion
    }
}
