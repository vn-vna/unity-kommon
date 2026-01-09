using System;

namespace Com.Hapiga.Scheherazade.MVOC
{
    /// <summary>
    /// Interface for objects that can exist in multiple variants controlled by a VariantController.
    /// </summary>
    /// <typeparam name="TIndex">The type used to index variants.</typeparam>
    /// <typeparam name="TVariant">The concrete variant type implementing this interface.</typeparam>
    /// <remarks>
    /// Implement this interface to create objects that can switch between different visual or logical variants
    /// based on an index. Used in conjunction with VariantController for dynamic object variations.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class CharacterVariant : MonoBehaviour, IObjectVariant&lt;string, CharacterVariant&gt;
    /// {
    ///     public VariantController&lt;string, CharacterVariant&gt; Controller { get; set; }
    ///     public string Index => variantName;
    ///     public bool IsDefault => isDefaultVariant;
    ///     
    ///     [SerializeField] private string variantName;
    ///     [SerializeField] private bool isDefaultVariant;
    /// }
    /// </code>
    /// </example>
    public interface IObjectVariant<TIndex, TVariant>
        where TVariant : IObjectVariant<TIndex, TVariant>
        where TIndex : IEquatable<TIndex>
    {
        /// <summary>
        /// Gets the controller managing this variant.
        /// </summary>
        VariantController<TIndex, TVariant> Controller { get; }
        
        /// <summary>
        /// Gets the index identifying this variant.
        /// </summary>
        TIndex Index { get; }

        /// <summary>
        /// Gets whether this is the default variant to display when no specific variant is selected.
        /// </summary>
        bool IsDefault { get; }
    }
}