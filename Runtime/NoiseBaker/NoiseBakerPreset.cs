using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// A template asset wrapping a full <see cref="NoiseBakerSettings"/>.
    /// Design the texture in the Noise Generator window (Dev Menu > Tools),
    /// save it as a template, then generate textures at runtime on demand:
    /// <code>
    /// NoiseBakerPreset template = Resources.Load&lt;NoiseBakerPreset&gt;("Templates/Lava");
    /// Texture2D texture = template.Bake();
    /// </code>
    /// Create via Assets > Create > Scheherazade > Noise Generator > Template.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Scheherazade/Noise Generator/Template",
        fileName = "NoiseGeneratorTemplate",
        order = 1200
    )]
    public class NoiseBakerPreset : ScriptableObject
    {
        [Tooltip("Display name shown in the Noise Generator window template dropdown.")]
        public string DisplayName = "Default";

        [Tooltip("Schema version for future parameter migrations.")]
        public int SchemaVersion = 1;

        [Tooltip("Full parameter set for this template.")]
        public NoiseBakerSettings Settings = new NoiseBakerSettings();

        /// <summary>Bakes the template into a Texture2D (main thread).</summary>
        public Texture2D Bake()
        {
            return NoiseBaker.Bake(Settings);
        }

        /// <summary>Bakes asynchronously: sampling on a worker thread, texture written on the main thread.</summary>
        public Task<Texture2D> BakeAsync(CancellationToken cancellationToken = default)
        {
            return NoiseBaker.BakeAsync(Settings, cancellationToken);
        }

        /// <summary>Samples a single normalized point (0..1) of this template.</summary>
        public float Sample(float u, float v)
        {
            return NoiseBaker.Sample(Settings, u, v);
        }

        /// <summary>Samples a single normalized point as a color (respects color mode).</summary>
        public Color SampleColor(float u, float v)
        {
            return NoiseBaker.SampleColor(Settings, u, v);
        }
    }
}
