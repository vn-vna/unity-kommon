using UnityEngine;

namespace Com.Scheherazade.Common.ScreenSplash
{
    public enum ScreenSplashType
    {
        Radial = 0
    }

    [CreateAssetMenu(
        fileName = "ScreenSplashRythm",
        menuName = "Scheherazade/Screen Splash/Rythm"
    )]
    public sealed class ScreenSplashRythm : ScriptableObject
    {
        #region Constants

        private const float MinimumDuration = 0.001f;

        #endregion

        #region Properties

        public ScreenSplashType SplashType => splashType;
        public float Duration => duration;
        public Color SplashColor => splashColor;
        public float BorderWidth => borderWidth;
        public float EdgeSoftness => edgeSoftness;
        public float SplashReach => splashReach;
        public float NoiseScale => noiseScale;
        public float NoiseSpeed => noiseSpeed;

        #endregion

        #region Serialized Fields

        [Header("Style")]
        [Tooltip("Radial gradient used to render this screen splash.")]
        [SerializeField]
        private ScreenSplashType splashType = ScreenSplashType.Radial;

        [Header("Timing")]
        [Tooltip("How long this screen splash plays, in unscaled seconds.")]
        [Min(MinimumDuration)]
        [SerializeField]
        private float duration = 0.3f;

        [Header("Color")]
        [Tooltip("Tint and maximum opacity of the radial screen splash.")]
        [SerializeField]
        private Color splashColor = new Color(1f, 0.22f, 0.08f, 0.55f);

        [Header("Radial Shape")]
        [Tooltip("Controls the dense center radius of the radial gradient.")]
        [Range(0.001f, 0.5f)]
        [SerializeField]
        private float borderWidth = 0.1f;

        [Tooltip("Soft fade between the radial gradient and the clear screen edge.")]
        [Range(0.0001f, 0.25f)]
        [SerializeField]
        private float edgeSoftness = 0.025f;

        [Tooltip("How far the radial gradient reaches from the screen center.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float splashReach = 0.65f;

        [Tooltip("Reserved for compatibility with existing splash assets.")]
        [Min(0.01f)]
        [SerializeField]
        private float noiseScale = 18f;

        [Tooltip("Reserved for compatibility with existing splash assets.")]
        [Min(0f)]
        [SerializeField]
        private float noiseSpeed = 2f;

        [Header("Envelope")]
        [Tooltip("Scales splash opacity from normalized time 0 to 1.")]
        [SerializeField]
        private AnimationCurve intensityOverLifetime = AnimationCurve.EaseInOut(
            0f,
            1f,
            1f,
            0f
        );

        #endregion

        #region Public Methods

        public float EvaluateIntensity(float elapsedSeconds)
        {
            if (duration <= 0f)
            {
                return 0f;
            }

            float normalizedTime = Mathf.Clamp01(elapsedSeconds / duration);
            float intensity = intensityOverLifetime != null
                ? intensityOverLifetime.Evaluate(normalizedTime)
                : 1f;
            float fadeIn = Mathf.SmoothStep(0f, 1f, normalizedTime / 0.15f);
            float fadeOut = 1f - Mathf.SmoothStep(
                0.75f,
                1f,
                normalizedTime
            );
            return Mathf.Clamp01(intensity) * fadeIn * fadeOut;
        }

        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            duration = Mathf.Max(MinimumDuration, duration);
            borderWidth = Mathf.Clamp(borderWidth, 0.001f, 0.5f);
            edgeSoftness = Mathf.Clamp(edgeSoftness, 0.0001f, 0.25f);
            splashReach = Mathf.Clamp01(splashReach);
            noiseScale = Mathf.Max(0.01f, noiseScale);
            noiseSpeed = Mathf.Max(0f, noiseSpeed);
            intensityOverLifetime ??= AnimationCurve.EaseInOut(
                0f,
                1f,
                1f,
                0f
            );
        }
#endif
    }
}
