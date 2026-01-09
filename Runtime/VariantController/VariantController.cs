using System;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.MVOC
{
    /// <summary>
    /// Base controller for managing and switching between object variants.
    /// </summary>
    /// <typeparam name="TIndex">The type used to index and identify variants.</typeparam>
    /// <typeparam name="TVariant">The variant type implementing IObjectVariant.</typeparam>
    /// <remarks>
    /// This abstract class provides a framework for dynamically switching between different visual or logical
    /// variants of an object. Extend this class and implement EnableCurrentVariant and DisableCurrentVariant
    /// to control how variants are activated and deactivated.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class CharacterController : VariantController&lt;string, CharacterVariant&gt;
    /// {
    ///     protected override void EnableCurrentVariant()
    ///     {
    ///         CurrentVariant.gameObject.SetActive(true);
    ///     }
    ///     
    ///     protected override void DisableCurrentVariant()
    ///     {
    ///         if (CurrentVariant != null)
    ///             CurrentVariant.gameObject.SetActive(false);
    ///     }
    /// }
    /// 
    /// // Usage:
    /// controller.VariantIndex = "ninja"; // Switches to ninja variant
    /// </code>
    /// </example>
    public abstract class VariantController<TIndex, TVariant> : MonoBehaviour
        where TVariant : IObjectVariant<TIndex, TVariant>
        where TIndex : IEquatable<TIndex>
    {
        #region Interfaces
        /// <summary>
        /// Gets the array of all available variants.
        /// </summary>
        public TVariant[] Variants => variants;
        
        /// <summary>
        /// Gets the currently active variant.
        /// </summary>
        public TVariant CurrentVariant => _currentVariant;
        
        /// <summary>
        /// Gets or sets the index of the variant to display.
        /// </summary>
        /// <remarks>
        /// Setting this property triggers a variant switch if the index differs from the current one.
        /// </remarks>
        public TIndex VariantIndex
        {
            get => _variantIndex;
            set
            {
                if (_variantIndex != null && _variantIndex.Equals(value)) return;

                _variantIndex = value;
                _updateVisual = true;
            }
        }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private TVariant[] variants;
        #endregion

        #region Private Fields
        private TVariant _currentVariant;
        private TIndex _variantIndex;
        private bool _updateVisual;
        #endregion

        #region Unity Events
        protected virtual void Awake()
        {
            _updateVisual = true;

            foreach (TVariant variant in Variants)
            {
                if (variant.IsDefault)
                {
                    _currentVariant = variant;
                    _variantIndex = variant.Index;
                    break;
                }
            }
        }
        protected virtual void Update()
        {
            UpdateVariant();
        }
        #endregion

        #region Methods
        /// <summary>
        /// Forces an immediate update of the displayed variant.
        /// </summary>
        public void ForceUpdateVariant()
        {
            _updateVisual = true;
            UpdateVariant();
        }

        /// <summary>
        /// Updates the displayed variant if a change is pending.
        /// </summary>
        public void UpdateVariant()
        {
            if (!_updateVisual) return;
            _updateVisual = false;

            DisableCurrentVariant();
            TVariant compatibleVariant = default;

            if (VariantIndex != null)
            {
                compatibleVariant = Variants.FirstOrDefault(v => VariantIndex.Equals(v.Index));
            }

            if (compatibleVariant == null)
            {
                compatibleVariant = Variants.FirstOrDefault(v => v.IsDefault);
                return;
            }

            _currentVariant = compatibleVariant;

            if (_currentVariant != null)
            {
                EnableCurrentVariant();
            }
        }

        /// <summary>
        /// Enables/activates the current variant. Implement this to define how variants are shown.
        /// </summary>
        protected abstract void EnableCurrentVariant();
        
        /// <summary>
        /// Disables/deactivates the current variant. Implement this to define how variants are hidden.
        /// </summary>
        protected abstract void DisableCurrentVariant();
        #endregion
    }

}